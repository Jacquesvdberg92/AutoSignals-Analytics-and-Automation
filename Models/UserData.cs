namespace AutoSignals.Models
{
    public class UserData
    {
        public string Id { get; set; }
        public string? NickName { get; set; }
        public string? TelegramId { get; set; }
        public string? TelegramNotifications { get; set; }

        public int? ExchangeId { get; set; }
        public string? ApiKey { get; set; }
        public string? ApiSecret { get; set; }
        public string? ApiPassword { get; set; }
        public string? ApiTestResult { get; set; }

        public string? X { get; set; }
        public string? Instagram { get; set; }
        public string? Facebook { get; set; }

        public string? StartBalance { get; set; }
        public string? SubscriptionActive { get; set; }

        public string? Notes { get; set; }
        public DateTime Time { get; set; }
    }
}
