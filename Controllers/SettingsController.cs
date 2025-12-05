using AutoSignals.Data;
using AutoSignals.Models;
using AutoSignals.Services;
using AutoSignals.ViewModels;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace AutoSignals.Controllers
{
    public class SettingsController : Controller
    {
        private readonly AutoSignalsDbContext _context;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly ErrorLogService _errorLogService;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly AesEncryptionService _encryptionService;

        public SettingsController(
        AutoSignalsDbContext context,
        UserManager<IdentityUser> userManager,
        RoleManager<IdentityRole> roleManager,
        ErrorLogService errorLogService,
        IServiceScopeFactory scopeFactory,
        AesEncryptionService encryptionService // Inject here
)
        {
            _context = context;
            _userManager = userManager;
            _roleManager = roleManager;
            _errorLogService = errorLogService;
            _scopeFactory = scopeFactory;
            _encryptionService = encryptionService;
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
            var openPositionCount = await _context.Positions.CountAsync(p => p.UserId == userId && p.Status == "Open");
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
        public async Task<IActionResult> UpdateUserDetails(UserProfileViewModel model)
        {
            try
            {
                var user = await _userManager.FindByIdAsync(model.User.Id);
                var userData = await _context.UsersData.FirstOrDefaultAsync(u => u.Id == model.UserData.Id);

                // Use plain API credentials for balance check
                var apiKey = model.UserData.ApiKey ?? "";
                var apiSecret = model.UserData.ApiSecret ?? "";
                var apiPassword = model.UserData.ApiPassword ?? "";

                decimal balance = 0;
                switch (model.UserData.ExchangeId)
                {
                    case 1: // Bitget
                        var bitgetService = new BitgetPriceService(
                            apiKey,
                            apiSecret,
                            apiPassword,
                            _errorLogService,
                            _scopeFactory
                        );
                        balance = await bitgetService.GetBalance(apiKey, apiSecret, apiPassword);
                        break;
                    case 2: // OKX
                        var okxService = new OkxPriceService(
                            apiKey,
                            apiSecret,
                            apiPassword,
                            _errorLogService,
                            _scopeFactory
                        );
                        balance = await okxService.GetBalance(apiKey, apiSecret, apiPassword);
                        break;
                    // Add cases for other exchanges if needed
                    default:
                        return Json(new { success = false, message = "Unsupported exchange" });
                }

                if (balance > 0)
                {
                    userData.ApiTestResult = "1";
                }
                else
                {
                    userData.ApiTestResult = "0";
                }

                if (user != null && userData != null)
                {
                    // Encrypt API credentials before saving
                    userData.ExchangeId = model.UserData.ExchangeId;
                    userData.ApiKey = _encryptionService.Encrypt(apiKey);
                    userData.ApiSecret = _encryptionService.Encrypt(apiSecret);
                    userData.ApiPassword = _encryptionService.Encrypt(apiPassword);

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



        [HttpPost]
        public async Task<IActionResult> UpdateProviderSettings(UserProfileViewModel model)
        {
            try
            {
                var userId = _userManager.GetUserId(User);
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

    }
}
