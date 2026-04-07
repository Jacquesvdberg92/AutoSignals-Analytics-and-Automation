using AutoSignals.Models;

namespace AutoSignals.Services
{
    public interface INotificationService
    {
        /// <summary>
        /// Routes an order-execution notification to Telegram and/or Email based on the user's
        /// <see cref="UserNotificationSettings"/>.  The event type (OrderExecuted, TakeProfitHit,
        /// StopLossHit) is inferred automatically from <paramref name="order"/>.Description.
        /// </summary>
        Task NotifyOrderExecutedAsync(string userId, Order order, CancellationToken cancellationToken = default);
    }
}
