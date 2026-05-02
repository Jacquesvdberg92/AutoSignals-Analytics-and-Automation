using AutoSignals.Data;
using AutoSignals.Models;
using AutoSignals.Services;
using AutoSignals.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace AutoSignals.Controllers
{
    [Authorize]
    public class SettingsController : Controller
    {
        private readonly AutoSignalsDbContext _context;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly ErrorLogService _errorLogService;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly AesEncryptionService _encryptionService;
        private readonly ExchangeBalanceService _exchangeBalanceService;

        public SettingsController(
        AutoSignalsDbContext context,
        UserManager<IdentityUser> userManager,
        RoleManager<IdentityRole> roleManager,
        ErrorLogService errorLogService,
        IServiceScopeFactory scopeFactory,
        AesEncryptionService encryptionService,
        ExchangeBalanceService exchangeBalanceService
)
        {
            _context = context;
            _userManager = userManager;
            _roleManager = roleManager;
            _errorLogService = errorLogService;
            _scopeFactory = scopeFactory;
            _encryptionService = encryptionService;
            _exchangeBalanceService = exchangeBalanceService;
        }

        [Route("/settings")]
        public async Task<IActionResult> Settings(string? userId)
        {
            // If no userId is provided, default to the current user's ID
            userId ??= _userManager.GetUserId(User);

            // Check if the current user is allowed to access the requested user's settings
            if (userId != _userManager.GetUserId(User) && !User.IsInRole("Admin"))
            {
                return Forbid(); // Prevent unauthorized access
            }

            var user = await _userManager.FindByIdAsync(userId);
            var userData = await _context.UsersData.FirstOrDefaultAsync(u => u.Id == userId) ?? new UserData();
            var roles = await _userManager.GetRolesAsync(user);
            var openPositionCount = await _context.Positions.CountAsync(p => p.UserId == userId && p.Status == "OPEN");
            var positionCount = await _context.Positions.CountAsync(p => p.UserId == userId);

            var providerSettings = await _context.ProvidersSettings.Where(ps => ps.UserId == userId).ToListAsync();
            if (providerSettings == null)
            {
                providerSettings = new List<ProviderSettings>();
            }

            var availableExchanges = await _context.Exchanges
                .Where(e => e.IsEnabled)
                .Select(e => new SelectListItem { Value = e.Id.ToString(), Text = e.Name })
                .ToListAsync();

            var userConnections = await _context.UserExchangeConnections
                .Include(c => c.Exchange)
                .Where(c => c.UserId == userId)
                .OrderByDescending(c => c.IsDefault)
                .ThenBy(c => c.CreatedAt)
                .ToListAsync();

            var connectionLimit = (roles.Contains("VIP") || roles.Contains("Tester") || roles.Contains("Admin")) ? 5 :
                                  (roles.Contains("Pro") || roles.Contains("Subscriber")) ? 1 : 0;
            ViewBag.ConnectionLimit = connectionLimit;

            var userProfile = new UserProfileViewModel
            {
                User = user,
                UserData = userData,
                Roles = roles,
                OpenPositionCount = openPositionCount,
                PositionCount = positionCount,
                ProviderSettings = providerSettings,
                Positions = await _context.Positions.Where(p => p.UserId == userId).ToListAsync(),
                AvailableExchanges = availableExchanges,
                UserConnections = userConnections,
                NotificationSettings = await _context.UserNotificationSettings
                    .FirstOrDefaultAsync(s => s.UserId == userId)
                    ?? new UserNotificationSettings { UserId = userId }
            };

            return View(userProfile);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateUserDetails(UserProfileViewModel model)
        {
            IdentityUser? user = null;

            try
            {
                var currentUserId = _userManager.GetUserId(User);
                var requestedUserId = model.User?.Id;
                var requestedUserDataId = model.UserData?.Id;

                if (string.IsNullOrWhiteSpace(currentUserId) ||
                    string.IsNullOrWhiteSpace(requestedUserId) ||
                    string.IsNullOrWhiteSpace(requestedUserDataId))
                {
                    return Unauthorized();
                }

                if ((requestedUserId != currentUserId || requestedUserDataId != currentUserId) && !User.IsInRole("Admin"))
                {
                    return Forbid();
                }

                // Free users cannot add or update exchange API connections
                var canConnect = User.IsInRole("Pro") || User.IsInRole("VIP") || User.IsInRole("Tester") ||
                                 User.IsInRole("Admin") || User.IsInRole("Subscriber");
                if (!canConnect)
                {
                    TempData["ErrorMessage"] = "Exchange API connections require a Pro subscription.";
                    return RedirectToAction("Settings");
                }

                user = await _userManager.FindByIdAsync(model.User.Id);
                var userData = await _context.UsersData.FirstOrDefaultAsync(u => u.Id == model.UserData.Id);

                if (user == null)
                {
                    ModelState.AddModelError(string.Empty, "User not found.");
                    await PopulateSettingsViewModelAsync(model);
                    return View("Settings", model);
                }

                if (userData == null)
                {
                    ModelState.AddModelError(string.Empty, "User settings were not found for this account.");
                    await PopulateSettingsViewModelAsync(model, user);
                    return View("Settings", model);
                }

                var apiKey = GetEffectiveCredential(model.ApiKeyInput, userData.ApiKey);
                var apiSecret = GetEffectiveCredential(model.ApiSecretInput, userData.ApiSecret);
                var apiPassword = GetEffectiveCredential(model.ApiPasswordInput, userData.ApiPassword);

                decimal balance = await _exchangeBalanceService.GetExchangeBalanceAsync(
                    model.UserData.ExchangeId,
                    apiKey,
                    apiSecret,
                    apiPassword);

                if (balance > 0)
                {
                    userData.ApiTestResult = "1";
                }
                else
                {
                    userData.ApiTestResult = "0";
                }

                // Encrypt API credentials before saving
                userData.ExchangeId = model.UserData.ExchangeId;
                if (!string.IsNullOrWhiteSpace(model.ApiKeyInput))
                {
                    userData.ApiKey = _encryptionService.Encrypt(model.ApiKeyInput);
                }

                if (!string.IsNullOrWhiteSpace(model.ApiSecretInput))
                {
                    userData.ApiSecret = _encryptionService.Encrypt(model.ApiSecretInput);
                }

                if (!string.IsNullOrWhiteSpace(model.ApiPasswordInput))
                {
                    userData.ApiPassword = _encryptionService.Encrypt(model.ApiPasswordInput);
                }

                // Save changes
                var result = await _userManager.UpdateAsync(user);
                if (result.Succeeded)
                {
                    _context.UsersData.Update(userData);

                    // Keep UserExchangeConnections in sync for connection-aware order routing
                    if (userData.ExchangeId.HasValue && !string.IsNullOrEmpty(userData.ApiKey))
                    {
                        var existingConn = await _context.UserExchangeConnections
                            .FirstOrDefaultAsync(c => c.UserId == userData.Id && c.IsDefault);
                        if (existingConn == null)
                        {
                            existingConn = await _context.UserExchangeConnections
                                .FirstOrDefaultAsync(c => c.UserId == userData.Id);
                        }

                        if (existingConn == null)
                        {
                            _context.UserExchangeConnections.Add(new UserExchangeConnection
                            {
                                UserId = userData.Id,
                                ExchangeId = userData.ExchangeId.Value,
                                Label = "Primary Connection",
                                ApiKey = userData.ApiKey,
                                ApiSecret = userData.ApiSecret,
                                ApiPassword = userData.ApiPassword,
                                IsDefault = true,
                                IsActive = true,
                                TestResult = userData.ApiTestResult,
                                LastTestedAt = DateTime.UtcNow,
                                CreatedAt = DateTime.UtcNow,
                                UpdatedAt = DateTime.UtcNow
                            });
                        }
                        else
                        {
                            existingConn.ExchangeId = userData.ExchangeId.Value;
                            existingConn.ApiKey = userData.ApiKey;
                            existingConn.ApiSecret = userData.ApiSecret;
                            existingConn.ApiPassword = userData.ApiPassword;
                            existingConn.TestResult = userData.ApiTestResult;
                            existingConn.LastTestedAt = DateTime.UtcNow;
                            existingConn.UpdatedAt = DateTime.UtcNow;
                        }
                    }

                    await _context.SaveChangesAsync();
                    return RedirectToAction("Settings");
                }
                else
                {
                    foreach (var error in result.Errors)
                    {
                        ModelState.AddModelError(string.Empty, error.Description);
                    }
                }
            }
            catch (Exception ex)
            {
                ModelState.AddModelError(string.Empty, ex.Message);
                await PopulateSettingsViewModelAsync(model);
                // If we got this far, something failed, redisplay form
                return View("Settings", model);
            }

            await PopulateSettingsViewModelAsync(model, user);
            // If we got this far, something failed, redisplay form
            return View("Settings", model);
        }

        private string GetEffectiveCredential(string? submittedValue, string? encryptedStoredValue)
        {
            if (!string.IsNullOrWhiteSpace(submittedValue))
            {
                return submittedValue;
            }

            if (string.IsNullOrWhiteSpace(encryptedStoredValue))
            {
                return string.Empty;
            }

            return _encryptionService.Decrypt(encryptedStoredValue);
        }

        private async Task PopulateSettingsViewModelAsync(UserProfileViewModel model, IdentityUser? user = null)
        {
            model.User ??= user ?? await _userManager.FindByIdAsync(model.UserData?.Id ?? string.Empty);
            model.ApiKeyInput = null;
            model.ApiSecretInput = null;
            model.ApiPasswordInput = null;
            model.AvailableExchanges = await _context.Exchanges
                .Where(e => e.IsEnabled)
                .Select(e => new SelectListItem { Value = e.Id.ToString(), Text = e.Name })
                .ToListAsync();
        }



        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateProviderSettings(UserProfileViewModel model)
        {
            try
            {
                var userId = _userManager.GetUserId(User);
                if (string.IsNullOrWhiteSpace(userId))
                {
                    return Unauthorized();
                }

                var providerSettings = await _context.ProvidersSettings.Where(ps => ps.UserId == userId).ToListAsync();

                if (providerSettings != null)
                {
                    foreach (var setting in model.ProviderSettings)
                    {
                        var existingSetting = providerSettings.FirstOrDefault(ps => ps.Id == setting.Id);
                        if (existingSetting != null)
                        {
                            existingSetting.IsEnabled = setting.IsEnabled;
                            existingSetting.Testing = setting.Testing;
                            existingSetting.OverideLeverage = setting.OverideLeverage;
                            existingSetting.Leverage = setting.Leverage;
                            existingSetting.UseStoploss = setting.UseStoploss;
                            existingSetting.IgnorLong = setting.IgnorLong;
                            existingSetting.IgnorShort = setting.IgnorShort;
                            existingSetting.IgnoreStoploss = setting.IgnoreStoploss;
                            existingSetting.StoplossPercentage = setting.StoplossPercentage;
                            existingSetting.MoveStoploss = setting.MoveStoploss;
                            existingSetting.MoveStoplossOn = setting.MoveStoplossOn;
                            existingSetting.RiskPercentage = setting.RiskPercentage;
                            existingSetting.MinTradeSizeUsd = setting.MinTradeSizeUsd;
                            existingSetting.MaxTradeSizeUsd = setting.MaxTradeSizeUsd;
                            existingSetting.IsIsolated = setting.IsIsolated;
                            existingSetting.UseMoonbag = setting.UseMoonbag;
                            existingSetting.MoonbagPercentage = setting.MoonbagPercentage;
                            existingSetting.MoonbagSize = setting.MoonbagSize;
                            existingSetting.TpPercentages = setting.TpPercentages; 
                        }
                    }

                    _context.ProvidersSettings.UpdateRange(providerSettings);
                    await _context.SaveChangesAsync();
                    return RedirectToAction("Settings");
                }
            }
            catch (Exception ex)
            {
                ModelState.AddModelError(string.Empty, ex.Message);
                // If we got this far, something failed, redisplay form
                return View("Settings", model);
            }

            // If we got this far, something failed, redisplay form
            return View("Settings", model);
        }

        // Add these methods to your SettingsController class

        [HttpGet]
        public async Task<IActionResult> GetProviderSettings(string providerId)
        {
            if (string.IsNullOrWhiteSpace(providerId))
            {
                return BadRequest("providerId is required.");
            }

            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Unauthorized();
            }

            var providerSetting = await _context.ProvidersSettings
                .FirstOrDefaultAsync(ps => ps.UserId == userId && ps.ProviderId == providerId);

            if (providerSetting == null)
            {
                return NotFound($"Provider settings not found for provider '{providerId}'.");
            }

            // Ensure TpPercentages has at least as many entries as TpCount so the
            // modal renders the correct number of TP inputs.
            while (providerSetting.TpPercentages.Count < providerSetting.TpCount)
            {
                providerSetting.TpPercentages.Add(25);
            }
            if (providerSetting.TpCount > 0 && providerSetting.TpPercentages[providerSetting.TpCount - 1] == 0)
            {
                providerSetting.TpPercentages[providerSetting.TpCount - 1] = 100;
            }

            var userConns = await _context.UserExchangeConnections
                .Include(c => c.Exchange)
                .Where(c => c.UserId == userId && c.IsActive)
                .OrderByDescending(c => c.IsDefault)
                .ThenBy(c => c.CreatedAt)
                .ToListAsync();
            ViewBag.UserConnections = userConns;

            return PartialView("_ProviderSettingsModal", providerSetting);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveProviderSettings([FromBody] ProviderSettings model)
        {
            try
            {
                var userId = _userManager.GetUserId(User);
                var existingSetting = await _context.ProvidersSettings
                    .FirstOrDefaultAsync(ps => ps.UserId == userId && ps.ProviderId == model.ProviderId);

                if (existingSetting == null)
                {
                    // Ensure UserId is set
                    model.UserId = userId;
                    model.Time = DateTime.UtcNow;
                    _context.ProvidersSettings.Add(model);
                }
                else
                {
                    // Update existing - ensure proper data type conversion
                    existingSetting.IsEnabled = model.IsEnabled;
                    existingSetting.Testing = model.Testing;
                    existingSetting.OverideLeverage = model.OverideLeverage;
                    existingSetting.Leverage = model.Leverage;
                    existingSetting.UseStoploss = model.UseStoploss;
                    existingSetting.IgnorLong = model.IgnorLong;
                    existingSetting.IgnorShort = model.IgnorShort;
                    existingSetting.IgnoreStoploss = model.IgnoreStoploss;
                    existingSetting.StoplossPercentage = model.StoplossPercentage;
                    existingSetting.MoveStoploss = model.MoveStoploss;
                    existingSetting.MoveStoplossOn = model.MoveStoplossOn;
                    existingSetting.RiskPercentage = model.RiskPercentage;
                    existingSetting.MinTradeSizeUsd = model.MinTradeSizeUsd;
                    existingSetting.MaxTradeSizeUsd = model.MaxTradeSizeUsd;
                    existingSetting.IsIsolated = model.IsIsolated;
                    existingSetting.UseMoonbag = model.UseMoonbag;
                    existingSetting.MoonbagPercentage = model.MoonbagPercentage;
                    existingSetting.MoonbagSize = model.MoonbagSize;
                    existingSetting.TpPercentages = model.TpPercentages ?? new List<double>();
                    existingSetting.ConnectionId = model.ConnectionId;
                    existingSetting.Time = DateTime.UtcNow; // Update timestamp
                }

                try
                {
                    await _context.SaveChangesAsync();
                }catch (Exception e)
                {
                    return Json(new { success = false, message = e.Message });
                    
                }
                
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkUpdateProviderSettings([FromBody] BulkProviderSettingsUpdateViewModel model)
        {
            try
            {
                var userId = _userManager.GetUserId(User);
                if (string.IsNullOrWhiteSpace(userId))
                {
                    return Unauthorized();
                }

                if (model?.ProviderId == null || model.ProviderId.Count == 0)
                {
                    return Json(new { success = false, message = "No providers selected" });
                }

                var providerIds = new HashSet<int>(model.ProviderId);

                var providerSettings = await _context.ProvidersSettings
                    .Where(ps => ps.UserId == userId)
                    .ToListAsync();

                foreach (var setting in providerSettings)
                {
                    if (!int.TryParse(setting.ProviderId, out var providerId) || !providerIds.Contains(providerId))
                    {
                        continue;
                    }

                    setting.IsEnabled = model.IsEnabled;
                    setting.Testing = model.Testing;

                    setting.OverideLeverage = model.OverideLeverage;
                    setting.Leverage = model.Leverage;

                    setting.IgnorLong = model.IgnorLong;
                    setting.IgnorShort = model.IgnorShort;

                    setting.IgnoreStoploss = model.IgnoreStoploss;
                    setting.UseStoploss = model.UseStoploss;
                    setting.StoplossPercentage = model.StoplossPercentage;
                    setting.MoveStoploss = model.MoveStoploss;
                    setting.MoveStoplossOn = model.MoveStoplossOn;

                    setting.RiskPercentage = model.RiskPercentage;
                    setting.MinTradeSizeUsd = model.MinTradeSizeUsd;
                    setting.MaxTradeSizeUsd = model.MaxTradeSizeUsd;

                    setting.IsIsolated = model.IsIsolated;

                    setting.UseMoonbag = model.UseMoonbag;
                    setting.MoonbagPercentage = model.MoonbagPercentage;
                    setting.MoonbagSize = model.MoonbagSize;

                    if (model.TpPercentages != null && model.TpPercentages.Count > 0)
                    {
                        setting.TpPercentages = model.TpPercentages;
                    }

                    setting.Time = DateTime.UtcNow;
                }

                await _context.SaveChangesAsync();
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Bulk update failed", detail = ex.Message });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CopyProviderSettings([FromBody] CopySettingsRequest request)
        {
            try
            {
                var userId = _userManager.GetUserId(User);

                // Get source provider settings
                var sourceSettings = await _context.ProvidersSettings
                    .FirstOrDefaultAsync(ps => ps.UserId == userId && ps.ProviderId == request.SourceProviderId.ToString());

                if (sourceSettings == null)
                {
                    return Json(new { success = false, message = "Source provider not found" });
                }

                // Update target providers
                foreach (var targetId in request.TargetProviderIds)
                {
                    var targetSettings = await _context.ProvidersSettings
                        .FirstOrDefaultAsync(ps => ps.UserId == userId && ps.ProviderId == targetId.ToString());

                    if (targetSettings == null)
                    {
                        targetSettings = new ProviderSettings
                        {
                            UserId = userId,
                            ProviderId = targetId.ToString()
                        };
                        _context.ProvidersSettings.Add(targetSettings);
                    }

                    // Copy properties based on what should be copied
                    targetSettings.IsEnabled = sourceSettings.IsEnabled;
                    targetSettings.Testing = sourceSettings.Testing;
                    // ... copy other properties as needed
                }

                await _context.SaveChangesAsync();
                return Json(new { success = true, message = $"Settings copied to {request.TargetProviderIds.Count} providers" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // Helper class for copy request
        public class CopySettingsRequest
        {
            public int SourceProviderId { get; set; }
            public List<int> TargetProviderIds { get; set; } = new List<int>();
            public bool CopyAllSettings { get; set; } = true;
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveNotificationSettings(UserNotificationSettings model)
        {
            try
            {
                var currentUserId = _userManager.GetUserId(User);

                if (string.IsNullOrWhiteSpace(currentUserId) || string.IsNullOrWhiteSpace(model.UserId))
                    return Unauthorized();

                if (model.UserId != currentUserId && !User.IsInRole("Admin"))
                            return Forbid();

                        // Freemium users cannot enable trade notifications — strip before saving
                        var isPro = User.IsInRole("Pro") || User.IsInRole("VIP") || User.IsInRole("Tester") ||
                                    User.IsInRole("Admin") || User.IsInRole("Subscriber");
                        if (!isPro)
                        {
                            model.TelegramOrderExecuted = false;
                            model.TelegramTakeProfitHit = false;
                            model.TelegramStopLossHit   = false;
                            model.EmailOrderExecuted    = false;
                            model.EmailTakeProfitHit    = false;
                            model.EmailStopLossHit      = false;
                        }

                        var existing = await _context.UserNotificationSettings
                    .FirstOrDefaultAsync(s => s.UserId == model.UserId);

                if (existing == null)
                {
                    model.UpdatedAt = DateTime.UtcNow;
                    _context.UserNotificationSettings.Add(model);
                }
                else
                {
                    existing.TelegramOrderExecuted  = model.TelegramOrderExecuted;
                    existing.TelegramTakeProfitHit  = model.TelegramTakeProfitHit;
                    existing.TelegramStopLossHit    = model.TelegramStopLossHit;
                    existing.EmailOrderExecuted     = model.EmailOrderExecuted;
                    existing.EmailTakeProfitHit     = model.EmailTakeProfitHit;
                    existing.EmailStopLossHit       = model.EmailStopLossHit;
                    existing.EmailMarketing         = model.EmailMarketing;
                    existing.EmailUpdates           = model.EmailUpdates;
                    existing.UpdatedAt              = DateTime.UtcNow;
                }

                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                await _errorLogService.LogErrorAsync(
                    "Failed to save notification settings",
                    ex.StackTrace, "SettingsController.SaveNotificationSettings", ex.Message);
            }

            return RedirectToAction("Settings");
        }

        // ===== Exchange Connection Management =====

        private int GetConnectionLimit()
        {
            if (User.IsInRole("VIP") || User.IsInRole("Tester") || User.IsInRole("Admin"))
                return 5;
            if (User.IsInRole("Pro") || User.IsInRole("Subscriber"))
                return 1;
            return 0;
        }

        [HttpGet]
        public async Task<IActionResult> GetAddConnectionModal()
        {
            if (GetConnectionLimit() == 0) return Forbid();

            var model = new ConnectionFormViewModel
            {
                AvailableExchanges = await _context.Exchanges
                    .Where(e => e.IsEnabled)
                    .Select(e => new SelectListItem { Value = e.Id.ToString(), Text = e.Name })
                    .ToListAsync()
            };
            return PartialView("_AddConnection", model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddConnection([FromBody] ConnectionFormViewModel model)
        {
            var limit = GetConnectionLimit();
            if (limit == 0) return Json(new { success = false, message = "Exchange connections require a Pro subscription." });

            var userId = _userManager.GetUserId(User)!;
            var count = await _context.UserExchangeConnections.CountAsync(c => c.UserId == userId);
            if (count >= limit)
                return Json(new { success = false, message = $"Your plan allows {limit} exchange connection(s). Upgrade to add more." });

            if (string.IsNullOrWhiteSpace(model.ApiKeyInput))
                return Json(new { success = false, message = "API Key is required." });

            var isFirst = count == 0;
            var connection = new UserExchangeConnection
            {
                UserId = userId,
                ExchangeId = model.ExchangeId,
                Label = model.Label,
                ApiKey = _encryptionService.Encrypt(model.ApiKeyInput),
                ApiSecret = !string.IsNullOrWhiteSpace(model.ApiSecretInput) ? _encryptionService.Encrypt(model.ApiSecretInput) : null,
                ApiPassword = !string.IsNullOrWhiteSpace(model.ApiPasswordInput) ? _encryptionService.Encrypt(model.ApiPasswordInput) : null,
                IsDefault = isFirst || model.IsDefault,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            if (connection.IsDefault)
            {
                var existing = await _context.UserExchangeConnections
                    .Where(c => c.UserId == userId && c.IsDefault)
                    .ToListAsync();
                existing.ForEach(c => c.IsDefault = false);
            }

            _context.UserExchangeConnections.Add(connection);

            // Keep UserData in sync for backward compat
            if (connection.IsDefault)
            {
                var userData = await _context.UsersData.FirstOrDefaultAsync(u => u.Id == userId);
                if (userData != null)
                {
                    userData.ExchangeId = model.ExchangeId;
                    userData.ApiKey = connection.ApiKey;
                    userData.ApiSecret = connection.ApiSecret;
                    userData.ApiPassword = connection.ApiPassword;
                    _context.UsersData.Update(userData);
                }
            }

            await _context.SaveChangesAsync();
            return Json(new { success = true });
        }

        [HttpGet]
        public async Task<IActionResult> GetEditConnectionModal(int id)
        {
            var userId = _userManager.GetUserId(User)!;
            var connection = await _context.UserExchangeConnections
                .Include(c => c.Exchange)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (connection == null) return NotFound();
            if (connection.UserId != userId && !User.IsInRole("Admin")) return Forbid();

            var model = new ConnectionFormViewModel
            {
                Id = connection.Id,
                ExchangeId = connection.ExchangeId,
                Label = connection.Label,
                IsDefault = connection.IsDefault,
                IsActive = connection.IsActive,
                HasExistingCredentials = !string.IsNullOrEmpty(connection.ApiKey),
                AvailableExchanges = await _context.Exchanges
                    .Where(e => e.IsEnabled)
                    .Select(e => new SelectListItem { Value = e.Id.ToString(), Text = e.Name })
                    .ToListAsync()
            };
            return PartialView("_EditConnection", model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditConnection(int id, [FromBody] ConnectionFormViewModel model)
        {
            var userId = _userManager.GetUserId(User)!;
            var connection = await _context.UserExchangeConnections
                .FirstOrDefaultAsync(c => c.Id == id);

            if (connection == null) return Json(new { success = false, message = "Connection not found." });
            if (connection.UserId != userId && !User.IsInRole("Admin")) return Json(new { success = false, message = "Forbidden." });

            connection.ExchangeId = model.ExchangeId;
            connection.Label = model.Label;
            connection.IsActive = model.IsActive;
            connection.UpdatedAt = DateTime.UtcNow;

            if (!string.IsNullOrWhiteSpace(model.ApiKeyInput))
                connection.ApiKey = _encryptionService.Encrypt(model.ApiKeyInput);
            if (!string.IsNullOrWhiteSpace(model.ApiSecretInput))
                connection.ApiSecret = _encryptionService.Encrypt(model.ApiSecretInput);
            if (!string.IsNullOrWhiteSpace(model.ApiPasswordInput))
                connection.ApiPassword = _encryptionService.Encrypt(model.ApiPasswordInput);

            if (model.IsDefault && !connection.IsDefault)
            {
                var others = await _context.UserExchangeConnections
                    .Where(c => c.UserId == userId && c.IsDefault && c.Id != id)
                    .ToListAsync();
                others.ForEach(c => c.IsDefault = false);
                connection.IsDefault = true;
            }

            _context.UserExchangeConnections.Update(connection);

            if (connection.IsDefault)
            {
                var userData = await _context.UsersData.FirstOrDefaultAsync(u => u.Id == userId);
                if (userData != null)
                {
                    userData.ExchangeId = connection.ExchangeId;
                    if (!string.IsNullOrWhiteSpace(model.ApiKeyInput)) userData.ApiKey = connection.ApiKey;
                    if (!string.IsNullOrWhiteSpace(model.ApiSecretInput)) userData.ApiSecret = connection.ApiSecret;
                    if (!string.IsNullOrWhiteSpace(model.ApiPasswordInput)) userData.ApiPassword = connection.ApiPassword;
                    _context.UsersData.Update(userData);
                }
            }

            await _context.SaveChangesAsync();
            return Json(new { success = true });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConnection(int id)
        {
            var userId = _userManager.GetUserId(User)!;
            var connection = await _context.UserExchangeConnections
                .FirstOrDefaultAsync(c => c.Id == id);

            if (connection == null) return Json(new { success = false, message = "Connection not found." });
            if (connection.UserId != userId && !User.IsInRole("Admin")) return Json(new { success = false, message = "Forbidden." });

            var wasDefault = connection.IsDefault;
            _context.UserExchangeConnections.Remove(connection);
            await _context.SaveChangesAsync();

            if (wasDefault)
            {
                var next = await _context.UserExchangeConnections
                    .Where(c => c.UserId == userId && c.IsActive)
                    .OrderBy(c => c.CreatedAt)
                    .FirstOrDefaultAsync();
                if (next != null)
                {
                    next.IsDefault = true;
                    next.UpdatedAt = DateTime.UtcNow;
                    var userData = await _context.UsersData.FirstOrDefaultAsync(u => u.Id == userId);
                    if (userData != null)
                    {
                        userData.ExchangeId = next.ExchangeId;
                        userData.ApiKey = next.ApiKey;
                        userData.ApiSecret = next.ApiSecret;
                        userData.ApiPassword = next.ApiPassword;
                        _context.UsersData.Update(userData);
                    }
                    await _context.SaveChangesAsync();
                }
            }

            return Json(new { success = true });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> TestConnection(int id)
        {
            var userId = _userManager.GetUserId(User)!;
            var connection = await _context.UserExchangeConnections
                .FirstOrDefaultAsync(c => c.Id == id);

            if (connection == null) return Json(new { success = false, message = "Connection not found." });
            if (connection.UserId != userId && !User.IsInRole("Admin")) return Json(new { success = false, message = "Forbidden." });

            var balance = await _exchangeBalanceService.GetConnectionBalanceAsync(connection);
            connection.TestResult = balance > 0 ? "1" : "0";
            connection.LastTestedAt = DateTime.UtcNow;
            connection.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return Json(new { success = true, balance, testResult = connection.TestResult });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SetDefaultConnection(int id)
        {
            var userId = _userManager.GetUserId(User)!;
            var connection = await _context.UserExchangeConnections
                .FirstOrDefaultAsync(c => c.Id == id);

            if (connection == null) return Json(new { success = false, message = "Connection not found." });
            if (connection.UserId != userId && !User.IsInRole("Admin")) return Json(new { success = false, message = "Forbidden." });

            var all = await _context.UserExchangeConnections
                .Where(c => c.UserId == userId && c.IsDefault)
                .ToListAsync();
            all.ForEach(c => c.IsDefault = false);
            connection.IsDefault = true;
            connection.UpdatedAt = DateTime.UtcNow;

            var userData = await _context.UsersData.FirstOrDefaultAsync(u => u.Id == userId);
            if (userData != null)
            {
                userData.ExchangeId = connection.ExchangeId;
                userData.ApiKey = connection.ApiKey;
                userData.ApiSecret = connection.ApiSecret;
                userData.ApiPassword = connection.ApiPassword;
                _context.UsersData.Update(userData);
            }

            await _context.SaveChangesAsync();
            return Json(new { success = true });
        }
    }
}
