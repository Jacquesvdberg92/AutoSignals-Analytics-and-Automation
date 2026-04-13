using AutoSignals.Data;
using AutoSignals.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AutoSignals.Services.LemonSqueezy
{
    /// <summary>
    /// Processes inbound LemonSqueezy webhook events.
    /// All processing is idempotent — <see cref="SubscriptionEvent.ExternalEventId"/> prevents double-processing.
    /// </summary>
    public class LemonSqueezyWebhookService
    {
        private readonly AutoSignalsDbContext _context;
        private readonly ISubscriptionService _subscriptionService;
        private readonly LemonSqueezyOptions _options;
        private readonly ILogger<LemonSqueezyWebhookService> _logger;
        private readonly IEmailSender _emailSender;
        private readonly UserManager<IdentityUser> _userManager;

        private static readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

        public LemonSqueezyWebhookService(
            AutoSignalsDbContext context,
            ISubscriptionService subscriptionService,
            IOptions<LemonSqueezyOptions> options,
            ILogger<LemonSqueezyWebhookService> logger,
            IEmailSender emailSender,
            UserManager<IdentityUser> userManager)
        {
            _context = context;
            _subscriptionService = subscriptionService;
            _options = options.Value;
            _logger = logger;
            _emailSender = emailSender;
            _userManager = userManager;
        }

        // ── Signature ──────────────────────────────────────────────────────────

        /// <summary>
        /// Verifies the HMAC-SHA256 signature from the <c>X-Signature</c> request header.
        /// </summary>
        public bool IsValidSignature(string rawBody, string signature)
        {
            if (string.IsNullOrWhiteSpace(_options.WebhookSecret))
                return false;

            var key = Encoding.UTF8.GetBytes(_options.WebhookSecret);
            var data = Encoding.UTF8.GetBytes(rawBody);
            var hash = HMACSHA256.HashData(key, data);
            var expected = Convert.ToHexString(hash).ToLowerInvariant();

            return CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(expected),
                Encoding.ASCII.GetBytes(signature.ToLowerInvariant()));
        }

        // ── Event Dispatch ─────────────────────────────────────────────────────

        public async Task HandleEventAsync(string rawBody)
        {
            using var doc = JsonDocument.Parse(rawBody);
            var root = doc.RootElement;

            var eventName = root.GetProperty("meta").GetProperty("event_name").GetString() ?? string.Empty;
            var eventId = TryGetString(root, "meta", "webhook_id") ?? Guid.NewGuid().ToString();

            _logger.LogInformation("LemonSqueezy webhook received. Event={Event} Id={Id}", eventName, eventId);

            // Idempotency check — skip if already processed
            var alreadyProcessed = await _context.SubscriptionEvents
                .AnyAsync(e => e.ExternalEventId == eventId);

            if (alreadyProcessed)
            {
                _logger.LogInformation("LemonSqueezy webhook {Id} already processed — skipping.", eventId);
                return;
            }

            switch (eventName)
            {
                case "subscription_created":
                    await HandleSubscriptionCreatedAsync(root, eventId, rawBody);
                    break;

                case "subscription_updated":
                    await HandleSubscriptionUpdatedAsync(root, eventId, rawBody);
                    break;

                case "subscription_cancelled":
                    await HandleSubscriptionCancelledAsync(root, eventId, rawBody);
                    break;

                case "subscription_expired":
                    await HandleSubscriptionExpiredAsync(root, eventId, rawBody);
                    break;

                case "subscription_payment_success":
                    await HandlePaymentSuccessAsync(root, eventId, rawBody);
                    break;

                case "subscription_payment_failed":
                    await HandlePaymentFailedAsync(root, eventId, rawBody);
                    break;

                case "subscription_payment_recovered":
                    await HandlePaymentRecoveredAsync(root, eventId, rawBody);
                    break;

                default:
                    _logger.LogDebug("LemonSqueezy webhook event {Event} not handled.", eventName);
                    // Still write a row so the unique index is satisfied and it is not reprocessed
                    await WriteAuditEventAsync("unknown", eventId, null, null, null, null, rawBody);
                    break;
            }
        }

        // ── Handlers ──────────────────────────────────────────────────────────

        private async Task HandleSubscriptionCreatedAsync(JsonElement root, string eventId, string rawBody)
        {
            var userId = GetCustomUserId(root);
            if (userId == null)
            {
                _logger.LogWarning("subscription_created webhook missing user_id in custom data.");
                return;
            }

            var data = root.GetProperty("data");
            var attrs = data.GetProperty("attributes");

            var subscriptionId = data.GetProperty("id").GetString() ?? string.Empty;
            var customerId = TryGetString(attrs, "customer_id")
                            ?? TryGetString(attrs, "customer", "data", "id")
                            ?? string.Empty;
            var variantId = TryGetString(attrs, "variant_id") ?? string.Empty;
            var renewsAt = TryGetDateTimeOffset(attrs, "renews_at");
            var status = TryGetString(attrs, "status") ?? string.Empty;

            var tier = await ResolveTierFromVariantAsync(variantId);

            // Store LemonSqueezy customer ID on UserData
            var userData = await _context.UsersData.FirstOrDefaultAsync(u => u.Id == userId);
            if (userData != null && !string.IsNullOrWhiteSpace(customerId))
                userData.LemonSqueezyCustomerId = customerId;

            var end = renewsAt?.UtcDateTime ?? DateTime.UtcNow.AddMonths(1);
            await _subscriptionService.ActivateSubscriptionAsync(
                userId, tier, "LemonSqueezy", subscriptionId, DateTime.UtcNow, end);

            await SendSubscriptionConfirmedEmailAsync(userId, tier);

            await WriteAuditEventAsync(userId, eventId, SubscriptionEventTypes.SubscriptionCreated,
                tier, subscriptionId, null, rawBody);
        }

        private async Task HandleSubscriptionUpdatedAsync(JsonElement root, string eventId, string rawBody)
        {
            var data = root.GetProperty("data");
            var attrs = data.GetProperty("attributes");
            var subscriptionId = data.GetProperty("id").GetString() ?? string.Empty;
            var status = TryGetString(attrs, "status") ?? string.Empty;
            var variantId = TryGetString(attrs, "variant_id") ?? string.Empty;
            var renewsAt = TryGetDateTimeOffset(attrs, "renews_at");

            var userData = await _context.UsersData
                .FirstOrDefaultAsync(u => u.ExternalSubscriptionId == subscriptionId);

            if (userData == null)
            {
                _logger.LogWarning("subscription_updated: no user found for subscription {Id}.", subscriptionId);
                await WriteAuditEventAsync("unknown", eventId, null, null, subscriptionId, null, rawBody);
                return;
            }

            if (status is "cancelled" or "expired")
            {
                await _subscriptionService.CancelSubscriptionAsync(userData.Id, $"LemonSqueezy status={status}");
                await WriteAuditEventAsync(userData.Id, eventId, SubscriptionEventTypes.SubscriptionCancelled,
                    SubscriptionTier.Freemium, subscriptionId, null, rawBody);
            }
            else if (status == "active")
            {
                var tier = await ResolveTierFromVariantAsync(variantId);
                var end = renewsAt?.UtcDateTime ?? DateTime.UtcNow.AddMonths(1);
                await _subscriptionService.ActivateSubscriptionAsync(
                    userData.Id, tier, "LemonSqueezy", subscriptionId, userData.SubscriptionStartDate ?? DateTime.UtcNow, end);
                await WriteAuditEventAsync(userData.Id, eventId, SubscriptionEventTypes.SubscriptionUpgraded,
                    tier, subscriptionId, null, rawBody);
            }
            else
            {
                await WriteAuditEventAsync(userData.Id, eventId, null, null, subscriptionId, null, rawBody);
            }
        }

        private async Task HandleSubscriptionCancelledAsync(JsonElement root, string eventId, string rawBody)
        {
            var data = root.GetProperty("data");
            var subscriptionId = data.GetProperty("id").GetString() ?? string.Empty;

            var userData = await _context.UsersData
                .FirstOrDefaultAsync(u => u.ExternalSubscriptionId == subscriptionId);

            if (userData == null)
            {
                await WriteAuditEventAsync("unknown", eventId, SubscriptionEventTypes.SubscriptionCancelled,
                    null, subscriptionId, null, rawBody);
                return;
            }

            // LemonSqueezy cancellation = still active until period ends; we update status here.
            // The subscription_expired event will do the final downgrade.
            userData.SubscriptionStatus = SubscriptionStatus.Cancelled;
            await _context.SaveChangesAsync();

            await WriteAuditEventAsync(userData.Id, eventId, SubscriptionEventTypes.SubscriptionCancelled,
                userData.SubscriptionTier, subscriptionId, null, rawBody);
        }

        private async Task HandleSubscriptionExpiredAsync(JsonElement root, string eventId, string rawBody)
        {
            var data = root.GetProperty("data");
            var subscriptionId = data.GetProperty("id").GetString() ?? string.Empty;

            var userData = await _context.UsersData
                .FirstOrDefaultAsync(u => u.ExternalSubscriptionId == subscriptionId);

            if (userData == null)
            {
                await WriteAuditEventAsync("unknown", eventId, SubscriptionEventTypes.SubscriptionExpired,
                    null, subscriptionId, null, rawBody);
                return;
            }

            await _subscriptionService.CancelSubscriptionAsync(userData.Id, "LemonSqueezy subscription_expired");

            await WriteAuditEventAsync(userData.Id, eventId, SubscriptionEventTypes.SubscriptionExpired,
                SubscriptionTier.Freemium, subscriptionId, null, rawBody);
        }

        private async Task HandlePaymentSuccessAsync(JsonElement root, string eventId, string rawBody)
        {
            var data = root.GetProperty("data");
            var attrs = data.GetProperty("attributes");
            var subscriptionId = data.GetProperty("id").GetString() ?? string.Empty;
            var renewsAt = TryGetDateTimeOffset(attrs, "renews_at");

            var userData = await _context.UsersData
                .FirstOrDefaultAsync(u => u.ExternalSubscriptionId == subscriptionId);

            if (userData != null)
            {
                userData.SubscriptionStatus = SubscriptionStatus.Active;
                if (renewsAt.HasValue)
                    userData.SubscriptionEndDate = renewsAt.Value.UtcDateTime;
                await _context.SaveChangesAsync();
            }

            await WriteAuditEventAsync(userData?.Id ?? "unknown", eventId,
                SubscriptionEventTypes.SubscriptionRenewed, userData?.SubscriptionTier,
                subscriptionId, null, rawBody);
        }

        private async Task HandlePaymentFailedAsync(JsonElement root, string eventId, string rawBody)
        {
            var data = root.GetProperty("data");
            var subscriptionId = data.GetProperty("id").GetString() ?? string.Empty;

            var userData = await _context.UsersData
                .FirstOrDefaultAsync(u => u.ExternalSubscriptionId == subscriptionId);

            if (userData != null)
            {
                userData.SubscriptionStatus = SubscriptionStatus.PastDue;
                await _context.SaveChangesAsync();
                await SendPaymentFailedEmailAsync(userData.Id);
            }

            await WriteAuditEventAsync(userData?.Id ?? "unknown", eventId,
                SubscriptionEventTypes.PaymentFailed, userData?.SubscriptionTier,
                subscriptionId, null, rawBody);
        }

        private async Task HandlePaymentRecoveredAsync(JsonElement root, string eventId, string rawBody)
        {
            var data = root.GetProperty("data");
            var attrs = data.GetProperty("attributes");
            var subscriptionId = data.GetProperty("id").GetString() ?? string.Empty;
            var renewsAt = TryGetDateTimeOffset(attrs, "renews_at");

            var userData = await _context.UsersData
                .FirstOrDefaultAsync(u => u.ExternalSubscriptionId == subscriptionId);

            if (userData != null)
            {
                userData.SubscriptionStatus = SubscriptionStatus.Active;
                if (renewsAt.HasValue)
                    userData.SubscriptionEndDate = renewsAt.Value.UtcDateTime;
                await _context.SaveChangesAsync();
            }

            await WriteAuditEventAsync(userData?.Id ?? "unknown", eventId,
                SubscriptionEventTypes.PaymentRecovered, userData?.SubscriptionTier,
                subscriptionId, null, rawBody);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private async Task SendSubscriptionConfirmedEmailAsync(string userId, SubscriptionTier tier)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return;
            var email = await _userManager.GetEmailAsync(user);
            if (string.IsNullOrEmpty(email)) return;

            var tierName = tier == SubscriptionTier.VIP ? "VIP" : "Pro";
            await _emailSender.SendEmailAsync(email,
                $"Welcome to AutoSignals {tierName}!",
                $@"<html><body style='font-family:Arial,sans-serif;background:#f4f4f4;padding:20px;'>
                <table style='max-width:600px;margin:0 auto;background:white;padding:20px;border-radius:8px;'>
                <tr><td style='text-align:center;'>
                    <h2 style='color:#6366f1;'>You're now on AutoSignals {tierName}! 🎉</h2>
                    <p>Your subscription is active. You now have full access to all {tierName} features.</p>
                    <a href='https://autosignals.xyz/Subscription/Manage'
                       style='background:#6366f1;color:white;padding:12px 24px;border-radius:6px;text-decoration:none;font-weight:bold;'>
                       Manage Subscription
                    </a>
                    <p style='color:#888;margin-top:20px;font-size:12px;'>This email is not monitored. Please do not reply.</p>
                </td></tr></table></body></html>");
        }

        private async Task SendPaymentFailedEmailAsync(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return;
            var email = await _userManager.GetEmailAsync(user);
            if (string.IsNullOrEmpty(email)) return;

            await _emailSender.SendEmailAsync(email,
                "AutoSignals — Payment Failed",
                @"<html><body style='font-family:Arial,sans-serif;background:#f4f4f4;padding:20px;'>
                <table style='max-width:600px;margin:0 auto;background:white;padding:20px;border-radius:8px;'>
                <tr><td style='text-align:center;'>
                    <h2 style='color:#ef4444;'>Payment Failed</h2>
                    <p>We were unable to process your AutoSignals subscription payment. Your account is currently past due.</p>
                    <p>Please update your payment method to restore full access.</p>
                    <a href='https://autosignals.xyz/Subscription/Portal'
                       style='background:#ef4444;color:white;padding:12px 24px;border-radius:6px;text-decoration:none;font-weight:bold;'>
                       Update Payment Method
                    </a>
                    <p style='color:#888;margin-top:20px;font-size:12px;'>This email is not monitored. Please do not reply.</p>
                </td></tr></table></body></html>");
        }

        private async Task<SubscriptionTier> ResolveTierFromVariantAsync(string variantId)
        {
            if (string.IsNullOrWhiteSpace(variantId)) return SubscriptionTier.Pro;

            var plan = await _context.SubscriptionPlans
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.LemonSqueezyVariantId == variantId);

            return plan?.Tier ?? SubscriptionTier.Pro;
        }

        private static string? GetCustomUserId(JsonElement root)
        {
            try
            {
                var meta = root.GetProperty("meta");
                if (meta.TryGetProperty("custom_data", out var cd))
                {
                    if (cd.TryGetProperty("user_id", out var uid))
                        return uid.GetString();
                }
            }
            catch { /* ignore */ }
            return null;
        }

        private static string? TryGetString(JsonElement element, params string[] path)
        {
            try
            {
                var current = element;
                foreach (var key in path)
                {
                    if (!current.TryGetProperty(key, out current))
                        return null;
                }
                return current.ValueKind == JsonValueKind.String ? current.GetString() : current.GetRawText();
            }
            catch { return null; }
        }

        private static DateTimeOffset? TryGetDateTimeOffset(JsonElement element, string key)
        {
            try
            {
                if (element.TryGetProperty(key, out var prop) && prop.ValueKind == JsonValueKind.String)
                {
                    if (DateTimeOffset.TryParse(prop.GetString(), out var result))
                        return result;
                }
            }
            catch { /* ignore */ }
            return null;
        }

        private async Task WriteAuditEventAsync(
            string userId, string externalEventId, string? eventType,
            SubscriptionTier? tier, string? externalSubscriptionId,
            decimal? amount, string? rawPayload)
        {
            _context.SubscriptionEvents.Add(new SubscriptionEvent
            {
                UserId = userId,
                Provider = "LemonSqueezy",
                EventType = eventType ?? "unknown",
                Tier = tier,
                Amount = amount,
                ExternalEventId = externalEventId,
                ExternalSubscriptionId = externalSubscriptionId,
                OccurredAt = DateTime.UtcNow,
                RawPayload = rawPayload
            });
            await _context.SaveChangesAsync();
        }
    }
}
