namespace AutoSignals.Models
{
    public class SubscriptionPlan
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;           // "Pro Monthly", "VIP Annual" etc.
        public SubscriptionTier Tier { get; set; }
        public decimal MonthlyPrice { get; set; }
        public string Currency { get; set; } = "USD";
        public bool IsAnnual { get; set; } = false;
        public bool IsActive { get; set; } = true;
        public string? FeaturesJson { get; set; }                  // JSON blob for display
    }
}
