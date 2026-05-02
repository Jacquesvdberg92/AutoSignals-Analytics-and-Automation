using AutoSignals.Data;
using AutoSignals.Models;
using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;

namespace AutoSignals.Services
{
    /// <summary>
    /// Shared logic for processing an inbound Telegram message into a signal,
    /// saving it to the database, and triggering orders. Used by both
    /// <see cref="TelegramBotService"/> (bot API) and
    /// <see cref="TelegramUserScannerService"/> (user-account MTProto).
    /// </summary>
    public class TelegramMessageProcessorService
    {
        private static readonly TimeSpan MessageAgeThreshold = TimeSpan.FromMinutes(60);

        private readonly ILogger<TelegramMessageProcessorService> _logger;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly DynamicSignalParserService _parserService;
        private readonly SignalDeduplicationService _deduplicationService;
        private readonly ImageSignalParserService _imageParser;

        public TelegramMessageProcessorService(
            ILogger<TelegramMessageProcessorService> logger,
            IServiceScopeFactory scopeFactory,
            DynamicSignalParserService parserService,
            SignalDeduplicationService deduplicationService,
            ImageSignalParserService imageParser)
        {
            _logger = logger;
            _scopeFactory = scopeFactory;
            _parserService = parserService;
            _deduplicationService = deduplicationService;
            _imageParser = imageParser;
        }

        /// <summary>
        /// Processes a single inbound message. Skips messages that are too old, parses
        /// for a signal, deduplicates, saves to DB, and triggers order creation.
        /// </summary>
        public async Task ProcessMessageAsync(
            string messageText,
            string telegramGroupId,
            DateTime messageDate,
            CancellationToken cancellationToken = default)
        {
            if (DateTime.UtcNow - messageDate > MessageAgeThreshold)
            {
                _logger.LogInformation("Skipping old message from chat {ChatId}.", telegramGroupId);
                return;
            }

            if (string.IsNullOrWhiteSpace(messageText))
                return;

            var groupQueue = _deduplicationService.GetOrCreateGroupQueue(telegramGroupId);

            var signal = await _parserService.ParseSignalAsync(messageText, telegramGroupId, groupQueue);

            if (signal == null)
                return;

            if (_deduplicationService.IsDuplicate(signal, telegramGroupId))
            {
                _logger.LogInformation("Duplicate signal detected for {Symbol} in group {GroupId}, ignoring.", signal.Symbol, telegramGroupId);
                return;
            }

            _logger.LogInformation(
                "Parsed Signal: Symbol={Symbol} Side={Side} Leverage={Leverage} Entry={Entry} SL={Stoploss} TP={TakeProfits} Provider={Provider}",
                signal.Symbol, signal.Side, signal.Leverage, signal.Entry, signal.Stoploss, signal.TakeProfits, signal.Provider);

            _deduplicationService.AddSignal(signal, telegramGroupId);

            var savedSignal = await SaveSignalAsync(signal);
            if (savedSignal != null)
            {
                using var orderScope = _scopeFactory.CreateScope();
                var orderService = orderScope.ServiceProvider.GetRequiredService<OrderService>();
                _logger.LogInformation("Calling CreateOrdersForActiveUsers...");
                await orderService.CreateOrdersForActiveUsers(savedSignal);
                _logger.LogInformation("CreateOrdersForActiveUsers called successfully.");
            }
        }

        /// <summary>
        /// Processes a chart image from a Telegram message. Looks up providers registered
        /// to <paramref name="telegramGroupId"/> with <c>UseImageParsing = true</c>, runs
        /// the vision-AI parser, then deduplicates, saves and triggers orders — the same
        /// pipeline as <see cref="ProcessMessageAsync"/>.
        /// </summary>
        public async Task ProcessImageMessageAsync(
            byte[] imageBytes,
            string telegramGroupId,
            DateTime messageDate,
            CancellationToken cancellationToken = default)
        {
            if (DateTime.UtcNow - messageDate > MessageAgeThreshold)
            {
                _logger.LogInformation("Skipping old image message from chat {ChatId}.", telegramGroupId);
                return;
            }

            if (imageBytes == null || imageBytes.Length == 0)
                return;

            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AutoSignalsDbContext>();

            var imageProviders = await dbContext.SignalProviders
                .Where(p => p.IsActive && p.UseImageParsing && p.TelegramGroupId == telegramGroupId)
                .ToListAsync(cancellationToken);

            if (imageProviders.Count == 0)
            {
                _logger.LogDebug("ImageProcessor: no image-parsing provider registered for group {ChatId}.", telegramGroupId);
                return;
            }

            var groupQueue = _deduplicationService.GetOrCreateGroupQueue(telegramGroupId);

            foreach (var provider in imageProviders)
            {
                var signal = await _imageParser.ParseFromImageAsync(imageBytes, provider.Name, provider.ImageParsingPrompt, cancellationToken);
                if (signal == null)
                    continue;

                if (_deduplicationService.IsDuplicate(signal, telegramGroupId))
                {
                    _logger.LogInformation("Duplicate image signal for {Symbol} in group {GroupId}, ignoring.", signal.Symbol, telegramGroupId);
                    continue;
                }

                _logger.LogInformation(
                    "Image Signal: Symbol={Symbol} Side={Side} Leverage={Leverage} Entry={Entry} SL={Stoploss} TP={TakeProfits} Provider={Provider}",
                    signal.Symbol, signal.Side, signal.Leverage, signal.Entry, signal.Stoploss, signal.TakeProfits, signal.Provider);

                _deduplicationService.AddSignal(signal, telegramGroupId);

                var savedSignal = await SaveSignalAsync(signal);
                if (savedSignal != null)
                {
                    using var orderScope = _scopeFactory.CreateScope();
                    var orderService = orderScope.ServiceProvider.GetRequiredService<OrderService>();
                    await orderService.CreateOrdersForActiveUsers(savedSignal);
                }

                break; // one signal per image is enough
            }
        }

        private async Task<Signal?> SaveSignalAsync(Signal signal)
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AutoSignalsDbContext>();
            try
            {
                var generalPrice = await dbContext.GeneralAssetPrices
                    .Where(gp => gp.Symbol == signal.Symbol)
                    .Select(gp => gp.Price)
                    .FirstOrDefaultAsync();

                if (generalPrice == 0)
                {
                    _logger.LogError("General price for symbol {Symbol} not found.", signal.Symbol);
                    return null;
                }

                var lowerBound = generalPrice * 0.85m;
                var upperBound = generalPrice * 1.15m;

                if (signal.Entry < (float)lowerBound || signal.Entry > (float)upperBound)
                {
                    var deviation = Math.Abs(signal.Entry - (float)generalPrice) / (float)generalPrice * 100;
                    _logger.LogWarning(
                        "Signal entry price {Entry} is not within 15% margin of general price {Price} (deviation: {Deviation:F2}%). Symbol: {Symbol}, Provider: {Provider}. Signal will not be saved.",
                        signal.Entry, generalPrice, deviation, signal.Symbol, signal.Provider);
                    return null;
                }

                dbContext.Signals.Add(signal);
                await dbContext.SaveChangesAsync();

                var signalPerformance = new SignalPerformance
                {
                    SignalId = signal.Id,
                    Status = "Pending",
                    StartTime = signal.Time,
                    HighPrice = signal.Entry,
                    LowPrice = signal.Entry,
                    ProfitLoss = 0,
                    TakeProfitCount = signal.TakeProfits.Split(',').Length,
                    TakeProfitsAchieved = 0,
                    Notes = string.Empty,
                    AchievedTakeProfits = string.Empty,
                };

                dbContext.SignalPerformances.Add(signalPerformance);
                await dbContext.SaveChangesAsync();

                var signalPredictionService = scope.ServiceProvider.GetRequiredService<SignalPredictionService>();
                var prediction = await signalPredictionService.GeneratePredictionAsync(signal);

                _logger.LogInformation("Signal and SignalPerformance saved to the database successfully.");

                if (prediction != null)
                {
                    _logger.LogInformation(
                        "Prediction generated for signal {SignalId}. Confidence: {ConfidenceScore}%, TPs: {TpProbabilities}.",
                        signal.Id, prediction.ConfidenceScore, prediction.TpProbabilities);
                }

                return signal;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving signal to the database.");
                return null;
            }
        }
    }
}
