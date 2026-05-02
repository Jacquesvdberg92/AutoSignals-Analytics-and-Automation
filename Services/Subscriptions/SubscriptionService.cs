using AutoSignals.Data;
using AutoSignals.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AutoSignals.Services
{
    public class SubscriptionService : ISubscriptionService
    {
        private readonly AutoSignalsDbContext _context;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly IConfiguration _configuration;

        public SubscriptionService(
            AutoSignalsDbContext context,
            UserManager<IdentityUser> userManager,
            IConfiguration configuration)
        {
            _context = context;
            _userManager = userManager;
            _configuration = configuration;
        }

        public async Task<SubscriptionTier> GetTierAsync(string userId)
        {
            var userData = await _context.UsersData.AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == userId);
            return userData?.SubscriptionTier ?? SubscriptionTier.Freemium;
        }

        public async Task<bool> IsTrialActiveAsync(string userId)
        {
            var userData = await _context.UsersData.AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == userId);
            return userData?.SubscriptionStatus == SubscriptionStatus.Trial
                && userData.TrialEndDate.HasValue
                && userData.TrialEndDate.Value > DateTime.UtcNow;
        }

        public async Task<bool> CanAccessFeatureAsync(string userId, SubscriptionFeature feature)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return false;

            var roles = await _userManager.GetRolesAsync(user);

            // Tester = VIP-equivalent (AD-07 / Decisions Log)
            bool isVip = roles.Contains("VIP") || roles.Contains("Tester") || roles.Contains("Admin");
            // Subscriber = Pro-equivalent (AD-07 / Decisions Log)
            bool isPro = isVip || roles.Contains("Pro") || roles.Contains("Subscriber");

            return feature switch
            {
                SubscriptionFeature.RealTimeSignals => isPro,
                SubscriptionFeature.AllSignalProviders => isPro,
                SubscriptionFeature.FullPerformanceHistory => isVip,
                SubscriptionFeature.SignalPredictions => isPro,
                SubscriptionFeature.Analytics => isPro,
                SubscriptionFeature.MultiplePortfolios => isPro,
                SubscriptionFeature.UnlimitedPortfolios => isVip,
                SubscriptionFeature.ExchangeApiConnection => isPro,
                SubscriptionFeature.MultipleExchangeConnections => isVip,
                SubscriptionFeature.AutoTrading => isPro,
                SubscriptionFeature.TelegramNotifications => isPro,
                SubscriptionFeature.FullEmailNotifications => isPro,
                SubscriptionFeature.VipDashboard => isPro,
                _ => false
            };
        }

        public async Task StartTrialAsync(string userId)
        {
            var trialDays = _configuration.GetValue<int>("Subscription:TrialDays", 30);

            var userData = await _context.UsersData.FirstOrDefaultAsync(u => u.Id == userId);
            if (userData == null) return;

            userData.SubscriptionTier = SubscriptionTier.Pro;
            userData.SubscriptionStatus = SubscriptionStatus.Trial;
            userData.TrialEndDate = DateTime.UtcNow.AddDays(trialDays);
            userData.SubscriptionProvider = "System";

            _context.SubscriptionEvents.Add(new SubscriptionEvent
            {
                UserId = userId,
                Provider = "System",
                EventType = SubscriptionEventTypes.TrialStarted,
                Tier = SubscriptionTier.Pro,
                OccurredAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();
        }

        public async Task ActivateSubscriptionAsync(string userId, SubscriptionTier tier,
            string provider, string externalSubscriptionId, DateTime start, DateTime end)
        {
            await StageSubscriptionActivationAsync(userId, tier, provider, externalSubscriptionId, start, end);
            await _context.SaveChangesAsync();
        }

        /// <inheritdoc/>
        public async Task StageSubscriptionActivationAsync(string userId, SubscriptionTier tier,
            string provider, string externalSubscriptionId, DateTime start, DateTime end)
        {
            var userData = await _context.UsersData.FirstOrDefaultAsync(u => u.Id == userId);
            if (userData == null) return;

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return;

            var oldTier = userData.SubscriptionTier;

            userData.SubscriptionTier = tier;
            userData.SubscriptionStatus = SubscriptionStatus.Active;
            userData.TrialEndDate = null;
            userData.SubscriptionStartDate = start;
            userData.SubscriptionEndDate = end;
            userData.SubscriptionProvider = provider;
            userData.ExternalSubscriptionId = externalSubscriptionId;

            // UserManager uses a separate Identity DbContext — cannot be in the same EF transaction.
            // Role assignments are idempotent so a retry is safe.
            try
            {
                await RemoveSubscriptionRolesAsync(user);
                await _userManager.AddToRoleAsync(user, tier == SubscriptionTier.VIP ? "VIP" : "Pro");
            }
            catch (Exception)
            {
                // Role sync failure is logged by the caller; subscription data is still staged.
                throw;
            }

            _context.SubscriptionEvents.Add(new SubscriptionEvent
            {
                UserId = userId,
                Provider = provider,
                EventType = oldTier == tier
                    ? SubscriptionEventTypes.SubscriptionCreated
                    : SubscriptionEventTypes.SubscriptionUpgraded,
                Tier = tier,
                ExternalSubscriptionId = externalSubscriptionId,
                OccurredAt = DateTime.UtcNow
            });

            await EnforceConnectionLimitAsync(userId, tier);
            // No SaveChangesAsync — caller controls the transaction.
        }

        public async Task CancelSubscriptionAsync(string userId, string reason)
        {
            var userData = await _context.UsersData.FirstOrDefaultAsync(u => u.Id == userId);
            if (userData == null) return;

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return;

            userData.SubscriptionTier = SubscriptionTier.Freemium;
            userData.SubscriptionStatus = SubscriptionStatus.Cancelled;
            userData.SubscriptionEndDate = DateTime.UtcNow;

            await RemoveSubscriptionRolesAsync(user);
            await _userManager.AddToRoleAsync(user, "Freemium");

            _context.SubscriptionEvents.Add(new SubscriptionEvent
            {
                UserId = userId,
                Provider = userData.SubscriptionProvider ?? "System",
                EventType = SubscriptionEventTypes.SubscriptionCancelled,
                Tier = SubscriptionTier.Freemium,
                OccurredAt = DateTime.UtcNow,
                RawPayload = reason
            });

            await EnforceConnectionLimitAsync(userId, SubscriptionTier.Freemium);
            await _context.SaveChangesAsync();
        }

        public async Task ExpireTrialAsync(string userId)
        {
            var userData = await _context.UsersData.FirstOrDefaultAsync(u => u.Id == userId);
            if (userData == null) return;

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return;

            userData.SubscriptionTier = SubscriptionTier.Freemium;
            userData.SubscriptionStatus = SubscriptionStatus.Expired;
            userData.TrialEndDate = null;

            await RemoveSubscriptionRolesAsync(user);
            await _userManager.AddToRoleAsync(user, "Freemium");

            _context.SubscriptionEvents.Add(new SubscriptionEvent
            {
                UserId = userId,
                Provider = "System",
                EventType = SubscriptionEventTypes.TrialExpired,
                Tier = SubscriptionTier.Freemium,
                OccurredAt = DateTime.UtcNow
            });

            await EnforceConnectionLimitAsync(userId, SubscriptionTier.Freemium);
            await _context.SaveChangesAsync();
        }

        public async Task ExpireSubscriptionAsync(string userId)
        {
            var userData = await _context.UsersData.FirstOrDefaultAsync(u => u.Id == userId);
            if (userData == null) return;

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return;

            userData.SubscriptionTier = SubscriptionTier.Freemium;
            userData.SubscriptionStatus = SubscriptionStatus.Expired;
            userData.SubscriptionEndDate = null;

            await RemoveSubscriptionRolesAsync(user);
            await _userManager.AddToRoleAsync(user, "Freemium");

            _context.SubscriptionEvents.Add(new SubscriptionEvent
            {
                UserId = userId,
                Provider = userData.SubscriptionProvider ?? "System",
                EventType = SubscriptionEventTypes.SubscriptionExpired,
                Tier = SubscriptionTier.Freemium,
                OccurredAt = DateTime.UtcNow
            });

            await EnforceConnectionLimitAsync(userId, SubscriptionTier.Freemium);
            await _context.SaveChangesAsync();
        }

        public async Task<UserData?> GetSubscriptionDataAsync(string userId)
        {
            return await _context.UsersData.AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == userId);
        }

        private async Task RemoveSubscriptionRolesAsync(IdentityUser user)
        {
            string[] subscriptionRoles = ["Freemium", "Pro", "VIP"];
            var currentRoles = await _userManager.GetRolesAsync(user);
            var toRemove = currentRoles.Intersect(subscriptionRoles).ToList();
            if (toRemove.Count > 0)
                await _userManager.RemoveFromRolesAsync(user, toRemove);
        }

        private async Task EnforceConnectionLimitAsync(string userId, SubscriptionTier newTier)
        {
            int limit = newTier switch
            {
                SubscriptionTier.VIP => 5,
                SubscriptionTier.Pro => 1,
                _ => 0
            };

            var connections = await _context.UserExchangeConnections
                .Where(c => c.UserId == userId)
                .OrderByDescending(c => c.IsDefault)
                .ThenBy(c => c.CreatedAt)
                .ToListAsync();

            for (int i = 0; i < connections.Count; i++)
            {
                connections[i].IsActive = i < limit;
                connections[i].UpdatedAt = DateTime.UtcNow;
            }
        }
    }
}
