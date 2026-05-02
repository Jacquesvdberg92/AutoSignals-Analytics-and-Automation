# NOWPayments Payment Flow — Fix Implementation Plan

> **Status:** Ready to implement  
> **Branch:** `master`  
> **Root cause confirmed:** Critical idempotency bug in `NOWPaymentsWebhookService.cs`  
> **Estimated steps:** 9 (Steps 1–4 are minimum for a working production flow)

---

## Table of Contents

1. [Root Cause Summary](#root-cause-summary)
2. [Bug Registry](#bug-registry)
3. [Step 1 — Fix Idempotency Logic (CRITICAL)](#step-1--fix-idempotency-logic-critical)
4. [Step 2 — Guard HMAC Length Mismatch (HIGH)](#step-2--guard-hmac-length-mismatch-high)
5. [Step 3 — Remove Broken Secondary Fallback (HIGH)](#step-3--remove-broken-secondary-fallback-high)
6. [Step 4 — Atomic Activation Transaction (HIGH)](#step-4--atomic-activation-transaction-high)
7. [Step 5 — IPN Callback URL Validation (MEDIUM)](#step-5--ipn-callback-url-validation-medium)
8. [Step 6 — Paginated Payment List Lookup (MEDIUM)](#step-6--paginated-payment-list-lookup-medium)
9. [Step 7 — Background IPN Recovery Service (MEDIUM)](#step-7--background-ipn-recovery-service-medium)
10. [Step 8 — Success Page UX Improvements (LOW)](#step-8--success-page-ux-improvements-low)
11. [Step 9 — Admin Payment Diagnostics Panel (LOW)](#step-9--admin-payment-diagnostics-panel-low)
12. [Test Checklist](#test-checklist)
13. [Deployment Checklist](#deployment-checklist)

---

## Root Cause Summary

NOWPayments fires an IPN webhook for **every** payment status change:

```
waiting → confirming → confirmed → sending → finished
```

Each IPN carries the **same `payment_id`**. The current code's `default` handler processes
`waiting`, `confirming`, `confirmed`, and `sending` by calling `WriteAuditEventAsync()`
which writes a `SubscriptionEvent` row with `ExternalEventId = payment_id`.

The idempotency guard at the top of `HandleEventAsync` then blocks the `finished` IPN
because it finds that earlier row and skips processing entirely — **the subscription is
never activated**.

```
IPN #1  status=waiting   → default handler → writes ExternalEventId=PAY-123  ← poisons key
IPN #2  status=confirming → idempotency check finds PAY-123 → SKIPPED
IPN #3  status=confirmed  → idempotency check finds PAY-123 → SKIPPED
IPN #4  status=sending    → idempotency check finds PAY-123 → SKIPPED
IPN #5  status=finished   → idempotency check finds PAY-123 → SKIPPED ← subscription never activated
```

---

## Bug Registry

| # | Severity | File | Description |
|---|----------|------|-------------|
| 1 | 🔴 CRITICAL | `NOWPaymentsWebhookService.cs` | Intermediate-status IPN handlers write `ExternalEventId = payment_id`, poisoning the idempotency key and blocking the `finished` IPN |
| 2 | 🔴 HIGH | `SubscriptionController.cs` | Secondary success-page fallback constructs `userId:planId` (no nonce) — never matches a real NOWPayments `order_id` |
| 3 | 🟠 HIGH | `SubscriptionService.cs` + `NOWPaymentsWebhookService.cs` | Two separate `SaveChangesAsync` calls create a race window where a concurrent IPN can double-activate |
| 4 | 🟠 HIGH | `NOWPaymentsWebhookService.cs` | `FixedTimeEquals` throws `ArgumentException` when signature lengths differ, causing a 500 → NOWPayments retry storm |
| 5 | 🟠 MEDIUM | `NOWPaymentsOptions.cs` + `NOWPaymentsSubscriptionProvider.cs` | Empty `IpnCallbackUrl` sends `""` to NOWPayments, silently disabling IPN for that invoice |
| 6 | 🟡 MEDIUM | `NOWPaymentsSubscriptionProvider.cs` | Payment list fallback only checks first 100 payments; fails silently for high-volume accounts |
| 7 | 🟡 LOW | `Views/Subscription/Success.cshtml` | Third fallback branch (⌛) has no auto-refresh; user can wait forever with no feedback |

---

## Step 1 — Fix Idempotency Logic (CRITICAL)

**File:** `Services/NOWPayments/NOWPaymentsWebhookService.cs`

### What changes

| Location | Before | After |
|----------|--------|-------|
| Idempotency check | `AnyAsync(e => e.ExternalEventId == paymentId)` — matches ANY event type | `AnyAsync(e => e.ExternalEventId == paymentId && (e.EventType == SubscriptionCreated \|\| e.EventType == SubscriptionUpgraded))` — matches activation events only |
| `default` handler | Calls `WriteAuditEventAsync(paymentId, ...)` — writes idempotency-poisoning row | Log only — **no DB write** |
| `partially_paid` handler | Calls `WriteAuditEventAsync(paymentId, ...)` — poisons key if payment later becomes `finished` | Calls `WriteAuditEventAsync(null, ...)` — no `ExternalEventId` |
| `HandlePaymentFinishedAsync` | Writes idempotency marker **after** `ActivateSubscriptionAsync` | Writes idempotency marker **before** `ActivateSubscriptionAsync` so a crash between steps can't leave an orphaned activation with no marker |

### Exact edits

**1a. Idempotency check** (`HandleEventAsync`, ~line 143):

```csharp
// BEFORE
var alreadyProcessed = await _context.SubscriptionEvents
    .AnyAsync(e => e.ExternalEventId == paymentId);

// AFTER
var alreadyProcessed = await _context.SubscriptionEvents
    .AnyAsync(e => e.ExternalEventId == paymentId
               && (e.EventType == SubscriptionEventTypes.SubscriptionCreated
                   || e.EventType == SubscriptionEventTypes.SubscriptionUpgraded));
```

**1b. `default` handler** (~line 168):

```csharp
// BEFORE
default:
    _logger.LogDebug("NOWPayments IPN status {Status} not handled.", paymentStatus);
    await WriteAuditEventAsync("unknown", paymentId, null, null, orderId, null, rawBody);
    break;

// AFTER
default:
    _logger.LogInformation(
        "NOWPayments IPN status '{Status}' for payment {PaymentId}, order {OrderId} — no action taken.",
        paymentStatus, paymentId, orderId);
    break;
```

**1c. `partially_paid` handler** (~line 158):

```csharp
// BEFORE
await WriteAuditEventAsync("unknown", paymentId, "PaymentPartial", null, orderId, null, rawBody);

// AFTER — pass null for externalEventId so the key is not poisoned
var (partialUserId, _) = ParseOrderId(orderId);
await WriteAuditEventAsync(partialUserId ?? "unknown", null, "PaymentPartial", null, orderId, null, rawBody);
```

**1d. `WriteAuditEventAsync` signature change** — make `externalEventId` nullable:

```csharp
// BEFORE
private async Task WriteAuditEventAsync(
    string userId, string externalEventId, string? eventType, ...)

// AFTER
private async Task WriteAuditEventAsync(
    string userId, string? externalEventId, string? eventType, ...)
```

**1e. Idempotency marker written BEFORE activation** in `HandlePaymentFinishedAsync`:

```csharp
// AFTER — order: idempotency marker first, then activation, then email
// 1. Write the idempotency marker so concurrent IPNs are blocked even if activation throws.
await WriteAuditEventAsync(userId, paymentId, SubscriptionEventTypes.SubscriptionCreated,
    plan.Tier, orderId, amount, rawBody);

// 2. Activate subscription.
await _subscriptionService.ActivateSubscriptionAsync(
    userId, plan.Tier, "NOWPayments", paymentId, now, end);

// 3. Send confirmation email — non-critical.
try { await SendSubscriptionConfirmedEmailAsync(userId, plan.Tier); }
catch (Exception ex) { _logger.LogError(ex, "..."); }
```

> **Note:** Step 4 will merge both `SaveChangesAsync` calls into one transaction,
> making steps 1 and 2 above fully atomic.

---

## Step 2 — Guard HMAC Length Mismatch (HIGH)

**File:** `Services/NOWPayments/NOWPaymentsWebhookService.cs`

### What changes

`CryptographicOperations.FixedTimeEquals` throws `ArgumentException` when the two byte
arrays have different lengths. A malformed or truncated `x-nowpayments-sig` header causes
an unhandled exception → HTTP 500 → NOWPayments keeps retrying → retry storm.

### Exact edit

```csharp
// BEFORE
private static bool FixedTimeHexEquals(string a, string b)
{
    return CryptographicOperations.FixedTimeEquals(
        Encoding.ASCII.GetBytes(a),
        Encoding.ASCII.GetBytes(b));
}

// AFTER
private static bool FixedTimeHexEquals(string a, string b)
{
    if (a.Length != b.Length) return false;
    return CryptographicOperations.FixedTimeEquals(
        Encoding.ASCII.GetBytes(a),
        Encoding.ASCII.GetBytes(b));
}
```

---

## Step 3 — Remove Broken Secondary Fallback (HIGH)

**File:** `Controllers/SubscriptionController.cs`

### What changes

The secondary fallback (lines 163–193) constructs `candidateOrderId = $"{user.Id}:{candidatePlanId}"`.
This is missing the `:{nonce}` suffix that `NOWPaymentsSubscriptionProvider.CreateCheckoutSessionAsync`
always appends (`$"{userId}:{planId}:{Guid.NewGuid():N}"`). It will **never** match a real
payment in NOWPayments and generates misleading error logs.

Since `orderId` is always embedded in the success redirect URL by `AppendQueryParameter`,
the primary fallback (by exact `orderId`) is the correct and sufficient path.

### Exact edit — replace the secondary fallback block with a structured warning log:

```csharp
// REMOVE the entire "Secondary fallback: scan by order_id" block (lines 163–193)
// REPLACE with:
if (rawJson == null)
{
    _logger.LogWarning(
        "NOWPayments success-page: no finished payment found for user {UserId}. " +
        "OrderId={OrderId} PaymentId={PaymentId}. Payment may still be confirming on-chain.",
        user.Id, orderId, paymentId);
}
```

---

## Step 4 — Atomic Activation Transaction (HIGH)

**Files:** `Services/SubscriptionService.cs`, `Services/NOWPayments/NOWPaymentsWebhookService.cs`

### What changes

Currently `ActivateSubscriptionAsync` calls `_context.SaveChangesAsync()` internally,
then `WriteAuditEventAsync` calls it again. Between those two commits there is a window
where the subscription is active but no idempotency marker exists — a concurrent IPN
or the success-page fallback can re-trigger activation.

The fix introduces an overload of `ActivateSubscriptionAsync` that **stages** changes
without committing, letting the caller control the transaction.

### Exact edits

**4a. Add new interface method to `ISubscriptionService`:**

```csharp
// New method — stages DB changes without calling SaveChangesAsync
Task StageSubscriptionActivationAsync(string userId, SubscriptionTier tier,
    string provider, string externalSubscriptionId, DateTime start, DateTime end);
```

**4b. Implement `StageSubscriptionActivationAsync` in `SubscriptionService`:**

```csharp
// Identical logic to ActivateSubscriptionAsync but NO SaveChangesAsync at the end.
// The caller is responsible for committing the transaction.
public async Task StageSubscriptionActivationAsync(string userId, SubscriptionTier tier,
    string provider, string externalSubscriptionId, DateTime start, DateTime end)
{
    // ... same body as ActivateSubscriptionAsync but without the final SaveChangesAsync
}
```

**4c. Update `HandlePaymentFinishedAsync` to use a single transaction:**

```csharp
await using var tx = await _context.Database.BeginTransactionAsync();
try
{
    // Stage activation (no commit yet)
    await _subscriptionService.StageSubscriptionActivationAsync(
        userId, plan.Tier, "NOWPayments", paymentId, now, end);

    // Stage audit/idempotency event (no commit yet)
    _context.SubscriptionEvents.Add(new SubscriptionEvent
    {
        UserId      = userId,
        Provider    = "NOWPayments",
        EventType   = SubscriptionEventTypes.SubscriptionCreated,
        Tier        = plan.Tier,
        Amount      = amount,
        ExternalEventId          = paymentId,
        ExternalSubscriptionId   = orderId,
        OccurredAt  = DateTime.UtcNow,
        RawPayload  = rawBody
    });

    // Single atomic commit
    await _context.SaveChangesAsync();
    await tx.CommitAsync();
}
catch
{
    await tx.RollbackAsync();
    throw;
}

// Email is outside the transaction — non-critical
try { await SendSubscriptionConfirmedEmailAsync(userId, plan.Tier); }
catch (Exception ex) { _logger.LogError(ex, "..."); }
```

> **Note:** `UserManager` role operations (`AddToRoleAsync`, `RemoveFromRolesAsync`) use
> the `ApplicationDbContext`, not `AutoSignalsDbContext`, so they cannot be included in
> the same EF transaction. They are idempotent (adding an existing role is a no-op), so
> the acceptable risk is a successful DB commit followed by a role-sync failure — which
> is recoverable by the admin or on next login. Log a `LogError` if the role sync fails
> after a successful commit.

---

## Step 5 — IPN Callback URL Validation (MEDIUM)

**Files:** `Services/NOWPayments/NOWPaymentsOptions.cs`, `Program.cs`,
`Services/NOWPayments/NOWPaymentsSubscriptionProvider.cs`

### What changes

An empty `IpnCallbackUrl` causes the invoice request to send `"ipn_callback_url": ""`
to NOWPayments, potentially overriding the globally-configured IPN URL in the NOWPayments
dashboard and silently disabling IPN for that invoice.

### Exact edits

**5a. Add data annotations to `NOWPaymentsOptions`:**

```csharp
using System.ComponentModel.DataAnnotations;

public class NOWPaymentsOptions
{
    public const string SectionName = "NOWPayments";

    [Required(AllowEmptyStrings = false)]
    public string ApiKey { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string IpnSecret { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string IpnCallbackUrl { get; set; } = string.Empty;
}
```

**5b. Enable startup validation in `Program.cs`:**

```csharp
builder.Services
    .Configure<NOWPaymentsOptions>(builder.Configuration.GetSection(NOWPaymentsOptions.SectionName))
    .AddOptions<NOWPaymentsOptions>()
    .Bind(builder.Configuration.GetSection(NOWPaymentsOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
```

**5c. Guard empty URL in `CreateCheckoutSessionAsync`:**

```csharp
// Only include ipn_callback_url when it is configured.
// Sending an empty string can override the dashboard-configured global IPN URL.
var bodyDict = new Dictionary<string, object>
{
    ["price_amount"]      = (double)plan.MonthlyPrice,
    ["price_currency"]    = plan.Currency.ToLower(),
    ["order_id"]          = orderId,
    ["order_description"] = plan.Name,
    ["success_url"]       = successUrlWithOrderId,
    ["cancel_url"]        = cancelUrl,
    ["is_fixed_rate"]     = false,
    ["is_fee_paid_by_user"] = false
};

if (!string.IsNullOrWhiteSpace(_options.IpnCallbackUrl))
    bodyDict["ipn_callback_url"] = _options.IpnCallbackUrl;
else
    _logger.LogCritical(
        "NOWPayments IpnCallbackUrl is not configured. IPN webhooks will use the dashboard default. " +
        "Set NOWPayments:IpnCallbackUrl in configuration.");
```

---

## Step 6 — Paginated Payment List Lookup (MEDIUM)

**File:** `Services/NOWPayments/NOWPaymentsSubscriptionProvider.cs`

### What changes

`GetFinishedPaymentRawByOrderIdAsync` fetches only the first 100 payments. For accounts
with a large payment history the target payment may not appear on page 0, causing the
fallback to silently return `null` even when the payment is finished.

### Exact edit — add pagination loop:

```csharp
public async Task<string?> GetFinishedPaymentRawByOrderIdAsync(string orderId)
{
    const int PageSize = 100;
    const int MaxPages = 10; // guard against infinite loops (1 000 payments max)

    string? selectedPaymentId = null;
    DateTime selectedUpdatedAt = DateTime.MinValue;

    for (int page = 0; page < MaxPages; page++)
    {
        var url = $"payment?limit={PageSize}&page={page}&sortBy=updated_at&orderBy=desc" +
                  $"&order_id={Uri.EscapeDataString(orderId)}";

        var listResponse = await _http.GetAsync(url);
        var listBody     = await listResponse.Content.ReadAsStringAsync();

        if (!listResponse.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "NOWPayments payment list failed. OrderId={OrderId} Page={Page} Status={Status}",
                orderId, page, listResponse.StatusCode);
            break;
        }

        using var doc = JsonDocument.Parse(listBody);

        var payments = doc.RootElement.ValueKind == JsonValueKind.Array
            ? doc.RootElement
            : doc.RootElement.TryGetProperty("data", out var data) ? data : default;

        if (payments.ValueKind != JsonValueKind.Array) break;

        var pageCount = 0;
        foreach (var payment in payments.EnumerateArray())
        {
            pageCount++;

            if (payment.TryGetProperty("order_id", out var oid) && oid.GetString() != orderId)
                continue;

            if (!payment.TryGetProperty("payment_status", out var ps) || ps.GetString() != "finished")
                continue;

            if (!payment.TryGetProperty("payment_id", out var pid)) continue;

            var paymentId = pid.ValueKind == JsonValueKind.Number
                ? pid.GetInt64().ToString() : pid.GetString();
            if (string.IsNullOrEmpty(paymentId)) continue;

            var updatedAt = DateTime.MinValue;
            if (payment.TryGetProperty("updated_at", out var upd)
                && upd.ValueKind == JsonValueKind.String
                && DateTime.TryParse(upd.GetString(), out var parsed))
                updatedAt = parsed;

            if (selectedPaymentId == null || updatedAt >= selectedUpdatedAt)
            {
                selectedPaymentId = paymentId;
                selectedUpdatedAt = updatedAt;
            }
        }

        // Stop paging if we got a full match or the page was not full (last page)
        if (selectedPaymentId != null || pageCount < PageSize) break;
    }

    return selectedPaymentId == null ? null : await GetPaymentRawAsync(selectedPaymentId);
}
```

---

## Step 7 — Background IPN Recovery Service (MEDIUM)

**New file:** `Services/NOWPayments/NOWPaymentsRecoveryService.cs`
**Edit:** `Program.cs`

### What it does

A `BackgroundService` that runs every 2 minutes. It scans `SubscriptionEvents` for
payments that received an intermediate-status IPN (e.g. the old `"unknown"` rows already
in the DB) but never received a `SubscriptionCreated` activation event within the
following 60 minutes. For each, it calls `GetFinishedPaymentRawByOrderIdAsync` and, if
the payment is now `finished` on NOWPayments, processes it via `HandleEventAsync`.

This makes the system **self-healing** for any IPN delivery failures, regardless of their
cause (misconfigured URL, server downtime during IPN window, etc.).

### Key logic outline

```csharp
public class NOWPaymentsRecoveryService : BackgroundService
{
    // Runs every 2 minutes
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
            await TryRecoverPendingPaymentsAsync(stoppingToken);
        }
    }

    private async Task TryRecoverPendingPaymentsAsync(CancellationToken ct)
    {
        // Find order_ids that have a SubscriptionEvent row but NO SubscriptionCreated event,
        // created within the past 24 hours (avoids processing ancient orphaned records).
        var cutoff = DateTime.UtcNow.AddHours(-24);
        var orphaned = await _context.SubscriptionEvents
            .Where(e => e.Provider == "NOWPayments"
                     && e.OccurredAt >= cutoff
                     && e.ExternalSubscriptionId != null              // has an order_id
                     && e.EventType != SubscriptionEventTypes.SubscriptionCreated
                     && e.EventType != SubscriptionEventTypes.SubscriptionUpgraded
                     && e.EventType != SubscriptionEventTypes.PaymentFailed)
            .Select(e => e.ExternalSubscriptionId!)
            .Distinct()
            .ToListAsync(ct);

        // Exclude order_ids that already have an activation event
        var activated = await _context.SubscriptionEvents
            .Where(e => e.Provider == "NOWPayments"
                     && (e.EventType == SubscriptionEventTypes.SubscriptionCreated
                      || e.EventType == SubscriptionEventTypes.SubscriptionUpgraded)
                     && e.ExternalSubscriptionId != null)
            .Select(e => e.ExternalSubscriptionId!)
            .ToHashSetAsync(ct);

        var toRecover = orphaned.Where(o => !activated.Contains(o)).ToList();

        foreach (var orderId in toRecover)
        {
            var rawJson = await _provider.GetFinishedPaymentRawByOrderIdAsync(orderId);
            if (rawJson == null) continue;

            _logger.LogInformation("NOWPayments recovery: processing finished payment for order {OrderId}.", orderId);
            await _webhookService.HandleEventAsync(rawJson);
        }
    }
}
```

**Registration in `Program.cs`:**

```csharp
builder.Services.AddScoped<NOWPaymentsRecoveryService>();
builder.Services.AddHostedService<NOWPaymentsRecoveryService>();
```

> **Scoping note:** `BackgroundService` is singleton but needs scoped services
> (`AutoSignalsDbContext`, `NOWPaymentsWebhookService`). Use `IServiceScopeFactory`
> to create a new scope per execution cycle.

---

## Step 8 — Success Page UX Improvements (LOW)

**File:** `Views/Subscription/Success.cshtml`

### What changes

| Issue | Fix |
|-------|-----|
| Auto-refresh is only on `paymentPending` — the third branch (⌛ "activation in progress") never refreshes | Add auto-refresh to the third branch too |
| Fixed 5 s refresh regardless of wait time — hammers the NOWPayments API | Progressive intervals passed via query param: 5 s → 10 s → 20 s → 30 s cap |
| No timeout — user can wait forever | After 15 minutes show "Contact support" with `orderId` pre-filled |
| `paymentPending` and third branch are visually similar — confusing | Clarify copy: pending = on-chain confirming; third branch = internal activation delay |

### Exact edit — progressive refresh interval logic:

```csharp
// In Success() action, pass the attempt count:
ViewBag.Attempt = (Request.Query.TryGetValue("attempt", out var a) 
                   && int.TryParse(a, out var n)) ? n : 0;
```

```razor
@{
    int attempt       = ViewBag.Attempt is int att ? att : 0;
    int nextAttempt   = attempt + 1;
    int refreshSecs   = attempt switch { 0 => 5, 1 => 10, 2 => 20, _ => 30 };
    bool timedOut     = attempt >= 30; // ~15 minutes
    var  refreshUrl   = Url.Action("Success", "Subscription",
                            new { planId, payment_id = paymentId, orderId, attempt = nextAttempt });
}

@if (paymentPending && !timedOut)
{
    <meta http-equiv="refresh" content="@refreshSecs;url=@refreshUrl" />
}
```

### Timeout state (new branch):

```razor
@if (timedOut)
{
    <h2>Payment taking longer than expected</h2>
    <p>Your payment may still be confirming. Please contact support with reference:</p>
    <code>@orderId</code>
    <a href="mailto:support@autosignals.xyz?subject=Payment+pending&body=OrderId:+@orderId"
       class="btn btn-primary">Email Support</a>
    <a asp-action="Manage" asp-controller="Subscription" class="btn btn-outline-secondary">
        Check Status
    </a>
}
```

---

## Step 9 — Admin Payment Diagnostics Panel (LOW)

**File:** `Controllers/AdminController.cs` (existing)  
**New view:** `Views/Admin/PaymentDiagnostics.cshtml`

### What it adds

- Table of all `SubscriptionEvents` where `Provider == "NOWPayments"`, grouped by `ExternalEventId` (payment_id)
- For each payment group: which status IPNs arrived, whether activation occurred, user email, order_id
- **"Force Re-activate"** button — calls `HandleEventAsync` after clearing the idempotency block for that `paymentId` (admin-only, requires `[Authorize(Roles = "Admin")]`)
- Filter by date range, user email, status (activated / pending / failed)

### Route

```
GET  /Admin/PaymentDiagnostics
POST /Admin/ForceReactivate  (body: { paymentId, orderId })
```

---

## Test Checklist

### Manual end-to-end tests (staging)

- [ ] Complete a test payment → subscription activates without manual intervention
- [ ] Confirm IPN sequence in logs: `waiting` → no DB write → `finished` → activation
- [ ] Navigate to success page immediately after redirect → subscription shows as active
- [ ] Simulate IPN arriving before user redirect → success page shows active immediately
- [ ] Simulate IPN arriving after user redirect → success-page fallback activates within one 5 s refresh
- [ ] Replay the same `finished` IPN twice → second replay skipped (idempotency check)
- [ ] Send a `partially_paid` IPN followed by `finished` → activation succeeds (no key poisoning)
- [ ] Send a malformed `x-nowpayments-sig` header → returns 401, no exception thrown
- [ ] Remove `IpnCallbackUrl` from config → critical log warning on startup; invoice still created

### Unit / integration tests to write

- [ ] `NOWPaymentsWebhookService.HandleEventAsync` — `waiting` IPN does not write `ExternalEventId`
- [ ] `NOWPaymentsWebhookService.HandleEventAsync` — `finished` IPN after `waiting` activates subscription
- [ ] `NOWPaymentsWebhookService.IsValidSignature` — truncated signature returns `false`, not exception
- [ ] `NOWPaymentsSubscriptionProvider.GetFinishedPaymentRawByOrderIdAsync` — paginated correctly
- [ ] `SubscriptionService.StageSubscriptionActivationAsync` — does not call `SaveChangesAsync`

---

## Deployment Checklist

- [ ] Ensure `NOWPayments:IpnCallbackUrl` is set to the **public HTTPS production URL**
  ```
  https://autosignals.xyz/api/nowpayments/webhook
  ```
- [ ] Ensure `NOWPayments:ApiKey` and `NOWPayments:IpnSecret` match the NOWPayments dashboard
- [ ] Verify the IPN secret in the NOWPayments dashboard matches `NOWPayments:IpnSecret` in Azure Key Vault / User Secrets
- [ ] Run `dotnet ef database update` if any new migrations are added
- [ ] Confirm the `/api/nowpayments/webhook` endpoint returns HTTP 200 to a test POST (no auth required)
- [ ] Check NOWPayments dashboard → Settings → IPN → verify global IPN URL is also set as backup
- [ ] Monitor Application Insights / logs for `NOWPayments IPN status 'finished'` log entries on first live payment

---

## File Change Summary

| File | Steps | Type |
|------|-------|------|
| `Services/NOWPayments/NOWPaymentsWebhookService.cs` | 1, 2, 4 | Modify |
| `Services/NOWPayments/NOWPaymentsOptions.cs` | 5 | Modify |
| `Services/NOWPayments/NOWPaymentsSubscriptionProvider.cs` | 5, 6 | Modify |
| `Services/NOWPayments/NOWPaymentsRecoveryService.cs` | 7 | **New file** |
| `Services/ISubscriptionService.cs` | 4 | Modify |
| `Services/SubscriptionService.cs` | 4 | Modify |
| `Controllers/SubscriptionController.cs` | 3 | Modify |
| `Controllers/AdminController.cs` | 9 | Modify |
| `Views/Subscription/Success.cshtml` | 8 | Modify |
| `Views/Admin/PaymentDiagnostics.cshtml` | 9 | **New file** |
| `Program.cs` | 5, 7 | Modify |

---

*Generated from full static analysis of the AutoSignals payment flow — no assumptions, every finding traced to exact file and line.*
