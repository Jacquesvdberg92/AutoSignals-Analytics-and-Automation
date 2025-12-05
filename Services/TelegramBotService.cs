using AutoSignals.Data;
using AutoSignals.Models;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

public class TelegramBotService : BackgroundService
{
    private readonly ILogger<TelegramBotService> _logger;
    private readonly ITelegramBotClient _botClient;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TelegramGroupsOptions _telegramGroupsOptions;


    private readonly ConcurrentDictionary<string, Queue<Signal>> _wolfxLastThreeEntries = new();
    private readonly ConcurrentDictionary<string, Queue<Signal>> _alexLastThreeEntries = new();
    private readonly ConcurrentDictionary<string, Queue<Signal>> _russianLastThreeEntries = new();
    private readonly ConcurrentDictionary<string, Queue<Signal>> _coincoachLastThreeEntries = new();
    private readonly ConcurrentDictionary<string, Queue<Signal>> _scalpingLastThreeEntries = new();
    private readonly ConcurrentDictionary<string, Queue<Signal>> _mastersLastThreeEntries = new();
    private readonly ConcurrentDictionary<string, Queue<Signal>> _bybitproLastThreeEntries = new();
    private readonly ConcurrentDictionary<string, Queue<Signal>> _andrewLastThreeEntries = new();
    private readonly ConcurrentDictionary<string, Queue<Signal>> _cicLastThreeEntries = new();
    private readonly ConcurrentDictionary<string, Queue<Signal>> _amanLastThreeEntries = new();
    private readonly ConcurrentDictionary<string, Queue<Signal>> _alwayswinLastThreeEntries = new();



    private static readonly float StoplossPercent = 10.0F;

    public TelegramBotService(
        ILogger<TelegramBotService> logger,
        ITelegramBotClient botClient,
        IServiceScopeFactory scopeFactory,
        IOptions<TelegramGroupsOptions> telegramGroupsOptions)
    {
        _logger = logger;
        _botClient = botClient;
        _scopeFactory = scopeFactory;
        _telegramGroupsOptions = telegramGroupsOptions.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Example of using the scope factory to create a scope
        using (var scope = _scopeFactory.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AutoSignalsDbContext>();
            // Use dbContext here
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
        // Only handle messages
        if (update.Type != UpdateType.Message)
            return;

        var chat = update.Message!.Chat;
        var chatId = chat.Id;

        // If this is a private chat (user), send "Coming soon" and website link
        if (chat.Type == ChatType.Private)
        {
            await botClient.SendTextMessageAsync(
                chatId,
                "🚀 Coming soon!\n\nVisit [AutoSignals.xyz](https://AutoSignals.xyz) for updates.",
                parseMode: ParseMode.Markdown,
                cancellationToken: cancellationToken
            );
            return;
        }

        // --- Existing group/channel logic below ---

        // Define the threshold for old messages (e.g., 60 minutes)
        var messageAgeThreshold = TimeSpan.FromMinutes(60);
        var messageDate = update.Message.Date;
        var currentDate = DateTime.UtcNow;

        // Check if the message is older than the threshold
        if (currentDate - messageDate > messageAgeThreshold)
        {
            _logger.LogInformation($"Skipping old message from chat {chatId}.");
            return;
        }

        var parsers = new List<Func<string, Signal?>>
    {
        ParseBybitPro,
        ParseBinanceMaster,
        ParseAlexFredman,
        ParseScalping300,
        ParseCoinCoach,
        ParseFedRussianInsider,
        ParseWolfX,
        ParseCryptoAndrew,
        ParseCryptoInnerCircle,
        ParseCryptoAman,
        ParseAlwaysWin
    };

        string? messageText = null;

        if (update.Message.Text != null)
        {
            messageText = update.Message.Text;
        }
        else if (update.Message.Photo != null && update.Message.Caption != null)
        {
            messageText = update.Message.Caption;
        }

        _logger.LogInformation($"Received a '{messageText}' message in chat {chatId}.");

        Signal? signal = null;
        foreach (var parser in parsers)
        {
            signal = parser(messageText);
            if (signal != null)
            {
                break;
            }
        }

        if (signal != null)
        {
            _logger.LogInformation($"Parsed Signal: \nSymbol: {signal.Symbol} \nSide: {signal.Side} \nLeverage: {signal.Leverage} \nEntry: {signal.Entry} \nStoploss: {signal.Stoploss} \nTake Profit: {signal.TakeProfits} \nProvider: {signal.Provider}");
            var savedSignal = await SaveSignalAsync(signal);
            if (savedSignal != null)
            {
                using (var scope = _scopeFactory.CreateScope())
                {
                    var orderService = scope.ServiceProvider.GetRequiredService<OrderService>();
                    _logger.LogInformation("Calling CreateOrdersForActiveUsers...");
                    await orderService.CreateOrdersForActiveUsers(savedSignal);
                    _logger.LogInformation("CreateOrdersForActiveUsers called successfully.");
                }
            }
        }
    }

    private Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
    {
        _logger.LogError(exception, "An error occurred while handling the update.");
        return Task.CompletedTask;
    }

    //////////////////////////////// <-- BybitPro Signal Parser --> ////////////////////////////////
    private Signal? ParseBybitPro(string message)
    {
        return BybitProSignalParser.Parse(
            message,
            StoplossPercent,
            _logger,
            _bybitproLastThreeEntries
        );
    }


    //////////////////////////////// <-- Binance Masters Signal Parser --> ////////////////////////////////
    private Signal? ParseBinanceMaster(string message)
    {
        return BinanceMasterSignalParser.Parse(
            message,
            _logger,
            _mastersLastThreeEntries
        );
    }


    //////////////////////////////// <-- Alex Fredman Signal Parser --> ////////////////////////////////
    private Signal? ParseAlexFredman(string message)
    {
        return AlexFredmanSignalParser.Parse(
            message,
            StoplossPercent,
            _logger,
            _alexLastThreeEntries
        );
    }

    //////////////////////////////// <-- Scalping300 Signal Parser --> ////////////////////////////////
    private Signal? ParseScalping300(string message)
    {
        return Scalping300SignalParser.Parse(
            message,
            _logger,
            _scalpingLastThreeEntries
        );
    }

    //////////////////////////////// <-- Coin Coach Signal Parser --> ////////////////////////////////
    private Signal? ParseCoinCoach(string message)
    {
        return CoinCoachSignalParser.Parse(
            message,
            _logger,
            _coincoachLastThreeEntries
        );
    }

    //////////////////////////////// <-- Fed Russian Insider Signal Parser --> ////////////////////////////////
    private Signal? ParseFedRussianInsider(string message)
    {
        return FedRussianInsiderSignalParser.Parse(
            message,
            _logger,
            _russianLastThreeEntries
        );
    }

    //////////////////////////////// <-- WolfX Signal Parser --> ////////////////////////////////
    private Signal? ParseWolfX(string message)
    {
        return WolfXSignalParser.Parse(
            message,
            _logger,
            _wolfxLastThreeEntries
        );
    }


    //////////////////////////////// <-- Andrew Parser --> ////////////////////////////////
    private Signal? ParseCryptoAndrew(string message)
    {
        return CryptoAndrewSignalParser.Parse(
            message,
            StoplossPercent,
            _logger,
            _andrewLastThreeEntries
        );
    }


    /////////////////////////////// <-- CryptoInnerCircle Parser --> ////////////////////////////////
    private Signal? ParseCryptoInnerCircle(string message)
    {
        return CryptoInnerCircleSignalParser.Parse(
            message,
            _logger,
            _cicLastThreeEntries
        );
    }



    /////////////////////////////// <-- Crypto Aman Parser --> ////////////////////////////////
    private Signal? ParseCryptoAman(string message)
    {
        return CryptoAmanSignalParser.Parse(
            message,
            _logger,
            _amanLastThreeEntries
        );
    }

    /////////////////////////////// <-- Always Win Parser --> ////////////////////////////////
    private Signal? ParseAlwaysWin(string message)
    {
        return AlwaysWinSignalParser.Parse(
            message,
            _logger,
            _amanLastThreeEntries
        );
    }



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
