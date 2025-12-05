using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AutoSignals.Data;
using AutoSignals.Models;
using AutoSignals.ViewModels;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.Configuration;
using Microsoft.AspNetCore.Authorization;

namespace AutoSignals.Controllers
{
    [Authorize(Roles = "Admin")]
    public class UsersDataController : Controller
    {
        private readonly AutoSignalsDbContext _context;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;

        public UsersDataController(AutoSignalsDbContext context, UserManager<IdentityUser> userManager, RoleManager<IdentityRole> roleManager)
        {
            _context = context;
            _userManager = userManager;
            _roleManager = roleManager;
        }

        // GET: UsersData
        public async Task<IActionResult> Index()
        {
            var usersData = await _context.UsersData.ToListAsync();
            var users = await _userManager.Users.ToListAsync();
            var userProfiles = new List<UserProfileViewModel>();

            foreach (var user in users)
            {
                var userData = usersData.FirstOrDefault(u => u.Id == user.Id) ?? new UserData();
                var roles = await _userManager.GetRolesAsync(user);

                userProfiles.Add(new UserProfileViewModel
                {
                    User = user,
                    UserData = userData,
                    Roles = roles
                });
            }

            return View(userProfiles);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateUserDetails(UserProfileViewModel model)
        {          
            var user = await _userManager.FindByIdAsync(model.User.Id);
            if (user == null)
            {
                return NotFound();
            }

            user.PhoneNumber = model.User.PhoneNumber;
            var updateUserResult = await _userManager.UpdateAsync(user);
            if (!updateUserResult.Succeeded)
            {
                foreach (var error in updateUserResult.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }
                return View(model);
            }

            var userData = await _context.UsersData.FindAsync(model.UserData.Id);
            if (userData == null)
            {
                return NotFound();
            }

            userData.TelegramId = model.UserData.TelegramId;
            userData.X = model.UserData.X;
            userData.Instagram = model.UserData.Instagram;
            userData.Facebook = model.UserData.Facebook;

            _context.Update(userData);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Details), new { id = model.User.Id });
        }

        // GET: UsersData/Details/5
        public async Task<IActionResult> Details(string id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
            {
                return NotFound();
            }

            var userData = await _context.UsersData.FirstOrDefaultAsync(m => m.Id == id);
            if (userData == null)
            {
                return NotFound();
            }

            var roles = await _userManager.GetRolesAsync(user);

            // Fetch the exchange information based on the ExchangeId
            var exchange = await _context.Exchanges.FirstOrDefaultAsync(e => e.Id == userData.ExchangeId);

            // Fetch the positions and orders for the user
            var positions = await _context.Positions
                .Where(p => p.UserId == id &&
                            (p.Status == "OPEN" ||
                            (p.Status == "CLOSED" && p.Time >= DateTime.Now.AddDays(-30))))
                .ToListAsync();
            var orders = await _context.Orders.Where(o => o.UserId == id).ToListAsync();

            var PositionCount = positions.Count;
            var OpenPositionCount = positions.Count(p => p.Status == "OPEN");

            var providerSettings = await _context.ProvidersSettings
                .Where(p => p.UserId == id)
                .ToListAsync();
            //var activeProviderSettings = providerSettings.Where(p => p.IsEnabled).Select(p => p.ProviderId).ToList();

            var model = new UserProfileViewModel
            {
                User = user,
                UserData = userData,
                Roles = roles,
                Exchange = exchange, // This might be null if no exchange is found
                Positions = positions,
                PositionCount = PositionCount,
                OpenPositionCount = OpenPositionCount,
                Orders = orders,
                AvailableExchanges = _context.Exchanges
                    .Where(e => e.IsEnabled)
                    .Select(e => new SelectListItem
                    {
                        Value = e.Id.ToString(),
                        Text = e.Name
                    }).ToList(),
                ProviderSettings = providerSettings
                
            };

            return View(model);
        }

        // GET: UsersData/Create
        public IActionResult Create()
        {
            return View();
        }

        // POST: UsersData/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("Id,NickName,TelegramId,TelegramNotifications,ExchangeId,ApiKey,ApiSecret,ApiPassword,ApiTestResult,StartBalance,SubscriptionActive,Notes,Time")] UserData userData)
        {
            if (ModelState.IsValid)
            {
                var user = new IdentityUser { UserName = userData.NickName, Email = userData.NickName };
                var result = await _userManager.CreateAsync(user, "DefaultPassword123!"); // Replace with a proper password

                if (result.Succeeded)
                {
                    userData.Id = user.Id;
                    _context.Add(userData);
                    await _context.SaveChangesAsync();
                    return RedirectToAction(nameof(Index));
                }

                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }

                return View(userData);
            }
            return View(userData);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateProviderSettings(UserProfileViewModel model)
        {
            if (model == null || model.ProviderSettings == null)
            {
                return NotFound();
            }

            foreach (var providerSetting in model.ProviderSettings)
            {
                var existingSetting = await _context.ProvidersSettings.FindAsync(providerSetting.Id);
                if (existingSetting == null)
                {
                    return NotFound();
                }

                existingSetting.IsEnabled = providerSetting.IsEnabled;
                existingSetting.OverideLeverage = providerSetting.OverideLeverage;
                existingSetting.Leverage = providerSetting.Leverage;
                existingSetting.UseStoploss = providerSetting.UseStoploss;
                existingSetting.StoplossPercentage = providerSetting.StoplossPercentage;
                existingSetting.MoveStoploss = providerSetting.MoveStoploss;
                existingSetting.MoveStoplossOn = providerSetting.MoveStoplossOn;
                existingSetting.MaxTradeSizeUsd = providerSetting.MaxTradeSizeUsd;
                existingSetting.MinTradeSizeUsd = providerSetting.MinTradeSizeUsd;
                existingSetting.IsIsolated = providerSetting.IsIsolated;
                existingSetting.UseMoonbag = providerSetting.UseMoonbag;
                existingSetting.MoonbagPercentage = providerSetting.MoonbagPercentage;
                existingSetting.MoonbagSize = providerSetting.MoonbagSize;

                _context.Update(existingSetting);
            }

            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Details), new { id = model.User.Id });
        }

        // GET: UsersData/Edit/5
        public async Task<IActionResult> Edit(string id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
            {
                return NotFound();
            }

            var userRoles = await _userManager.GetRolesAsync(user);

            var allRoles = _roleManager.Roles.ToList();
            var userData = await _context.UsersData.FindAsync(id);

            if (userData == null)
            {
                return NotFound();
            }

            var model = new UserProfileViewModel
            {
                User = user,
                UserData = userData,
                Roles = userRoles,
                AvailableRoles = allRoles.Select(role => new SelectListItem
                {
                    Value = role.Id,
                    Text = role.Name
                }).ToList(),
                SelectedRoleId = userRoles.Any() ? allRoles.First(r => r.Name == userRoles.First()).Id : null,
                AvailableExchanges = _context.Exchanges
                    .Where(e => e.IsEnabled)
                    .Select(e => new SelectListItem
                    {
                        Value = e.Id.ToString(),
                        Text = e.Name
                    }).ToList()
            };

            return View(model);
        }

        // POST: UsersData/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(UserProfileViewModel model)
        {
            if (model == null)
            {
                return NotFound();
            }

            var user = await _userManager.FindByIdAsync(model.UserData.Id);
            if (user == null)
            {
                return NotFound();
            }

            var userRoles = await _userManager.GetRolesAsync(user);
            var selectedRole = _roleManager.Roles.FirstOrDefault(r => r.Id == model.SelectedRoleId)?.Name;

            if (selectedRole == null)
            {
                ModelState.AddModelError("", "Selected role is invalid.");
                return View(model);
            }

            // Remove all existing roles
            var removeResult = await _userManager.RemoveFromRolesAsync(user, userRoles);
            if (!removeResult.Succeeded)
            {
                foreach (var error in removeResult.Errors)
                {
                    ModelState.AddModelError("", error.Description);
                }
                return View(model);
            }

            // Add the new role
            var addResult = await _userManager.AddToRoleAsync(user, selectedRole);
            if (!addResult.Succeeded)
            {
                foreach (var error in addResult.Errors)
                {
                    ModelState.AddModelError("", error.Description);
                }
                return View(model);
            }

            // Update UserData
            var userData = await _context.UsersData.FindAsync(model.UserData.Id);
            if (userData == null)
            {
                return NotFound();
            }

            userData.NickName = model.UserData.NickName;
            userData.TelegramId = model.UserData.TelegramId;
            userData.TelegramNotifications = model.UserData.TelegramNotifications;
            userData.ExchangeId = model.UserData.ExchangeId;
            userData.ApiKey = model.UserData.ApiKey;
            userData.ApiSecret = model.UserData.ApiSecret;
            userData.ApiPassword = model.UserData.ApiPassword;
            userData.ApiTestResult = model.UserData.ApiTestResult;
            userData.X = model.UserData.X;
            userData.Instagram = model.UserData.Instagram;
            userData.Facebook = model.UserData.Facebook;
            userData.StartBalance = model.UserData.StartBalance;
            userData.SubscriptionActive = model.UserData.SubscriptionActive;
            userData.Notes = model.UserData.Notes;
            userData.Time = model.UserData.Time;

            _context.Update(userData);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }

        // GET: UsersData/Delete/5
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> Delete(string id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
            {
                return NotFound();
            }

            var userData = await _context.UsersData.FirstOrDefaultAsync(m => m.Id == id);
            if (userData == null)
            {
                return NotFound();
            }

            var model = new UserProfileViewModel
            {
                User = user,
                UserData = userData
            };

            return View(model);
        }

        // POST: UsersData/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
            {
                return NotFound();
            }

            var userData = await _context.UsersData.FindAsync(id);
            if (userData != null)
            {
                _context.UsersData.Remove(userData);
            }

            await _context.SaveChangesAsync();
            await _userManager.DeleteAsync(user);

            return RedirectToAction(nameof(Index));
        }

        private bool UserDataExists(string id)
        {
            return _context.UsersData.Any(e => e.Id == id);
        }
    }

}
