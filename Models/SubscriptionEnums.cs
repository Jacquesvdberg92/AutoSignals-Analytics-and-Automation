namespace AutoSignals.Models
{
    public enum SubscriptionTier
    {
        Freemium = 0,
        Pro = 1,
        VIP = 2
    }

    public enum SubscriptionStatus
    {
        Trial = 0,
        Active = 1,
        PastDue = 2,
        Cancelled = 3,
        Expired = 4
    }
}
