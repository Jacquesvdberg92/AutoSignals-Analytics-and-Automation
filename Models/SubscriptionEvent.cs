namespace AutoSignals.Models
{
    public class SubscriptionEvent
    {
        public int Id { get; set; }
        public string UserId { get; set; } = string.Empty;
        public string Provider { get; set; } = string.Empty;       // "Stripe" | "GooglePlay" | "System"
        public string EventType { get; set; } = string.Empty;
        public SubscriptionTier? Tier { get; set; }
        public decimal? Amount { get; set; }
        public string? Currency { get; set; }
        public string? ExternalEventId { get; set; }               // Stripe event ID / Play notification ID
        public string? ExternalSubscriptionId { get; set; }
        public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
        public string? RawPayload { get; set; }                    // Full JSON for debugging
    }

    public static class SubscriptionEventTypes
    {
        public const string TrialStarted = "TrialStarted";
        public const string TrialExpired = "TrialExpired";
        public const string TrialWarning = "TrialWarning";
        public const string SubscriptionCreated = "SubscriptionCreated";
        public const string SubscriptionRenewed = "SubscriptionRenewed";
        public const string SubscriptionUpgraded = "SubscriptionUpgraded";
        public const string SubscriptionCancelled = "SubscriptionCancelled";
        public const string SubscriptionExpired = "SubscriptionExpired";
        public const string SubscriptionRenewalWarning = "SubscriptionRenewalWarning";
        public const string PaymentFailed = "PaymentFailed";
        public const string PaymentRecovered = "PaymentRecovered";
        public const string ManualOverride = "ManualOverride";
        public const string GooglePlayPurchase = "GooglePlayPurchase";
        public const string GooglePlayCancelled = "GooglePlayCancelled";
    }
}
