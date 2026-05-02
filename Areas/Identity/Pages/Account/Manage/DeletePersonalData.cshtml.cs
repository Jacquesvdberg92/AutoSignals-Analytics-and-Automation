// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable

using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using AutoSignals.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AutoSignals.Areas.Identity.Pages.Account.Manage
{
    public class DeletePersonalDataModel : PageModel
    {
        private readonly UserManager<IdentityUser> _userManager;
        private readonly SignInManager<IdentityUser> _signInManager;
        private readonly ILogger<DeletePersonalDataModel> _logger;
        private readonly AutoSignalsDbContext _context;

        public DeletePersonalDataModel(
            UserManager<IdentityUser> userManager,
            SignInManager<IdentityUser> signInManager,
            ILogger<DeletePersonalDataModel> logger,
            AutoSignalsDbContext context)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _logger = logger;
            _context = context;
        }

        /// <summary>
        ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        [BindProperty]
        public InputModel Input { get; set; }

        /// <summary>
        ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public class InputModel
        {
            /// <summary>
            ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
            ///     directly from your code. This API may change or be removed in future releases.
            /// </summary>
            [Required]
            [DataType(DataType.Password)]
            public string Password { get; set; }
        }

        /// <summary>
        ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public bool RequirePassword { get; set; }

        public async Task<IActionResult> OnGet()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound($"Unable to load user with ID '{_userManager.GetUserId(User)}'.");
            }

            RequirePassword = await _userManager.HasPasswordAsync(user);
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound($"Unable to load user with ID '{_userManager.GetUserId(User)}'.");
            }

            RequirePassword = await _userManager.HasPasswordAsync(user);
            if (RequirePassword)
            {
                if (!await _userManager.CheckPasswordAsync(user, Input.Password))
                {
                    ModelState.AddModelError(string.Empty, "Incorrect password.");
                    return Page();
                }
            }

            var userId = await _userManager.GetUserIdAsync(user);

            _logger.LogInformation("User with ID '{UserId}' initiated account deletion.", userId);

            // Delete all user-related data from AutoSignals database
            // Order matters - delete child entities before parents to avoid FK constraints

            // 1. Portfolio Holdings (child of Portfolios)
            var portfolios = await _context.Portfolios
                .Where(p => p.UserId == userId)
                .Include(p => p.Holdings)
                .ToListAsync();
            foreach (var portfolio in portfolios)
            {
                _context.PortfolioHoldings.RemoveRange(portfolio.Holdings);
            }

            // 2. Portfolios
            _context.Portfolios.RemoveRange(portfolios);

            // 3. Orders
            var orders = await _context.Orders.Where(o => o.UserId == userId).ToListAsync();
            _context.Orders.RemoveRange(orders);

            // 4. Positions
            var positions = await _context.Positions.Where(p => p.UserId == userId).ToListAsync();
            _context.Positions.RemoveRange(positions);

            // 5. Provider Settings
            var providerSettings = await _context.ProvidersSettings.Where(p => p.UserId == userId).ToListAsync();
            _context.ProvidersSettings.RemoveRange(providerSettings);

            // 6. Notification Settings
            var notificationSettings = await _context.UserNotificationSettings.Where(n => n.UserId == userId).ToListAsync();
            _context.UserNotificationSettings.RemoveRange(notificationSettings);

            // 7. Exchange Connections
            var exchangeConnections = await _context.UserExchangeConnections.Where(c => c.UserId == userId).ToListAsync();
            _context.UserExchangeConnections.RemoveRange(exchangeConnections);

            // 8. User Visits
            var userVisits = await _context.UserVisits.Where(v => v.UserId == userId).ToListAsync();
            _context.UserVisits.RemoveRange(userVisits);

            // 9. Subscription Events
            var subscriptionEvents = await _context.SubscriptionEvents.Where(e => e.UserId == userId).ToListAsync();
            _context.SubscriptionEvents.RemoveRange(subscriptionEvents);

            // 10. UserData (profile information)
            var userData = await _context.UsersData.FindAsync(userId);
            if (userData != null)
            {
                _context.UsersData.Remove(userData);
            }

            // Save all deletions
            await _context.SaveChangesAsync();

            // Finally, delete the Identity user (this also deletes roles, tokens, claims, etc.)
            var result = await _userManager.DeleteAsync(user);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException($"Unexpected error occurred deleting user.");
            }

            await _signInManager.SignOutAsync();

            _logger.LogInformation("User with ID '{UserId}' deleted themselves and all associated data.", userId);

            return Redirect("~/");
        }
    }
}
