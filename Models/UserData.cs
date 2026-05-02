namespace AutoSignals.Models
{
    public class UserData
    {
        public string Id { get; set; }
        public string? NickName { get; set; }
        public string? TelegramId { get; set; }
        public string? TelegramNotifications { get; set; }

        public string? EmailNotifications { get; set; }

        public int? ExchangeId { get; set; }
        public string? ApiKey { get; set; }
        public string? ApiSecret { get; set; }
        public string? ApiPassword { get; set; }
        public string? ApiTestResult { get; set; }

        public string? X { get; set; }
        public string? Instagram { get; set; }
        public string? Facebook { get; set; }

        public string? StartBalance { get; set; }

        // Subscription tier
        public SubscriptionTier SubscriptionTier { get; set; } = SubscriptionTier.Freemium;
        public SubscriptionStatus SubscriptionStatus { get; set; } = SubscriptionStatus.Trial;

        // Trial
        public DateTime? TrialEndDate { get; set; }

        // Active paid subscription dates
        public DateTime? SubscriptionStartDate { get; set; }
        public DateTime? SubscriptionEndDate { get; set; }

        // Admin-granted permanent access — bypasses all automated expiry
        public bool NeverExpires { get; set; } = false;

        // Payment provider — supports NOWPayments (crypto), Manual overrides
        public string? SubscriptionProvider { get; set; }       // "NOWPayments" | "Manual"
        public string? ExternalSubscriptionId { get; set; }     // provider-agnostic payment reference

        public DateOnly? BirthDate { get; set; }

        public string? Notes { get; set; }
        public DateTime Time { get; set; }
    }
}
