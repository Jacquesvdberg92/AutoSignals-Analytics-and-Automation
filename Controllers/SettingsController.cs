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

            var userProfile = new UserProfileViewModel
            {
                User = user,
                UserData = userData,
                Roles = roles,
                OpenPositionCount = openPositionCount,
                PositionCount = positionCount,
                ProviderSettings = providerSettings,
                Positions = await _context.Positions.Where(p => p.UserId == userId).ToListAsync(),
                AvailableExchanges = availableExchanges
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

    }
}
