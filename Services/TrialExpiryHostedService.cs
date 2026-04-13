using AutoSignals.Data;
using AutoSignals.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;

namespace AutoSignals.Services
{
    public class TrialExpiryHostedService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<TrialExpiryHostedService> _logger;

        public TrialExpiryHostedService(
            IServiceScopeFactory scopeFactory,
            ILogger<TrialExpiryHostedService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Wait until 02:00 UTC on the first run
            await WaitUntilNextRunAsync(stoppingToken);

            using var timer = new PeriodicTimer(TimeSpan.FromHours(24));

            do
            {
                try
                {
                    await ProcessTrialsAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "TrialExpiryHostedService encountered an error.");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }

        private async Task ProcessTrialsAsync(CancellationToken stoppingToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AutoSignalsDbContext>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var subscriptionService = scope.ServiceProvider.GetRequiredService<ISubscriptionService>();
            var emailSender = scope.ServiceProvider.GetRequiredService<IEmailSender>();
            var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();

            var today = DateTime.UtcNow.Date;
            var warningDays = configuration.GetValue<int>("Subscription:TrialWarningDays", 5);
            var warningDate = today.AddDays(warningDays);

            // Expire overdue trials
            var expired = await context.UsersData
                .Where(u => u.SubscriptionStatus == SubscriptionStatus.Trial
                         && u.TrialEndDate.HasValue
                         && u.TrialEndDate.Value.Date <= today)
                .ToListAsync(stoppingToken);

            foreach (var userData in expired)
            {
                _logger.LogInformation("Expiring trial for user {UserId}", userData.Id);
                await subscriptionService.ExpireTrialAsync(userData.Id);

                var user = await userManager.FindByIdAsync(userData.Id);
                if (user != null)
                {
                    var email = await userManager.GetEmailAsync(user);
                    if (!string.IsNullOrEmpty(email))
                    {
                        await emailSender.SendEmailAsync(email,
                            "Your AutoSignals Pro trial has ended",
                            BuildExpiredEmail());
                    }
                }
            }

            if (expired.Count > 0)
                _logger.LogInformation("Expired {Count} trial(s).", expired.Count);

            // Send 5-day warning
            var warnings = await context.UsersData
                .Where(u => u.SubscriptionStatus == SubscriptionStatus.Trial
                         && u.TrialEndDate.HasValue
                         && u.TrialEndDate.Value.Date == warningDate)
                .ToListAsync(stoppingToken);

            foreach (var userData in warnings)
            {
                _logger.LogInformation("Sending trial warning to user {UserId}", userData.Id);

                context.SubscriptionEvents.Add(new SubscriptionEvent
                {
                    UserId = userData.Id,
                    Provider = "System",
                    EventType = SubscriptionEventTypes.TrialWarning,
                    Tier = SubscriptionTier.Pro,
                    OccurredAt = DateTime.UtcNow
                });

                var user = await userManager.FindByIdAsync(userData.Id);
                if (user != null)
                {
                    var email = await userManager.GetEmailAsync(user);
                    if (!string.IsNullOrEmpty(email))
                    {
                        await emailSender.SendEmailAsync(email,
                            $"Your AutoSignals Pro trial ends in {warningDays} days",
                            BuildWarningEmail(warningDays));
                    }
                }
            }

            if (warnings.Count > 0)
            {
                await context.SaveChangesAsync(stoppingToken);
                _logger.LogInformation("Sent {Count} trial warning email(s).", warnings.Count);
            }
        }

        private static async Task WaitUntilNextRunAsync(CancellationToken stoppingToken)
        {
            var now = DateTime.UtcNow;
            var nextRun = now.Date.AddHours(2); // 02:00 UTC
            if (now >= nextRun) nextRun = nextRun.AddDays(1);

            var delay = nextRun - now;
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, stoppingToken);
        }

        private static string BuildExpiredEmail() =>
            @"<html><body style='font-family:Arial,sans-serif;background:#f4f4f4;padding:20px;'>
            <table style='max-width:600px;margin:0 auto;background:white;padding:20px;border-radius:8px;'>
            <tr><td style='text-align:center;'>
                <h2 style='color:#6366f1;'>Your Pro Trial Has Ended</h2>
                <p>Your 30-day AutoSignals Pro trial has expired. Your account has been moved to the Free plan.</p>
                <p>Upgrade to Pro or VIP to keep real-time signals, auto-trading, and full analytics.</p>
                <a href='https://autosignals.xyz/Pricing'
                   style='background:#6366f1;color:white;padding:12px 24px;border-radius:6px;text-decoration:none;font-weight:bold;'>
                   See Plans &amp; Pricing
                </a>
                <p style='color:#888;margin-top:20px;font-size:12px;'>This email is not monitored. Please do not reply.</p>
            </td></tr></table></body></html>";

        private static string BuildWarningEmail(int daysRemaining) =>
            $@"<html><body style='font-family:Arial,sans-serif;background:#f4f4f4;padding:20px;'>
            <table style='max-width:600px;margin:0 auto;background:white;padding:20px;border-radius:8px;'>
            <tr><td style='text-align:center;'>
                <h2 style='color:#6366f1;'>Your Pro Trial Ends in {daysRemaining} Days</h2>
                <p>You have <strong>{daysRemaining} days</strong> left on your AutoSignals Pro trial.</p>
                <p>Subscribe now to keep real-time signals, auto-trading, and full analytics — from $29/month.</p>
                <a href='https://autosignals.xyz/Pricing'
                   style='background:#6366f1;color:white;padding:12px 24px;border-radius:6px;text-decoration:none;font-weight:bold;'>
                   Upgrade Now — From $29/mo
                </a>
                <p style='color:#888;margin-top:20px;font-size:12px;'>This email is not monitored. Please do not reply.</p>
            </td></tr></table></body></html>";
    }
}
