using AutoSignals.Models;
using Telegram.Bot.Types.ReplyMarkups;

namespace AutoSignals.Services
{
    public class DisabledTelegramNotifier : ITelegramNotifier
    {
        private readonly ILogger<DisabledTelegramNotifier> _logger;

        public DisabledTelegramNotifier(ILogger<DisabledTelegramNotifier> logger)
        {
            _logger = logger;
        }

        public Task<bool> NotifyUserAsync(string userId, Order executedOrder, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Telegram notifications are disabled. Skipping user notification for user {UserId}, order {OrderId}.", userId, executedOrder?.Id);
            return Task.FromResult(false);
        }

        public Task<bool> SendDirectMessageToUserAsync(string userId, string htmlText, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Telegram integration is disabled. Direct message to user {UserId} was skipped.", userId);
            return Task.FromResult(false);
        }

        public Task<int?> PostMessageToGroupAsync(
            string message,
            CancellationToken cancellationToken,
            int? replyToMessageId = null,
            int? messageThreadId = null,
            Stream imageStream = null,
            string imageFileName = "AutoSignals.jpg",
            IEnumerable<IEnumerable<InlineKeyboardButton>>? buttons = null)
        {
            _logger.LogInformation("Telegram integration is disabled. Group message was skipped.");
            return Task.FromResult<int?>(null);
        }

        public Task LoggError(string message)
        {
            _logger.LogWarning("Telegram integration is disabled. Error notification was skipped. Message: {Message}", message);
            return Task.CompletedTask;
        }
    }
}
