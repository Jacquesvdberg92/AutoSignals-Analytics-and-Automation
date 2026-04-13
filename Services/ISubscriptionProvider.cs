namespace AutoSignals.Services
{
    public interface ISubscriptionProvider
    {
        string ProviderName { get; }
        Task<string> CreateCheckoutSessionAsync(string userId, int planId, string successUrl, string cancelUrl);
        Task<string> GetBillingPortalUrlAsync(string userId, string returnUrl);
        Task CancelSubscriptionAsync(string stripeSubscriptionId);
    }
}
