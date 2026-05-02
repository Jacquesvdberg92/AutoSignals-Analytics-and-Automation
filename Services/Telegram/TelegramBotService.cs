using AutoSignals.Data;
using AutoSignals.Models;
using AutoSignals.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Globalization;
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

    public async Task<bool> SendDirectMessageToUserAsync(string userId, string htmlText, CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AutoSignalsDbContext>();

            var userData = await dbContext.UsersData
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

            if (userData is null || string.IsNullOrWhiteSpace(userData.TelegramId))
            {
                _logger.LogWarning("SendDirectMessageToUserAsync: user {UserId} has no TelegramId.", userId);
                return false;
            }

            var recipient = userData.TelegramId.Trim();

            if (long.TryParse(recipient, NumberStyles.Integer, CultureInfo.InvariantCulture, out var chatId))
            {
                await _botClient.SendTextMessageAsync(
                    chatId: chatId,
                    text: htmlText,
                    parseMode: ParseMode.Html,
                    cancellationToken: cancellationToken);
            }
            else
            {
                await _botClient.SendTextMessageAsync(
                    chatId: recipient,
                    text: htmlText,
                    parseMode: ParseMode.Html,
                    cancellationToken: cancellationToken);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SendDirectMessageToUserAsync failed for user {UserId}.", userId);
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

        // Only respond to private messages — signal scanning is handled by TelegramUserScannerService.
        if (chat.Type != ChatType.Private)
            return;

        var keyboard = new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithUrl("Open App", "https://autosignals.xyz/"),
            }
        });

        await botClient.SendTextMessageAsync(
            chatId: chatId,
            text: "Open AutoSignals or try the Mini App beta:",
            replyMarkup: keyboard,
            cancellationToken: cancellationToken
        );
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



    }

