using AutoSignals.Data;
using AutoSignals.Models;
using AutoSignals.Services;
using AutoSignals.Services.NOWPayments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace AutoSignals.Controllers
{
    [Authorize(Roles = "Admin")]
    public class AdminController : Controller
    {
        private readonly AdminSettingService _adminSettingService;
        private readonly AutoSignalsDbContext _context;
        private readonly KlineHistoryImportService _klineImport;
        private readonly ISubscriptionService _subscriptionService;
        private readonly UserManager<IdentityUser> _userManager;

        public AdminController(
            AdminSettingService adminSettingService,
            AutoSignalsDbContext context,
            KlineHistoryImportService klineImport,
            ISubscriptionService subscriptionService,
            UserManager<IdentityUser> userManager)
        {
            _adminSettingService = adminSettingService;
            _context = context;
            _klineImport = klineImport;
            _subscriptionService = subscriptionService;
            _userManager = userManager;
        }

        [HttpGet("/Admin/KlineSettings")]
        public async Task<IActionResult> KlineSettings()
        {
            ViewBag.KlineChartsEnabled = await _adminSettingService.IsEnabledAsync("KlineChartsEnabled");
            ViewBag.RowCount          = await _context.KLineAssetPrices.CountAsync();
            ViewBag.SymbolCount       = await _context.KLineAssetPrices
                                            .Select(k => new { k.Symbol, k.Type })
                                            .Distinct()
                                            .CountAsync();
            ViewBag.OldestSnapshot    = await _context.KLineAssetPrices
                                            .OrderBy(k => k.Time)
                                            .Select(k => (DateTime?)k.Time)
                                            .FirstOrDefaultAsync();
            ViewBag.NewestSnapshot    = await _context.KLineAssetPrices
                                            .OrderByDescending(k => k.Time)
                                            .Select(k => (DateTime?)k.Time)
                                            .FirstOrDefaultAsync();
            return View();
        }

        [HttpPost("/Admin/KlineSettings")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> KlineSettingsToggle(bool enabled)
        {
            await _adminSettingService.SetAsync("KlineChartsEnabled", enabled ? "true" : "false");
            TempData["Success"] = $"Kline data collection {(enabled ? "enabled" : "disabled")}.";
            return RedirectToAction(nameof(KlineSettings));
        }

        [HttpPost("/Admin/KlineImport")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> KlineImport(string exchange, string symbol, string interval, int limit)
        {
            try
            {
                var inserted = await _klineImport.ImportAsync(exchange, symbol.Trim(), interval, limit);
                TempData["Success"] = inserted > 0
                    ? $"Imported {inserted:N0} new {interval} candles for {symbol} from {KlineHistoryImportService.ExchangeLabels[exchange]}."
                    : $"No new candles to import — all {interval} data for {symbol} is already up to date.";
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Import failed: {ex.Message}";
            }

            return RedirectToAction(nameof(KlineSettings));
        }

        [HttpPost("/Admin/KlineBulkImport")]
        [ValidateAntiForgeryToken]
        public IActionResult KlineBulkImport()
        {
            var started = _klineImport.StartBulkImport();
            if (!started)
                TempData["Error"] = "A bulk import is already running.";

            return RedirectToAction(nameof(KlineSettings));
        }

        [HttpGet("/Admin/KlineBulkImportStatus")]
        public IActionResult KlineBulkImportStatus()
        {
            var p = KlineHistoryImportService.BulkProgress;
            return Json(new
            {
                isRunning      = p.IsRunning,
                total          = p.Total,
                completed      = p.Completed,
                inserted       = p.Inserted,
                errors         = p.Errors,
                percentComplete = p.PercentComplete,
                currentSymbol  = p.CurrentSymbol,
                startedAt      = p.StartedAt,
                finishedAt     = p.FinishedAt,
                lastError      = p.LastError,
            });
        }

        [HttpGet("/Admin/UserTier")]
        public async Task<IActionResult> UserTier(string? email)
        {
            if (string.IsNullOrWhiteSpace(email))
                return View("TierOverride", (object?)null);

            var user = await _userManager.FindByEmailAsync(email);
            if (user == null)
            {
                TempData["Error"] = $"No user found with email '{email}'.";
                return View("TierOverride", (object?)null);
            }

            var data = await _subscriptionService.GetSubscriptionDataAsync(user.Id);
            var roles = await _userManager.GetRolesAsync(user);

            ViewBag.SearchEmail = email;
            ViewBag.UserId = user.Id;
            ViewBag.CurrentTier = data?.SubscriptionTier ?? SubscriptionTier.Freemium;
            ViewBag.CurrentStatus = data?.SubscriptionStatus ?? SubscriptionStatus.Expired;
            ViewBag.TrialEndDate = data?.TrialEndDate;
            ViewBag.SubscriptionEndDate = data?.SubscriptionEndDate;
            ViewBag.NeverExpires = data?.NeverExpires ?? false;
            ViewBag.Roles = string.Join(", ", roles);
            ViewBag.Tiers = Enum.GetValues<SubscriptionTier>();
            ViewBag.Statuses = Enum.GetValues<SubscriptionStatus>();

            return View("TierOverride", user);
        }

        [HttpPost("/Admin/UserTier/Override")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> OverrideTier(
            string userId,
            SubscriptionTier tier,
            SubscriptionStatus status,
            bool neverExpires,
            string? notes)
        {
            var userData = await _context.UsersData.FirstOrDefaultAsync(u => u.Id == userId);
            if (userData == null)
                return NotFound();

            var oldTier = userData.SubscriptionTier;
            userData.SubscriptionTier = tier;
            userData.NeverExpires = neverExpires;

            if (neverExpires)
            {
                // Permanent access — force Active, clear all expiry dates
                userData.SubscriptionStatus = SubscriptionStatus.Active;
                userData.TrialEndDate = null;
                userData.SubscriptionEndDate = null;
                userData.SubscriptionProvider = "Manual";
            }
            else
            {
                userData.SubscriptionStatus = status;
            }

            _context.SubscriptionEvents.Add(new SubscriptionEvent
            {
                UserId = userId,
                Provider = "Manual",
                EventType = SubscriptionEventTypes.ManualOverride,
                Tier = tier,
                OccurredAt = DateTime.UtcNow,
                RawPayload = neverExpires ? $"[NeverExpires] {notes}" : notes
            });

            await _context.SaveChangesAsync();

            // Sync the identity role to match the new tier
            var user = await _userManager.FindByIdAsync(userId);
            if (user != null)
            {
                var subscriptionRoles = new[] { "Freemium", "Pro", "VIP" };
                foreach (var role in await _userManager.GetRolesAsync(user))
                {
                    if (subscriptionRoles.Contains(role))
                        await _userManager.RemoveFromRoleAsync(user, role);
                }
                var newRole = tier == SubscriptionTier.VIP ? "VIP"
                            : tier == SubscriptionTier.Pro ? "Pro"
                            : "Freemium";
                await _userManager.AddToRoleAsync(user, newRole);
            }

            var suffix = neverExpires ? " (Permanent — never expires)" : string.Empty;
            TempData["Success"] = $"Tier updated from {oldTier} to {tier} ({(neverExpires ? SubscriptionStatus.Active : status)}){suffix} for user {userId}.";
                        return RedirectToAction(nameof(UserTier), new { email = (await _userManager.GetEmailAsync(user!)) });
                    }

                    [HttpGet("/Admin/Plans")]
                    public async Task<IActionResult> Plans()
                    {
                        var plans = await _context.SubscriptionPlans
                            .OrderBy(p => p.Tier)
                            .ThenBy(p => p.IsAnnual)
                            .ToListAsync();
                        return View(plans);
                    }

                    [HttpPost("/Admin/Plans/Edit")]
                    [ValidateAntiForgeryToken]
                    public async Task<IActionResult> EditPlan(
                        int id, string name, decimal monthlyPrice, string currency,
                        bool isActive)
                    {
                        var plan = await _context.SubscriptionPlans.FindAsync(id);
                        if (plan == null)
                            return NotFound();

                        plan.Name         = name.Trim();
                        plan.MonthlyPrice = monthlyPrice;
                        plan.Currency     = currency.Trim().ToUpperInvariant();
                        plan.IsActive     = isActive;

                        await _context.SaveChangesAsync();
                        TempData["Success"] = $"Plan \"{plan.Name}\" updated.";
                        return RedirectToAction(nameof(Plans));
                    }

                    [HttpGet("/Admin/TelegramUserAuth")]
                    public IActionResult TelegramUserAuth()
                    {
                        var scanner = HttpContext.RequestServices.GetService<TelegramUserScannerService>();
                        ViewBag.Status = scanner?.AuthStatus ?? "Not registered";
                        return View();
                    }

                    [HttpPost("/Admin/TelegramUserAuth/Code")]
                    [ValidateAntiForgeryToken]
                    public IActionResult ProvideVerificationCode(string code)
                    {
                        var scanner = HttpContext.RequestServices.GetService<TelegramUserScannerService>();
                        if (scanner == null)
                        {
                            TempData["Error"] = "Telegram user scanner is not running. Check TelegramUserClient config.";
                            return RedirectToAction(nameof(TelegramUserAuth));
                        }
                        scanner.ProvideVerificationCode(code.Trim());
                        TempData["Success"] = "Verification code submitted. The scanner will authenticate shortly.";
                        return RedirectToAction(nameof(TelegramUserAuth));
                    }

                    [HttpPost("/Admin/TelegramUserAuth/Password")]
                    [ValidateAntiForgeryToken]
                    public IActionResult ProvidePassword(string password)
                    {
                        var scanner = HttpContext.RequestServices.GetService<TelegramUserScannerService>();
                        if (scanner == null)
                        {
                            TempData["Error"] = "Telegram user scanner is not running. Check TelegramUserClient config.";
                            return RedirectToAction(nameof(TelegramUserAuth));
                        }
                        scanner.ProvidePassword(password);
                        TempData["Success"] = "2FA password submitted.";
                        return RedirectToAction(nameof(TelegramUserAuth));
                    }

        // ── Subscription Events ───────────────────────────────────────────────

        [HttpGet("/Admin/SubscriptionEvents")]
        public async Task<IActionResult> SubscriptionEvents(string? email, int page = 1)
        {
            const int pageSize = 50;

            string? filterUserId = null;
            if (!string.IsNullOrWhiteSpace(email))
            {
                var user = await _userManager.FindByEmailAsync(email);
                filterUserId = user?.Id;
                ViewBag.FilterEmail = email;
                ViewBag.FilterUserNotFound = user == null;
            }

            var query = _context.SubscriptionEvents.AsNoTracking();
            if (filterUserId != null)
                query = query.Where(e => e.UserId == filterUserId);

            var total = await query.CountAsync();
            var events = await query
                .OrderByDescending(e => e.OccurredAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            // Resolve emails for display
            var userIds = events.Select(e => e.UserId).Distinct().ToList();
            var emailMap = new Dictionary<string, string>();
            foreach (var uid in userIds)
            {
                var u = await _userManager.FindByIdAsync(uid);
                if (u != null)
                    emailMap[uid] = (await _userManager.GetEmailAsync(u)) ?? uid;
            }

            ViewBag.EmailMap = emailMap;
            ViewBag.Page = page;
            ViewBag.PageSize = pageSize;
            ViewBag.Total = total;
            ViewBag.TotalPages = (int)Math.Ceiling(total / (double)pageSize);

            return View(events);
        }

        // ── NOWPayments Admin Diagnostics ────────────────────────────────────

        [HttpGet("/Admin/NOWPayments")]
        public IActionResult NOWPaymentsAdmin(string? paymentId)
        {
            ViewBag.PaymentId = paymentId;
            return View();
        }

        [HttpPost("/Admin/NOWPayments/Lookup")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> NOWPaymentsLookup(string paymentId)
        {
            paymentId = paymentId.Trim();
            var provider = HttpContext.RequestServices.GetRequiredService<NOWPaymentsSubscriptionProvider>();
            var rawJson = await provider.GetPaymentRawAsync(paymentId);

            if (rawJson == null)
            {
                TempData["Error"] = $"NOWPayments API returned an error for payment ID '{paymentId}'. Check application logs.";
                return RedirectToAction(nameof(NOWPaymentsAdmin), new { paymentId });
            }

            // Parse key fields for display
            using var doc = JsonDocument.Parse(rawJson);
            var root = doc.RootElement;

            var status = root.TryGetProperty("payment_status", out var ps) ? ps.GetString() : "unknown";
            var orderId = root.TryGetProperty("order_id", out var oid) ? oid.GetString() : null;
            decimal? amount = root.TryGetProperty("price_amount", out var amt) && amt.ValueKind == JsonValueKind.Number
                ? amt.GetDecimal() : null;
            var currency = root.TryGetProperty("price_currency", out var cur) ? cur.GetString() : null;

            // IsActivated: has a SubscriptionCreated/SubscriptionUpgraded event — mirrors the idempotency check
            var isActivated = await _context.SubscriptionEvents
                .AnyAsync(e => e.ExternalEventId == paymentId
                           && (e.EventType == SubscriptionEventTypes.SubscriptionCreated
                               || e.EventType == SubscriptionEventTypes.SubscriptionUpgraded));

            // HasAnyEvent: any event at all with this payment_id (includes intermediate/failed rows)
            var hasAnyEvent = isActivated || await _context.SubscriptionEvents
                .AnyAsync(e => e.ExternalEventId == paymentId);

            ViewBag.PaymentId = paymentId;
            ViewBag.Status = status;
            ViewBag.OrderId = orderId;
            ViewBag.Amount = amount;
            ViewBag.Currency = currency?.ToUpperInvariant();
            ViewBag.RawJson = rawJson;
            ViewBag.IsActivated = isActivated;
            ViewBag.HasAnyEvent = hasAnyEvent;
            ViewBag.AlreadyProcessed = isActivated; // kept for backward compat

            return View("NOWPaymentsAdmin");
        }

        [HttpPost("/Admin/NOWPayments/Replay")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> NOWPaymentsReplay(string paymentId)
        {
            paymentId = paymentId.Trim();
            var provider = HttpContext.RequestServices.GetRequiredService<NOWPaymentsSubscriptionProvider>();
            var webhookService = HttpContext.RequestServices.GetRequiredService<NOWPaymentsWebhookService>();

            var rawJson = await provider.GetPaymentRawAsync(paymentId);
            if (rawJson == null)
            {
                TempData["Error"] = $"Could not fetch payment '{paymentId}' from NOWPayments API.";
                return RedirectToAction(nameof(NOWPaymentsAdmin), new { paymentId });
            }

            await webhookService.HandleEventAsync(rawJson);
            TempData["Success"] = $"Replay submitted for payment '{paymentId}'. Idempotency check applies — already-processed payments are skipped.";
            return RedirectToAction(nameof(NOWPaymentsAdmin), new { paymentId });
        }

        [HttpPost("/Admin/NOWPayments/ForceActivate")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> NOWPaymentsForceActivate(string paymentId, string orderId)
        {
            paymentId = paymentId.Trim();
            orderId = orderId.Trim();

            // Parse order_id → userId:planId[:nonce]
            // Use first-colon split so the nonce segment is ignored.
            var firstColon = orderId.IndexOf(':');
            if (firstColon <= 0)
            {
                TempData["Error"] = $"Cannot parse order_id '{orderId}': no colon separator found.";
                return RedirectToAction(nameof(NOWPaymentsAdmin), new { paymentId });
            }
            var userId = orderId[..firstColon];
            var remainder = orderId[(firstColon + 1)..];
            var numericPlanPart = new string(remainder.TakeWhile(char.IsDigit).ToArray());
            if (!int.TryParse(numericPlanPart, out var planId) || string.IsNullOrWhiteSpace(userId))
            {
                TempData["Error"] = $"Cannot parse order_id '{orderId}'. Expected format: {{userId}}:{{planId}} or {{userId}}:{{planId}}:{{nonce}}.";
                return RedirectToAction(nameof(NOWPaymentsAdmin), new { paymentId });
            }

            var plan = await _context.SubscriptionPlans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == planId);
            if (plan == null)
            {
                TempData["Error"] = $"SubscriptionPlan ID {planId} not found.";
                return RedirectToAction(nameof(NOWPaymentsAdmin), new { paymentId });
            }

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                TempData["Error"] = $"User ID '{userId}' not found.";
                return RedirectToAction(nameof(NOWPaymentsAdmin), new { paymentId });
            }

            var now = DateTime.UtcNow;
            var end = plan.IsAnnual ? now.AddYears(1) : now.AddMonths(1);

            await _subscriptionService.ActivateSubscriptionAsync(userId, plan.Tier, "NOWPayments", paymentId, now, end);

            // Write audit event with _admin_forced suffix to avoid idempotency collision on future replays
            _context.SubscriptionEvents.Add(new SubscriptionEvent
            {
                UserId = userId,
                Provider = "NOWPayments",
                EventType = SubscriptionEventTypes.SubscriptionCreated,
                Tier = plan.Tier,
                ExternalEventId = paymentId + "_admin_forced",
                ExternalSubscriptionId = orderId,
                OccurredAt = now,
                RawPayload = $"[Admin force-activated] paymentId={paymentId} planId={planId}"
            });
            await _context.SaveChangesAsync();

            var userEmail = await _userManager.GetEmailAsync(user) ?? userId;
            TempData["Success"] = $"Force-activated {plan.Tier} for {userEmail} until {end:yyyy-MM-dd}.";
            return RedirectToAction(nameof(NOWPaymentsAdmin), new { paymentId });
        }

        // ── Payment Diagnostics ───────────────────────────────────────────────

        [HttpGet("/Admin/PaymentDiagnostics")]
        public async Task<IActionResult> PaymentDiagnostics(
            string? email, string? status,
            DateTime? from, DateTime? to)
        {
            var query = _context.SubscriptionEvents
                .AsNoTracking()
                .Where(e => e.Provider == "NOWPayments");

            if (from.HasValue)  query = query.Where(e => e.OccurredAt >= from.Value);
            if (to.HasValue)    query = query.Where(e => e.OccurredAt <= to.Value.AddDays(1));

            string? filterUserId = null;
            if (!string.IsNullOrWhiteSpace(email))
            {
                var filterUser = await _userManager.FindByEmailAsync(email);
                filterUserId = filterUser?.Id;
                ViewBag.FilterUserNotFound = filterUser == null;
            }
            if (filterUserId != null) query = query.Where(e => e.UserId == filterUserId);

            var allEvents = await query
                .OrderByDescending(e => e.OccurredAt)
                .ToListAsync();

            // Resolve user emails
            var userIds = allEvents.Select(e => e.UserId).Distinct().ToList();
            var emailMap = new Dictionary<string, string>();
            foreach (var uid in userIds)
            {
                var u = await _userManager.FindByIdAsync(uid);
                if (u != null) emailMap[uid] = await _userManager.GetEmailAsync(u) ?? uid;
            }

            // Group by payment_id (ExternalEventId) — fall back to ExternalSubscriptionId for
            // rows (e.g. PaymentPartial) that have no ExternalEventId.
            var groups = allEvents
                .GroupBy(e => e.ExternalEventId ?? $"(no-id):{e.ExternalSubscriptionId}:{e.UserId}")
                .Select(g =>
                {
                    bool activated = g.Any(e =>
                        e.EventType == SubscriptionEventTypes.SubscriptionCreated ||
                        e.EventType == SubscriptionEventTypes.SubscriptionUpgraded);
                    bool failed = g.Any(e => e.EventType == SubscriptionEventTypes.PaymentFailed);
                    return new PaymentDiagnosticsGroup
                    {
                        PaymentId  = g.First().ExternalEventId,
                        OrderId    = g.Select(e => e.ExternalSubscriptionId).FirstOrDefault(x => x != null),
                        UserId     = g.First().UserId,
                        UserEmail  = emailMap.GetValueOrDefault(g.First().UserId, g.First().UserId),
                        Events     = g.OrderBy(e => e.OccurredAt).ToList(),
                        IsActivated = activated,
                        HasFailure  = failed
                    };
                })
                .ToList();

            // Apply status filter after grouping
            if (status == "activated")  groups = groups.Where(g => g.IsActivated).ToList();
            if (status == "pending")    groups = groups.Where(g => !g.IsActivated && !g.HasFailure).ToList();
            if (status == "failed")     groups = groups.Where(g => g.HasFailure).ToList();

            groups = groups.OrderByDescending(g => g.Events.Max(e => e.OccurredAt)).ToList();

            ViewBag.FilterEmail  = email;
            ViewBag.FilterStatus = status;
            ViewBag.FilterFrom   = from?.ToString("yyyy-MM-dd");
            ViewBag.FilterTo     = to?.ToString("yyyy-MM-dd");

            return View(groups);
        }

        [HttpPost("/Admin/ForceReactivate")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ForceReactivate(string paymentId, string orderId)
        {
            paymentId = paymentId.Trim();
            orderId   = orderId.Trim();

            // Clear the idempotency block so HandleEventAsync will process this payment again.
            var blockingEvents = await _context.SubscriptionEvents
                .Where(e => e.ExternalEventId == paymentId
                         && (e.EventType == SubscriptionEventTypes.SubscriptionCreated
                          || e.EventType == SubscriptionEventTypes.SubscriptionUpgraded))
                .ToListAsync();

            if (blockingEvents.Count > 0)
            {
                _context.SubscriptionEvents.RemoveRange(blockingEvents);
                await _context.SaveChangesAsync();
            }

            // Fetch the raw payment JSON and replay it through the normal webhook handler.
            var provider       = HttpContext.RequestServices.GetRequiredService<NOWPaymentsSubscriptionProvider>();
            var webhookService = HttpContext.RequestServices.GetRequiredService<NOWPaymentsWebhookService>();

            var rawJson = await provider.GetPaymentRawAsync(paymentId);
            if (rawJson == null)
            {
                TempData["Error"] = $"Could not fetch payment '{paymentId}' from NOWPayments API.";
                return RedirectToAction(nameof(PaymentDiagnostics));
            }

            await webhookService.HandleEventAsync(rawJson);
            TempData["Success"] = $"Force re-activate submitted for payment '{paymentId}'."; 
            return RedirectToAction(nameof(PaymentDiagnostics));
        }
                }
            }
