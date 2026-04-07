using AutoSignals.Models;
using Telegram.Bot.Types.ReplyMarkups;

namespace AutoSignals.Services
{
    public interface ITelegramNotifier
    {
        Task<bool> NotifyUserAsync(string userId, Order executedOrder, CancellationToken cancellationToken = default);

        /// <summary>
        /// Sends an HTML-formatted message directly to the user's Telegram chat without performing
        /// any notification-settings check (the caller is responsible for that gate).
        /// </summary>
        Task<bool> SendDirectMessageToUserAsync(string userId, string htmlText, CancellationToken cancellationToken = default);

        Task<int?> PostMessageToGroupAsync(
            string message,
            CancellationToken cancellationToken,
            int? replyToMessageId = null,
            int? messageThreadId = null,
            Stream imageStream = null,
            string imageFileName = "AutoSignals.jpg",
            IEnumerable<IEnumerable<InlineKeyboardButton>>? buttons = null);
        Task LoggError(string message);
    }
}
