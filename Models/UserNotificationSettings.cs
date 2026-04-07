namespace AutoSignals.Models
{
    public class UserNotificationSettings
    {
        public int Id { get; set; }
        public string UserId { get; set; } = default!;

        // Trading events — Telegram
        public bool TelegramOrderExecuted { get; set; } = true;
        public bool TelegramTakeProfitHit { get; set; } = true;
        public bool TelegramStopLossHit { get; set; } = true;

        // Trading events — Email
        public bool EmailOrderExecuted { get; set; } = false;
        public bool EmailTakeProfitHit { get; set; } = false;
        public bool EmailStopLossHit { get; set; } = false;

        // Marketing — Email only
        public bool EmailMarketing { get; set; } = true;

        // Platform updates / new features — Email only
        public bool EmailUpdates { get; set; } = true;

        // System emails (password reset, account security, etc.) are ALWAYS sent and cannot be disabled.

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
