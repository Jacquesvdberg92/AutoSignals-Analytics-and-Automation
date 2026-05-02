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
        /// <summary>
        /// Stages all subscription-activation DB changes (UserData + SubscriptionEvent + connection limit)
        /// without calling SaveChangesAsync. The caller is responsible for the transaction and commit.
        /// UserManager role operations are performed immediately (they use a separate Identity DbContext).
        /// </summary>
        Task StageSubscriptionActivationAsync(string userId, SubscriptionTier tier,
            string provider, string externalSubscriptionId,
            DateTime start, DateTime end);
        Task CancelSubscriptionAsync(string userId, string reason);
        Task ExpireTrialAsync(string userId);
        Task ExpireSubscriptionAsync(string userId);
        Task<UserData?> GetSubscriptionDataAsync(string userId);
    }
}
