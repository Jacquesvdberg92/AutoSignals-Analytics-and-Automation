using AutoSignals.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace AutoSignals.Services.NOWPayments
{
    public class NOWPaymentsSubscriptionProvider : ISubscriptionProvider
    {
        public string ProviderName => "NOWPayments";

        private readonly HttpClient _http;
        private readonly NOWPaymentsOptions _options;
        private readonly AutoSignalsDbContext _context;
        private readonly ILogger<NOWPaymentsSubscriptionProvider> _logger;

        private static readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

        // invoiceId → orderId — populated at invoice-creation time so the success-page fallback
        // can find the payment_id via the IPN payload even before the first IPN arrives.
        // Survives for the lifetime of the process (app restart clears it — acceptable since
        // the recovery service handles longer-lived gaps).
        private static readonly ConcurrentDictionary<string, string> s_invoiceToOrder = new(StringComparer.Ordinal);

        public NOWPaymentsSubscriptionProvider(
            HttpClient http,
            IOptions<NOWPaymentsOptions> options,
            AutoSignalsDbContext context,
            ILogger<NOWPaymentsSubscriptionProvider> logger)
        {
            _http = http;
            _options = options.Value;
            _context = context;
            _logger = logger;

            _http.BaseAddress = new Uri("https://api.nowpayments.io/v1/");
            _http.DefaultRequestHeaders.Add("x-api-key", _options.ApiKey);
        }

        /// <inheritdoc />
        /// <param name="userId">AutoSignals user ID embedded in the NOWPayments order_id.</param>
        /// <param name="planId">AutoSignals <see cref="Models.SubscriptionPlan.Id"/>.</param>
        /// <param name="successUrl">URL NOWPayments redirects to on payment completion.</param>
        /// <param name="cancelUrl">URL NOWPayments redirects to when the buyer cancels.</param>
        /// <returns>The hosted invoice URL to redirect the user to.</returns>
        public async Task<string> CreateCheckoutSessionAsync(
            string userId, int planId, string successUrl, string cancelUrl)
        {
            _logger.LogInformation(
                "NOWPayments checkout step 1/6: start invoice creation. UserId={UserId} PlanId={PlanId}",
                userId, planId);

            var plan = await _context.SubscriptionPlans
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == planId && p.IsActive);

            if (plan == null)
                throw new InvalidOperationException($"SubscriptionPlan {planId} not found or inactive.");

            _logger.LogInformation(
                "NOWPayments checkout step 2/6: plan resolved. PlanId={PlanId} Tier={Tier} MonthlyPrice={MonthlyPrice} IsAnnual={IsAnnual} Currency={Currency}",
                plan.Id, plan.Tier, plan.MonthlyPrice, plan.IsAnnual, plan.Currency);

            var orderId = $"{userId}:{planId}:{Guid.NewGuid():N}";
            var successUrlWithOrderId = AppendQueryParameter(successUrl, "orderId", orderId);

            _logger.LogInformation(
                "NOWPayments checkout step 3/6: generated orderId and callback URLs. OrderId={OrderId} SuccessUrl={SuccessUrl} CancelUrl={CancelUrl}",
                orderId, successUrlWithOrderId, cancelUrl);

            var chargeAmount = plan.IsAnnual ? plan.MonthlyPrice * 12 : plan.MonthlyPrice;

            var bodyDict = new Dictionary<string, object>
            {
                ["price_amount"]        = (double)chargeAmount,
                ["price_currency"]      = plan.Currency.ToLower(),
                ["order_id"]            = orderId,
                ["order_description"]   = plan.Name,
                ["success_url"]         = successUrlWithOrderId,
                ["cancel_url"]          = cancelUrl,
                ["is_fixed_rate"]       = false,
                ["is_fee_paid_by_user"] = false
            };

            // Only include ipn_callback_url when configured; sending an empty string can override
            // the globally-configured IPN URL in the NOWPayments dashboard.
            if (!string.IsNullOrWhiteSpace(_options.IpnCallbackUrl))
                bodyDict["ipn_callback_url"] = _options.IpnCallbackUrl;
            else
                _logger.LogCritical(
                    "NOWPayments IpnCallbackUrl is not configured. " +
                    "IPN webhooks will fall back to the dashboard default. " +
                    "Set NOWPayments:IpnCallbackUrl in configuration.");

            var json = JsonSerializer.Serialize(bodyDict);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            _logger.LogInformation(
                "NOWPayments checkout step 4/6: posting invoice request. OrderId={OrderId} HasIpnCallbackUrl={HasIpnCallbackUrl}",
                orderId, !string.IsNullOrWhiteSpace(_options.IpnCallbackUrl));

            var response = await _http.PostAsync("invoice", content);
            var responseBody = await response.Content.ReadAsStringAsync();

            _logger.LogInformation(
                "NOWPayments checkout step 5/6: invoice API response received. OrderId={OrderId} Status={Status}",
                orderId, response.StatusCode);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("NOWPayments invoice creation failed. Status={Status} Body={Body}",
                    response.StatusCode, responseBody);
                throw new InvalidOperationException(
                    $"NOWPayments {(int)response.StatusCode}: {responseBody}");
            }

            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;

            var invoiceUrl = root.GetProperty("invoice_url").GetString()
                ?? throw new InvalidOperationException("NOWPayments returned no invoice URL.");

            _logger.LogInformation(
                "NOWPayments checkout step 6/6: invoice created. OrderId={OrderId} InvoiceUrl={InvoiceUrl}",
                orderId, invoiceUrl);

            // Cache invoiceId → orderId and persist to DB so the success-page fallback
            // can find the payment_id via the IPN payload, and so it survives app restarts.
            if (root.TryGetProperty("id", out var invoiceIdProp))
            {
                var invoiceId = invoiceIdProp.ValueKind == JsonValueKind.Number
                    ? invoiceIdProp.GetInt64().ToString()
                    : invoiceIdProp.GetString();

                if (!string.IsNullOrEmpty(invoiceId))
                {
                    s_invoiceToOrder[invoiceId] = orderId;

                    // Write a lightweight audit row so the invoiceId survives app restarts.
                    // ExternalEventId = invoiceId, ExternalSubscriptionId = orderId.
                    _context.SubscriptionEvents.Add(new Models.SubscriptionEvent
                    {
                        UserId = userId,
                        Provider = "NOWPayments",
                        EventType = "PaymentInitiated",
                        ExternalEventId = invoiceId,
                        ExternalSubscriptionId = orderId,
                        OccurredAt = DateTime.UtcNow
                    });
                    await _context.SaveChangesAsync();

                    _logger.LogInformation(
                        "NOWPayments checkout audit persisted. OrderId={OrderId} InvoiceId={InvoiceId}",
                        orderId, invoiceId);
                }
            }

            return invoiceUrl;
        }

        private static string AppendQueryParameter(string url, string key, string value)
        {
            var separator = url.Contains('?') ? "&" : "?";
            return $"{url}{separator}{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value)}";
        }

        /// <summary>
        /// NOWPayments has no customer portal. Returns the subscription manage page URL.
        /// </summary>
        public Task<string> GetBillingPortalUrlAsync(string userId, string returnUrl)
        {
            return Task.FromResult(returnUrl);
        }

        /// <summary>
        /// Fetches a single payment from the NOWPayments API by payment ID.
        /// Returns the raw JSON body, or null if the request failed.
        /// </summary>
        public async Task<string?> GetPaymentRawAsync(string paymentId)
        {
            _logger.LogDebug("NOWPayments fetch payment start. PaymentId={Id}", paymentId);

            var response = await _http.GetAsync($"payment/{paymentId}");
            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("NOWPayments GetPayment failed. PaymentId={Id} Status={Status} Body={Body}",
                    paymentId, response.StatusCode, body);
                return null;
            }

            _logger.LogDebug("NOWPayments fetch payment success. PaymentId={Id}", paymentId);

            return body;
        }

        /// <summary>
        /// Finds a <em>finished</em> payment for <paramref name="orderId"/> without requiring
        /// JWT credentials, using only the x-api-key single-payment endpoint.
        /// Strategy:
        ///   1. Scan <see cref="AutoSignalsDbContext.SubscriptionEvents"/> for any IPN row whose
        ///      <c>RawPayload</c> contains the <c>order_id</c> or the matching <c>invoice_id</c>
        ///      and extract the <c>payment_id</c>.
        ///   2. For each candidate, call <c>GET /v1/payment/{id}</c> (x-api-key only) and return
        ///      the raw JSON if <c>payment_status == "finished"</c>.
        /// Returns <c>null</c> if no finished payment is found (still confirming on-chain).
        /// </summary>
        public async Task<string?> GetFinishedPaymentRawByOrderIdAsync(string orderId)
        {
            _logger.LogInformation(
                "NOWPayments fallback step 1/6: start finished-payment lookup by orderId. OrderId={OrderId}",
                orderId);

            var orderEventCounts = await _context.SubscriptionEvents
                .AsNoTracking()
                .Where(e => e.Provider == "NOWPayments" && e.ExternalSubscriptionId == orderId)
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    Total = g.Count(),
                    WithRawPayload = g.Count(e => e.RawPayload != null)
                })
                .FirstOrDefaultAsync();

            var totalOrderEvents = orderEventCounts?.Total ?? 0;
            var orderEventsWithPayload = orderEventCounts?.WithRawPayload ?? 0;

            _logger.LogInformation(
                "NOWPayments fallback diagnostics: order events in DB. OrderId={OrderId} TotalEvents={TotalEvents} EventsWithRawPayload={EventsWithRawPayload}",
                orderId, totalOrderEvents, orderEventsWithPayload);

            // Resolve invoiceId — check in-memory cache first, then DB (survives restarts).
            var knownInvoiceId = s_invoiceToOrder
                .Where(kv => kv.Value == orderId)
                .Select(kv => kv.Key)
                .FirstOrDefault();

            _logger.LogInformation(
                "NOWPayments fallback step 2/6: cache lookup done. OrderId={OrderId} KnownInvoiceId={InvoiceId}",
                orderId, knownInvoiceId);

            if (knownInvoiceId == null)
            {
                var initiated = await _context.SubscriptionEvents
                    .AsNoTracking()
                    .Where(e => e.Provider == "NOWPayments"
                             && e.EventType == "PaymentInitiated"
                             && e.ExternalSubscriptionId == orderId
                             && e.ExternalEventId != null)
                    .OrderByDescending(e => e.OccurredAt)
                    .Select(e => e.ExternalEventId)
                    .FirstOrDefaultAsync();

                if (initiated != null)
                {
                    knownInvoiceId = initiated;
                    s_invoiceToOrder[initiated] = orderId; // repopulate cache

                    _logger.LogInformation(
                        "NOWPayments fallback step 3/6: invoice id recovered from DB. OrderId={OrderId} InvoiceId={InvoiceId}",
                        orderId, knownInvoiceId);
                }
            }

            // Pull all NOWPayments IPN payloads from our own DB and find matching payment_ids.
            var candidates = await _context.SubscriptionEvents
                .AsNoTracking()
                .Where(e => e.Provider == "NOWPayments" && e.RawPayload != null)
                .Select(e => new { e.ExternalEventId, e.RawPayload })
                .ToListAsync();

            var paymentIds = new HashSet<string>(StringComparer.Ordinal);

            foreach (var row in candidates)
            {
                bool matchesOrderId = false;
                try
                {
                    using var doc = JsonDocument.Parse(row.RawPayload!);
                    var root = doc.RootElement;

                    var rowOrderId = root.TryGetProperty("order_id", out var oidProp)
                        ? oidProp.GetString()
                        : null;

                    var rowInvoiceId = root.TryGetProperty("invoice_id", out var iidProp)
                        ? (iidProp.ValueKind == JsonValueKind.Number
                            ? iidProp.GetInt64().ToString()
                            : iidProp.GetString())
                        : null;

                    bool invoiceMatch = knownInvoiceId != null
                        && rowInvoiceId != null
                        && rowInvoiceId == knownInvoiceId;

                    if (rowOrderId != orderId && !invoiceMatch)
                        continue;

                    matchesOrderId = true;

                    if (root.TryGetProperty("payment_id", out var pidProp))
                    {
                        var pid = pidProp.ValueKind == JsonValueKind.Number
                            ? pidProp.GetInt64().ToString()
                            : pidProp.GetString();

                        if (!string.IsNullOrEmpty(pid))
                            paymentIds.Add(pid);
                    }
                }
                catch (JsonException) { /* malformed payload — skip */ }

                if (matchesOrderId && !string.IsNullOrEmpty(row.ExternalEventId))
                    paymentIds.Add(row.ExternalEventId);
            }

            _logger.LogDebug(
                "NOWPayments fallback: {Count} candidate payment IDs for orderId {OrderId}.",
                paymentIds.Count, orderId);

            _logger.LogInformation(
                "NOWPayments fallback step 4/6: candidate IDs from local events. OrderId={OrderId} CandidateCount={Count}",
                orderId, paymentIds.Count);

            // If we have an invoiceId, query the invoice endpoint (x-api-key auth) and
            // extract payment IDs, including additional_payments, without requiring JWT.
            if (!string.IsNullOrWhiteSpace(knownInvoiceId))
            {
                var invoiceCandidateIds = await GetPaymentIdsFromInvoiceAsync(orderId, knownInvoiceId);
                paymentIds.UnionWith(invoiceCandidateIds);

                _logger.LogDebug(
                    "NOWPayments fallback after invoice scan: {Count} candidate payment IDs for orderId {OrderId}.",
                    paymentIds.Count, orderId);

                _logger.LogInformation(
                    "NOWPayments fallback step 5/6: candidate IDs after invoice scan. OrderId={OrderId} CandidateCount={Count}",
                    orderId, paymentIds.Count);
            }

            foreach (var paymentId in paymentIds)
            {
                _logger.LogInformation(
                    "NOWPayments fallback step 6/6: checking payment status. OrderId={OrderId} PaymentId={PaymentId}",
                    orderId, paymentId);

                var raw = await GetPaymentRawAsync(paymentId);
                if (raw == null) continue;

                try
                {
                    using var doc = JsonDocument.Parse(raw);
                    var status = doc.RootElement.TryGetProperty("payment_status", out var ps)
                        ? ps.GetString()
                        : null;

                    if (string.Equals(status, "finished", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogInformation(
                            "NOWPayments fallback success: finished payment found. OrderId={OrderId} PaymentId={PaymentId}",
                            orderId, paymentId);
                        return raw;
                    }
                }
                catch (JsonException) { /* skip */ }
            }

            _logger.LogWarning(
                "NOWPayments fallback complete: no finished payment found. OrderId={OrderId} KnownInvoiceId={InvoiceId} CandidateCount={Count} TotalOrderEvents={TotalEvents} EventsWithRawPayload={EventsWithRawPayload}. " +
                "If CandidateCount is 0, no usable payment_id reached this app yet (webhook not delivered/rejected and no payment_id in success redirect).",
                orderId, knownInvoiceId, paymentIds.Count, totalOrderEvents, orderEventsWithPayload);

            return null;
        }

        private async Task<string?> GetInvoiceRawAsync(string invoiceId)
        {
            _logger.LogDebug("NOWPayments fetch invoice start. InvoiceId={Id}", invoiceId);

            var response = await _http.GetAsync($"invoice/{invoiceId}");
            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    _logger.LogDebug(
                        "NOWPayments GetInvoice endpoint unavailable or unsupported for this account. InvoiceId={Id} Status={Status} Body={Body}",
                        invoiceId, response.StatusCode, body);
                    return null;
                }

                _logger.LogWarning(
                    "NOWPayments GetInvoice failed. InvoiceId={Id} Status={Status} Body={Body}",
                    invoiceId, response.StatusCode, body);
                return null;
            }

            _logger.LogDebug("NOWPayments fetch invoice success. InvoiceId={Id}", invoiceId);

            return body;
        }

        private async Task<HashSet<string>> GetPaymentIdsFromInvoiceAsync(string orderId, string invoiceId)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var raw = await GetInvoiceRawAsync(invoiceId);
            if (raw == null)
                return ids;

            try
            {
                using var doc = JsonDocument.Parse(raw);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                    return ids;

                var rowOrderId = TryGetString(root, "order_id");
                var rowInvoiceId = TryGetString(root, "invoice_id") ?? TryGetString(root, "id");

                var invoiceMatch = !string.IsNullOrEmpty(rowInvoiceId)
                    && string.Equals(rowInvoiceId, invoiceId, StringComparison.Ordinal);

                if (!string.Equals(rowOrderId, orderId, StringComparison.Ordinal) && !invoiceMatch)
                    return ids;

                AddCandidateId(ids, root, "payment_id");

                if (root.TryGetProperty("payments", out var payments) && payments.ValueKind == JsonValueKind.Array)
                {
                    foreach (var payment in payments.EnumerateArray())
                    {
                        if (payment.ValueKind != JsonValueKind.Object)
                            continue;

                        AddCandidateId(ids, payment, "payment_id");
                        AddCandidateId(ids, payment, "id");
                    }
                }

                if (root.TryGetProperty("additional_payments", out var additionalPayments))
                    AddIdsFromArrayLikeValue(ids, additionalPayments);

                if (root.TryGetProperty("payment_ids", out var paymentIds))
                    AddIdsFromArrayLikeValue(ids, paymentIds);
            }
            catch (JsonException)
            {
                _logger.LogWarning("NOWPayments invoice payload was malformed for invoiceId {InvoiceId}.", invoiceId);
            }

            return ids;
        }

        private static string? TryGetString(JsonElement parent, string propertyName)
        {
            if (!parent.TryGetProperty(propertyName, out var prop))
                return null;

            return prop.ValueKind switch
            {
                JsonValueKind.String => prop.GetString(),
                JsonValueKind.Number => prop.GetInt64().ToString(),
                _ => null
            };
        }

        private static void AddCandidateId(HashSet<string> target, JsonElement parent, string propertyName)
        {
            var value = TryGetString(parent, propertyName);
            if (!string.IsNullOrWhiteSpace(value))
                target.Add(value);
        }

        private static void AddIdsFromArrayLikeValue(HashSet<string> target, JsonElement value)
        {
            if (value.ValueKind != JsonValueKind.Array)
                return;

            foreach (var item in value.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object)
                {
                    AddCandidateId(target, item, "payment_id");
                    AddCandidateId(target, item, "id");
                }
                else if (item.ValueKind == JsonValueKind.Number)
                {
                    target.Add(item.GetInt64().ToString());
                }
                else if (item.ValueKind == JsonValueKind.String)
                {
                    var str = item.GetString();
                    if (!string.IsNullOrWhiteSpace(str))
                        target.Add(str);
                }
            }
        }

        /// <summary>
        /// Crypto payments cannot be cancelled remotely. Manual admin action required.
        /// </summary>
        public Task CancelSubscriptionAsync(string externalSubscriptionId)
        {
            _logger.LogInformation(
                "CancelSubscriptionAsync called for NOWPayments subscription {Id}. No remote action taken.",
                externalSubscriptionId);
            return Task.CompletedTask;
        }
    }
}
