using AutoSignals.Models;

namespace AutoSignals.Services
{
    public interface ISubscriptionService
    {
        Task<SubscriptionTier> GetTierAsync(string userId);
        Task<bool> IsTrialActiveAsync(string userId);
        Task<bool> CanAccessFeatureAsync(string userId, SubscriptionFeature feature);
        Task StartTrialAsync(string userId);
        Task ActivateSubscriptionAsync(string userId, SubscriptionTier tier,
            string provider, string externalSubscriptionId,
            DateTime start, DateTime end);
        Task CancelSubscriptionAsync(string userId, string reason);
        Task ExpireTrialAsync(string userId);
        Task<UserData?> GetSubscriptionDataAsync(string userId);
    }
}
