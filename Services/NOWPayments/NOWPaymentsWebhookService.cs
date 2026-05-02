using AutoSignals.Data;
using AutoSignals.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AutoSignals.Services.NOWPayments
{
    /// <summary>
    /// Processes inbound NOWPayments IPN webhook events.
    /// All processing is idempotent — <see cref="SubscriptionEvent.ExternalEventId"/> prevents double-processing.
    /// </summary>
    public class NOWPaymentsWebhookService
    {
        private readonly AutoSignalsDbContext _context;
        private readonly ISubscriptionService _subscriptionService;
        private readonly NOWPaymentsOptions _options;
        private readonly ILogger<NOWPaymentsWebhookService> _logger;
        private readonly IEmailSender _emailSender;
        private readonly UserManager<IdentityUser> _userManager;

        public NOWPaymentsWebhookService(
            AutoSignalsDbContext context,
            ISubscriptionService subscriptionService,
            IOptions<NOWPaymentsOptions> options,
            ILogger<NOWPaymentsWebhookService> logger,
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

        /// <summary>True when <see cref="NOWPaymentsOptions.IpnSecret"/> is configured.</summary>
        public bool IsIpnSecretConfigured => !string.IsNullOrWhiteSpace(_options.IpnSecret);

        /// <summary>
        /// Verifies the HMAC-SHA512 signature from the <c>x-nowpayments-sig</c> request header.
        /// NOWPayments sorts all JSON keys alphabetically before hashing.
        /// </summary>
        public bool IsValidSignature(string rawBody, string signature)
        {
            if (string.IsNullOrWhiteSpace(_options.IpnSecret))
                return false;

            var normalizedSignature = signature.Trim().ToLowerInvariant();

            // NOWPayments signature examples/documentation can differ on canonicalization.
            // Validate against both deterministic key-sorted JSON and raw body text.
            var canonicalJson = BuildCanonicalSortedJson(rawBody);
            var canonicalExpected = ComputeHmacSha512Hex(canonicalJson);
            if (FixedTimeHexEquals(canonicalExpected, normalizedSignature))
                return true;

            var rawExpected = ComputeHmacSha512Hex(rawBody);
            return FixedTimeHexEquals(rawExpected, normalizedSignature);
        }

        private string ComputeHmacSha512Hex(string data)
        {
            var key = Encoding.UTF8.GetBytes(_options.IpnSecret);
            var payload = Encoding.UTF8.GetBytes(data);
            using var hmac = new HMACSHA512(key);
            var hash = hmac.ComputeHash(payload);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        private static bool FixedTimeHexEquals(string a, string b)
        {
            // FixedTimeEquals throws ArgumentException when lengths differ; guard it explicitly.
            if (a.Length != b.Length) return false;
            return CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(a),
                Encoding.ASCII.GetBytes(b));
        }

        private static string BuildCanonicalSortedJson(string rawBody)
        {
            using var doc = JsonDocument.Parse(rawBody);
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
            {
                WriteCanonicalElement(writer, doc.RootElement);
            }

            // Keep JSON compact and stable; allow unescaped slashes to align with common NOWPayments examples.
            var canonical = Encoding.UTF8.GetString(stream.ToArray());
            var normalizedNode = JsonNode.Parse(canonical);
            return normalizedNode?.ToJsonString(new JsonSerializerOptions
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                WriteIndented = false
            }) ?? "{}";
        }

        private static void WriteCanonicalElement(Utf8JsonWriter writer, JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    writer.WriteStartObject();
                    foreach (var prop in element.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                    {
                        writer.WritePropertyName(prop.Name);
                        WriteCanonicalElement(writer, prop.Value);
                    }
                    writer.WriteEndObject();
                    break;

                case JsonValueKind.Array:
                    writer.WriteStartArray();
                    foreach (var item in element.EnumerateArray())
                        WriteCanonicalElement(writer, item);
                    writer.WriteEndArray();
                    break;

                default:
                    element.WriteTo(writer);
                    break;
            }
        }

        // ── Event Dispatch ─────────────────────────────────────────────────────

        public async Task HandleEventAsync(string rawBody)
        {
            _logger.LogInformation(
                "NOWPayments webhook step 1/7: begin handling raw payload. BodyLength={Length}",
                rawBody?.Length ?? 0);

            using var doc = JsonDocument.Parse(rawBody);
            var root = doc.RootElement;

            var paymentId = root.TryGetProperty("payment_id", out var pid)
                ? pid.ValueKind == JsonValueKind.Number ? pid.GetInt64().ToString() : pid.GetString() ?? Guid.NewGuid().ToString()
                : Guid.NewGuid().ToString();

            var paymentStatus = root.TryGetProperty("payment_status", out var ps)
                ? ps.GetString() ?? string.Empty
                : string.Empty;

            var orderId = root.TryGetProperty("order_id", out var oid)
                ? oid.GetString() ?? string.Empty
                : string.Empty;

            _logger.LogInformation("NOWPayments IPN received. PaymentId={Id} Status={Status} OrderId={OrderId}",
                paymentId, paymentStatus, orderId);

            _logger.LogInformation(
                "NOWPayments webhook step 2/7: payload parsed. PaymentId={PaymentId} PaymentStatus={Status} OrderId={OrderId}",
                paymentId, paymentStatus, orderId);

            // Idempotency check — only activation events (SubscriptionCreated / SubscriptionUpgraded)
            // carry the payment_id as ExternalEventId. Intermediate-status IPN rows must NOT block
            // the finished IPN from activating the subscription.
            var alreadyProcessed = await _context.SubscriptionEvents
                .AnyAsync(e => e.ExternalEventId == paymentId
                           && (e.EventType == SubscriptionEventTypes.SubscriptionCreated
                               || e.EventType == SubscriptionEventTypes.SubscriptionUpgraded));

            if (alreadyProcessed)
            {
                _logger.LogInformation("NOWPayments IPN {Id} already processed — skipping.", paymentId);
                return;
            }

            _logger.LogInformation(
                "NOWPayments webhook step 3/7: idempotency check passed. PaymentId={PaymentId}",
                paymentId);

            switch (paymentStatus)
            {
                case "finished":
                    _logger.LogInformation(
                        "NOWPayments webhook step 4/7: dispatching finished handler. PaymentId={PaymentId} OrderId={OrderId}",
                        paymentId, orderId);
                    await HandlePaymentFinishedAsync(root, paymentId, orderId, rawBody);
                    _logger.LogInformation(
                        "NOWPayments webhook step 7/7: finished handler completed. PaymentId={PaymentId} OrderId={OrderId}",
                        paymentId, orderId);
                    break;

                case "partially_paid":
                    _logger.LogWarning("NOWPayments partial payment for order {OrderId}.", orderId);
                    // Pass null for externalEventId so the payment_id is not written as the idempotency key;
                    // a later 'finished' IPN must still be able to activate the subscription.
                    var (partialUserId, _) = ParseOrderId(orderId);
                    await WriteAuditEventAsync(partialUserId ?? "unknown", null, "PaymentPartial", null, orderId, null, rawBody);
                    _logger.LogInformation(
                        "NOWPayments webhook step 7/7: partial-payment audit persisted. PaymentId={PaymentId} OrderId={OrderId}",
                        paymentId, orderId);
                    break;

                case "failed":
                case "expired":
                    await HandlePaymentFailedAsync(paymentId, orderId, paymentStatus, rawBody);
                    _logger.LogInformation(
                        "NOWPayments webhook step 7/7: failed/expired handler completed. PaymentId={PaymentId} OrderId={OrderId}",
                        paymentId, orderId);
                    break;

                default:
                    // Write an audit row so the success-page fallback can discover the payment_id
                    // from our DB without needing to call the JWT-protected list endpoint.
                    // ExternalEventId is intentionally null here to avoid blocking the finished IPN.
                    _logger.LogInformation(
                        "NOWPayments IPN status '{Status}' for payment {PaymentId}, order {OrderId} — audit only.",
                        paymentStatus, paymentId, orderId);
                    var (intermediateUserId, _) = ParseOrderId(orderId);
                    await WriteAuditEventAsync(
                        intermediateUserId ?? "unknown", null,
                        $"Payment{char.ToUpperInvariant(paymentStatus[0])}{paymentStatus[1..]}",
                        null, orderId, null, rawBody);
                    _logger.LogInformation(
                        "NOWPayments webhook step 7/7: intermediate-status audit persisted. PaymentId={PaymentId} OrderId={OrderId} Status={Status}",
                        paymentId, orderId, paymentStatus);
                    break;
            }
        }

        // ── Handlers ──────────────────────────────────────────────────────────

        private async Task HandlePaymentFinishedAsync(
            JsonElement root, string paymentId, string orderId, string rawBody)
        {
            _logger.LogInformation(
                "NOWPayments finished step 5/7: parsing order and plan. PaymentId={PaymentId} OrderId={OrderId}",
                paymentId, orderId);

            var (userId, planId) = ParseOrderId(orderId);
            if (userId == null || planId == null)
            {
                _logger.LogWarning("NOWPayments finished payment has unparseable order_id '{OrderId}'.", orderId);
                await WriteAuditEventAsync("unknown", paymentId, SubscriptionEventTypes.PaymentFailed,
                    null, orderId, null, rawBody);
                return;
            }

            var plan = await _context.SubscriptionPlans
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == planId.Value);

            if (plan == null)
            {
                _logger.LogWarning("NOWPayments payment references unknown plan {PlanId}.", planId);
                await WriteAuditEventAsync(userId, paymentId, SubscriptionEventTypes.PaymentFailed,
                    null, orderId, null, rawBody);
                return;
            }

            var now = DateTime.UtcNow;
            var end = plan.IsAnnual ? now.AddYears(1) : now.AddMonths(1);

            _logger.LogInformation(
                "NOWPayments finished step 6/7: activating subscription transaction. UserId={UserId} PlanId={PlanId} Tier={Tier} PaymentId={PaymentId}",
                userId, planId, plan.Tier, paymentId);

            decimal? amount = root.TryGetProperty("price_amount", out var amt)
                && amt.ValueKind == JsonValueKind.Number ? amt.GetDecimal() : null;

            // Wrap activation + idempotency marker in a single atomic transaction.
            // This eliminates the race window where a concurrent IPN could re-activate.
            // Note: UserManager role operations use a separate Identity DbContext and cannot
            // be included in this transaction — they are idempotent so a retry is safe.
            await using var tx = await _context.Database.BeginTransactionAsync();
            try
            {
                // Stage user-data changes + inner SubscriptionEvent (no SaveChanges yet).
                await _subscriptionService.StageSubscriptionActivationAsync(
                    userId, plan.Tier, "NOWPayments", paymentId, now, end);

                // Stage the idempotency marker event with ExternalEventId so concurrent IPNs
                // are blocked as soon as this transaction commits.
                _context.SubscriptionEvents.Add(new SubscriptionEvent
                {
                    UserId = userId,
                    Provider = "NOWPayments",
                    EventType = SubscriptionEventTypes.SubscriptionCreated,
                    Tier = plan.Tier,
                    Amount = amount,
                    ExternalEventId = paymentId,
                    ExternalSubscriptionId = orderId,
                    OccurredAt = DateTime.UtcNow,
                    RawPayload = rawBody
                });

                // Single atomic commit — both the activation and the idempotency marker.
                await _context.SaveChangesAsync();
                await tx.CommitAsync();

                _logger.LogInformation(
                    "NOWPayments finished activation committed. UserId={UserId} PaymentId={PaymentId} OrderId={OrderId}",
                    userId, paymentId, orderId);
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                _logger.LogError(ex,
                    "NOWPayments: transaction rolled back for payment {PaymentId}, order {OrderId}.",
                    paymentId, orderId);
                throw;
            }

            // Email is outside the transaction — non-critical.
            try
            {
                await SendSubscriptionConfirmedEmailAsync(userId, plan.Tier);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "NOWPayments: confirmation email failed for user {UserId} after activating payment {PaymentId}. Subscription is active.",
                    userId, paymentId);
            }
        }

        private async Task HandlePaymentFailedAsync(
            string paymentId, string orderId, string status, string rawBody)
        {
            var (userId, _) = ParseOrderId(orderId);
            _logger.LogWarning("NOWPayments payment {Status} for order {OrderId}.", status, orderId);
            await WriteAuditEventAsync(userId ?? "unknown", paymentId,
                SubscriptionEventTypes.PaymentFailed, null, orderId, null, rawBody);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static (string? userId, int? planId) ParseOrderId(string orderId)
        {
            if (string.IsNullOrWhiteSpace(orderId)) return (null, null);

            // Supported formats:
            // 1) {userId}:{planId}
            // 2) {userId}:{planId}:{nonce}
            var firstColon = orderId.IndexOf(':');
            if (firstColon <= 0) return (null, null);

            var userId = orderId[..firstColon];
            var remainder = orderId[(firstColon + 1)..];
            if (string.IsNullOrWhiteSpace(remainder)) return (null, null);

            var numericPlanPart = new string(remainder.TakeWhile(char.IsDigit).ToArray());
            if (!int.TryParse(numericPlanPart, out var planId)) return (null, null);

            return (userId, planId);
        }

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

        private async Task WriteAuditEventAsync(
            string userId, string? externalEventId, string? eventType,
            SubscriptionTier? tier, string? externalSubscriptionId,
            decimal? amount, string? rawPayload)
        {
            _context.SubscriptionEvents.Add(new SubscriptionEvent
            {
                UserId = userId,
                Provider = "NOWPayments",
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
