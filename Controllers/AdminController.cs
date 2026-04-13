using AutoSignals.Data;
using AutoSignals.Models;
using AutoSignals.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

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
            string? notes)
        {
            var userData = await _context.UsersData.FirstOrDefaultAsync(u => u.Id == userId);
            if (userData == null)
                return NotFound();

            var oldTier = userData.SubscriptionTier;
            userData.SubscriptionTier = tier;
            userData.SubscriptionStatus = status;

            _context.SubscriptionEvents.Add(new SubscriptionEvent
            {
                UserId = userId,
                Provider = "Manual",
                EventType = SubscriptionEventTypes.ManualOverride,
                Tier = tier,
                OccurredAt = DateTime.UtcNow,
                RawPayload = notes
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

                    TempData["Success"] = $"Tier updated from {oldTier} to {tier} ({status}) for user {userId}.";
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
                        bool isActive, string? lemonSqueezyVariantId)
                    {
                        var plan = await _context.SubscriptionPlans.FindAsync(id);
                        if (plan == null)
                            return NotFound();

                        plan.Name                  = name.Trim();
                        plan.MonthlyPrice          = monthlyPrice;
                        plan.Currency              = currency.Trim().ToUpperInvariant();
                        plan.IsActive              = isActive;
                        plan.LemonSqueezyVariantId = string.IsNullOrWhiteSpace(lemonSqueezyVariantId)
                                                        ? null : lemonSqueezyVariantId.Trim();

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
                }
            }
