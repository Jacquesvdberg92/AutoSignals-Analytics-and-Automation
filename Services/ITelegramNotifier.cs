using AutoSignals.Models;
using Telegram.Bot.Types.ReplyMarkups;

namespace AutoSignals.Services
{
    public interface ITelegramNotifier
    {
        Task<bool> NotifyUserAsync(string userId, Order executedOrder, CancellationToken cancellationToken = default);
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
