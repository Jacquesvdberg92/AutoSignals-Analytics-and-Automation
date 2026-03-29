using AutoSignals.Data;
using AutoSignals.Models;
using AutoSignals.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

public class TelegramBotService : BackgroundService, ITelegramNotifier
{
    private readonly ILogger<TelegramBotService> _logger;
    private readonly ITelegramBotClient _botClient;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TelegramGroupsOptions _telegramGroupsOptions;
    private readonly SignalDeduplicationService _deduplicationService;

    private static readonly float StoplossPercent = 10.0F;

    public TelegramBotService(
        ILogger<TelegramBotService> logger,
        ITelegramBotClient botClient,
        IServiceScopeFactory scopeFactory,
        IOptions<TelegramGroupsOptions> telegramGroupsOptions,
        SignalDeduplicationService deduplicationService)
    {
        _logger = logger;
        _botClient = botClient;
        _scopeFactory = scopeFactory;
        _telegramGroupsOptions = telegramGroupsOptions.Value;
        _deduplicationService = deduplicationService;
    }
    public async Task<bool> NotifyUserAsync(string userId, Order executedOrder, CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AutoSignalsDbContext>();

            var user = await dbContext.UsersData
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

            if (user is null)
            {
                _logger.LogWarning("NotifyUserAsync: user {UserId} not found.", userId);
                return false;
            }

            // Treat "1"/"true"/"yes"/"on" (case-insensitive) as enabled.
            var enabled = !string.IsNullOrWhiteSpace(user.TelegramNotifications)
                && (user.TelegramNotifications.Equals("1", StringComparison.OrdinalIgnoreCase)
                    || user.TelegramNotifications.Equals("true", StringComparison.OrdinalIgnoreCase)
                    || user.TelegramNotifications.Equals("yes", StringComparison.OrdinalIgnoreCase)
                    || user.TelegramNotifications.Equals("on", StringComparison.OrdinalIgnoreCase));

            if (!enabled)
                return false;

            var recipient = user.TelegramId?.Trim();
            if (string.IsNullOrWhiteSpace(recipient))
            {
                _logger.LogWarning("NotifyUserAsync: user {UserId} has no TelegramId/username.", userId);
                return false;
            }

            var text =
                $"<b>Order executed</b>\n" +
                $"<b>Symbol:</b> {executedOrder.Symbol}\n" +
                $"<b>Side:</b> {executedOrder.Side}\n" +
                $"<b>Description:</b> {executedOrder.Description}\n" +
                $"<b>Price:</b> {(executedOrder.Price?.ToString() ?? "N/A")}\n" +
                $"<b>Size:</b> {executedOrder.Size}\n" +
                $"<b>Leverage:</b> {executedOrder.Leverage}\n" +
                $"<b>Status:</b> {executedOrder.Status}\n" +
                $"<b>Time (UTC):</b> {executedOrder.Time:yyyy-MM-dd HH:mm:ss}";

            // If it's numeric, treat as chatId; otherwise treat as username (e.g. "@myuser")
            if (long.TryParse(recipient, NumberStyles.Integer, CultureInfo.InvariantCulture, out var chatId))
            {
                await _botClient.SendTextMessageAsync(
                    chatId: chatId,
                    text: text,
                    parseMode: ParseMode.Html,
                    cancellationToken: cancellationToken);

                return true;
            }

            await _botClient.SendTextMessageAsync(
                chatId: recipient,
                text: text,
                parseMode: ParseMode.Html,
                cancellationToken: cancellationToken);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "NotifyUserAsync failed for user {UserId}, order {OrderId}.", userId, executedOrder?.Id);
            return false;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using (var scope = _scopeFactory.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AutoSignalsDbContext>();
        }

        _botClient.StartReceiving(
            HandleUpdateAsync,
            HandleErrorAsync,
            new ReceiverOptions { AllowedUpdates = Array.Empty<UpdateType>() },
            cancellationToken: stoppingToken
        );

        _logger.LogInformation("Telegram Bot started.");
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    public async Task<int?> PostMessageToGroupAsync(
    string message,
    CancellationToken cancellationToken,
    int? replyToMessageId = null,
    int? messageThreadId = null,
    Stream imageStream = null,
    string imageFileName = "AutoSignals.jpg",
    IEnumerable<IEnumerable<InlineKeyboardButton>>? buttons = null)
    {
        try
        {
            IReplyMarkup? replyMarkup = null;
            if (buttons != null)
            {
                replyMarkup = new InlineKeyboardMarkup(buttons);
            }

            if (imageStream != null)
            {
                imageStream.Position = 0;
                var response = await _botClient.SendPhotoAsync(
                    chatId: _telegramGroupsOptions.MessageGroupId,
                    photo: new InputFileStream(imageStream, imageFileName),
                    caption: message,
                    parseMode: ParseMode.Html,
                    replyToMessageId: replyToMessageId,
                    replyMarkup: replyMarkup,
                    cancellationToken: cancellationToken
                );
                return response.MessageId;
            }
            else
            {
                var response = await _botClient.SendTextMessageAsync(
                    chatId: _telegramGroupsOptions.MessageGroupId,
                    text: message,
                    parseMode: ParseMode.Html,
                    replyToMessageId: replyToMessageId,
                    replyMarkup: replyMarkup,
                    cancellationToken: cancellationToken
                );
                return response.MessageId;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error sending message to group {_telegramGroupsOptions.MessageGroupId}");
            return null;
        }
    }



    public async Task LoggError(string message)
    {
        try
        {
            await _botClient.SendTextMessageAsync(
                chatId: _telegramGroupsOptions.ErrorLogGroupId,
                text: message,
                parseMode: ParseMode.Html,
                cancellationToken: CancellationToken.None
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex.Message);
        }
    }

    private async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
    {
        // Accept group messages, supergroup messages, and channel posts (including forwards).
        var message = update.Type switch
        {
            UpdateType.Message => update.Message,
            UpdateType.ChannelPost => update.ChannelPost,
            UpdateType.EditedMessage => update.EditedMessage,
            UpdateType.EditedChannelPost => update.EditedChannelPost,
            _ => null
        };

        if (message is null)
            return;

        var chat = message.Chat;
        var chatId = chat.Id;

        // Private chat handling remains the same...
        if (chat.Type == ChatType.Private)
        {
            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithUrl("Open App", "https://autosignals.xyz/") }
            });

            await botClient.SendTextMessageAsync(
                chatId: chatId,
                text: "Open App:",
                replyMarkup: keyboard,
                cancellationToken: cancellationToken
            );

            return;
        }

        // Check for old messages...
        var messageAgeThreshold = TimeSpan.FromMinutes(60);
        var messageDate = message.Date;
        var currentDate = DateTime.UtcNow;

        if (currentDate - messageDate > messageAgeThreshold)
        {
            _logger.LogInformation($"Skipping old message from chat {chatId}.");
            return;
        }

        string? messageText = null;

        if (!string.IsNullOrEmpty(message.Text))
        {
            messageText = message.Text;
        }
        else if (message.Photo != null && !string.IsNullOrEmpty(message.Caption))
        {
            messageText = message.Caption;
        }

        if (string.IsNullOrEmpty(messageText))
            return;

        // Use dynamic parser
        using (var scope = _scopeFactory.CreateScope())
        {
            var parserService = scope.ServiceProvider.GetRequiredService<DynamicSignalParserService>();

            var telegramGroupId = chatId.ToString();
            var groupQueue = _deduplicationService.GetOrCreateGroupQueue(telegramGroupId);

            var signal = await parserService.ParseSignalAsync(
                messageText,
                chatId.ToString(),
                groupQueue);

            if (signal != null)
            {
                if (!_deduplicationService.IsDuplicate(signal, telegramGroupId))
                {
                    _logger.LogInformation($"Parsed Signal: \nSymbol: {signal.Symbol} \nSide: {signal.Side} \nLeverage: {signal.Leverage} \nEntry: {signal.Entry} \nStoploss: {signal.Stoploss} \nTake Profit: {signal.TakeProfits} \nProvider: {signal.Provider}");

                    _deduplicationService.AddSignal(signal, telegramGroupId);

                    var savedSignal = await SaveSignalAsync(signal);
                    if (savedSignal != null)
                    {
                        using (var orderScope = _scopeFactory.CreateScope())
                        {
                            var orderService = orderScope.ServiceProvider.GetRequiredService<OrderService>();
                            _logger.LogInformation("Calling CreateOrdersForActiveUsers...");
                            await orderService.CreateOrdersForActiveUsers(savedSignal);
                            _logger.LogInformation("CreateOrdersForActiveUsers called successfully.");
                        }
                    }
                }
                else
                {
                    _logger.LogInformation($"Duplicate signal detected for {signal.Symbol} in group {telegramGroupId}, ignoring.");
                }
            }
        }
    }


private ConcurrentDictionary<string, Queue<Signal>> GetOrCreateLastThreeEntries(string chatId)
    {
        // You'll need to manage these dictionaries differently now
        // Consider using a factory or service to manage them
        // This is a simplified version
        return new ConcurrentDictionary<string, Queue<Signal>>();
    }

    private Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
    {
        _logger.LogError(exception, "An error occurred while handling the update.");
        return Task.CompletedTask;
    }

    //////////////////////////////// <-- BybitPro Signal Parser --> ////////////////////////////////
    //private Signal? ParseBybitPro(string message)
    //{
    //    return BybitProSignalParser.Parse(
    //        message,
    //        StoplossPercent,
    //        _logger,
    //        _bybitproLastThreeEntries
    //    );
    //}


    //////////////////////////////// <-- Binance Masters Signal Parser --> ////////////////////////////////
    //private Signal? ParseBinanceMaster(string message)
    //{
    //    return BinanceMasterSignalParser.Parse(
    //        message,
    //        _logger,
    //        _mastersLastThreeEntries
    //    );
    //}


    //////////////////////////////// <-- Alex Fredman Signal Parser --> ////////////////////////////////
    //private Signal? ParseAlexFredman(string message)
    //{
    //    return AlexFredmanSignalParser.Parse(
    //        message,
    //        StoplossPercent,
    //        _logger,
    //        _alexLastThreeEntries
    //    );
    //}

    //////////////////////////////// <-- Scalping300 Signal Parser --> ////////////////////////////////
    //private Signal? ParseScalping300(string message)
    //{
    //    return Scalping300SignalParser.Parse(
    //        message,
    //        _logger,
    //        _scalpingLastThreeEntries
    //    );
    //}

    //////////////////////////////// <-- Coin Coach Signal Parser --> ////////////////////////////////
    //private Signal? ParseCoinCoach(string message)
    //{
    //    return CoinCoachSignalParser.Parse(
    //        message,
    //        _logger,
    //        _coincoachLastThreeEntries
    //    );
    //}

    //////////////////////////////// <-- Fed Russian Insider Signal Parser --> ////////////////////////////////
    //private Signal? ParseFedRussianInsider(string message)
    //{
    //    return FedRussianInsiderSignalParser.Parse(
    //        message,
    //        _logger,
    //        _russianLastThreeEntries
    //    );
    //}

    //////////////////////////////// <-- WolfX Signal Parser --> ////////////////////////////////
    //private Signal? ParseWolfX(string message)
    //{
    //    return WolfXSignalParser.Parse(
    //        message,
    //        _logger,
    //        _wolfxLastThreeEntries
    //    );
    //}


    //////////////////////////////// <-- Andrew Parser --> ////////////////////////////////
    //private Signal? ParseCryptoAndrew(string message)
    //{
    //    return CryptoAndrewSignalParser.Parse(
    //        message,
    //        StoplossPercent,
    //        _logger,
    //        _andrewLastThreeEntries
    //    );
    //}


    /////////////////////////////// <-- CryptoInnerCircle Parser --> ////////////////////////////////
    //private Signal? ParseCryptoInnerCircle(string message)
    //{
    //    return CryptoInnerCircleSignalParser.Parse(
    //        message,
    //        _logger,
    //        _cicLastThreeEntries
    //    );
    //}



    /////////////////////////////// <-- Crypto Aman Parser --> ////////////////////////////////
    //private Signal? ParseCryptoAman(string message)
    //{
    //    return CryptoAmanSignalParser.Parse(
    //        message,
    //        _logger,
    //        _amanLastThreeEntries
    //    );
    //}

    /////////////////////////////// <-- Always Win Parser --> ////////////////////////////////
    //private Signal? ParseAlwaysWin(string message)
    //{
    //    return AlwaysWinSignalParser.Parse(
    //        message,
    //        _logger,
    //        _amanLastThreeEntries
    //    );
    //}



    private async Task<Signal?> SaveSignalAsync(Signal signal)
    {
        using (var scope = _scopeFactory.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AutoSignalsDbContext>();
            try
            {
                // Retrieve the general price for the symbol from the database
                var generalPrice = await dbContext.GeneralAssetPrices
                    .Where(gp => gp.Symbol == signal.Symbol)
                    .Select(gp => gp.Price)
                    .FirstOrDefaultAsync();

                if (generalPrice == 0)
                {
                    _logger.LogError($"General price for symbol {signal.Symbol} not found.");
                    return null;
                }

                // Check if the signal's entry price is within a 5% margin of the general price
                var lowerBound = generalPrice * 0.95m;
                var upperBound = generalPrice * 1.05m;

                if (signal.Entry < (float)lowerBound || signal.Entry > (float)upperBound)
                {
                    _logger.LogError($"Signal entry price {signal.Entry} is not within 5% margin of the general price {generalPrice}.");
                    return null;
                }

                // Add the signal to the database
                dbContext.Signals.Add(signal);
                await dbContext.SaveChangesAsync();

                // At this point, signal.Id is populated with the generated value

                // Create and save SignalPerformance entry
                var signalPerformance = new SignalPerformance
                {
                    SignalId = signal.Id, // Use the populated Id here
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

                _logger.LogInformation("Signal and SignalPerformance saved to the database successfully.");

                return signal; // Return the saved signal with its Id
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error saving signal to the database: {ex.Message}");
                return null;
            }
        }
    }

}
