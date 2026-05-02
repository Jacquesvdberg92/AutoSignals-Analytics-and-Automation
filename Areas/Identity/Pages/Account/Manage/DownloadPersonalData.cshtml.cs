// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AutoSignals.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AutoSignals.Areas.Identity.Pages.Account.Manage
{
    public class DownloadPersonalDataModel : PageModel
    {
        private readonly UserManager<IdentityUser> _userManager;
        private readonly ILogger<DownloadPersonalDataModel> _logger;
        private readonly AutoSignalsDbContext _context;

        public DownloadPersonalDataModel(
            UserManager<IdentityUser> userManager,
            ILogger<DownloadPersonalDataModel> logger,
            AutoSignalsDbContext context)
        {
            _userManager = userManager;
            _logger = logger;
            _context = context;
        }

        [BindProperty]
        public InputModel Input { get; set; }

        public class InputModel
        {
            [Required]
            [DataType(DataType.Password)]
            public string Password { get; set; }
        }

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

            _logger.LogInformation("User with ID '{UserId}' downloaded their personal data.", _userManager.GetUserId(User));

            var exportData = new Dictionary<string, object>();

            // 1. Core account identifiers marked [PersonalData] by ASP.NET Core Identity
            var accountFields = new Dictionary<string, string>();
            var personalDataProps = typeof(IdentityUser).GetProperties()
                .Where(p => Attribute.IsDefined(p, typeof(PersonalDataAttribute)));
            foreach (var p in personalDataProps)
                accountFields[p.Name] = p.GetValue(user)?.ToString() ?? "null";

            var logins = await _userManager.GetLoginsAsync(user);
            foreach (var l in logins)
                accountFields[$"{l.LoginProvider} external login"] = l.ProviderDisplayName ?? l.LoginProvider;

            exportData["Account"] = accountFields;

            // 2. Personal profile — name, contact details, social handles, bio
            var userData = await _context.UsersData.FirstOrDefaultAsync(u => u.Id == user.Id);
            exportData["PersonalProfile"] = userData == null ? (object)"No profile found" : new
            {
                DisplayName = userData.NickName,
                TelegramHandle = userData.TelegramId,
                Twitter = userData.X,
                Instagram = userData.Instagram,
                Facebook = userData.Facebook,
                Bio = userData.Notes,
                DateOfBirth = userData.BirthDate,
                MemberSince = userData.Time
            };

            // 3. Subscription information
            exportData["Subscription"] = userData == null ? (object)"No subscription data found" : new
            {
                Tier = userData.SubscriptionTier.ToString(),
                Status = userData.SubscriptionStatus.ToString(),
                StartDate = userData.SubscriptionStartDate,
                EndDate = userData.SubscriptionEndDate,
                TrialEndDate = userData.TrialEndDate
            };

            // 4. Account roles
            var roles = await _userManager.GetRolesAsync(user);
            exportData["AccountRoles"] = roles;

            // 5. Linked exchanges — connection status only; API credentials are not personal data and are never exported
            var linkedExchanges = await _context.UserExchangeConnections
                .Where(c => c.UserId == user.Id)
                .Select(c => new
                {
                    Exchange = c.Exchange != null ? c.Exchange.Name : "Unknown",
                    Nickname = c.Label,
                    IsDefault = c.IsDefault,
                    IsActive = c.IsActive,
                    ConnectedOn = c.CreatedAt,
                    LastVerified = c.LastTestedAt,
                    VerificationPassed = c.TestResult == "1"
                })
                .ToListAsync();
            exportData["LinkedExchanges"] = linkedExchanges;

            // 6. Subscription event history — dates and plan changes only; no payment provider internals
            var billingHistory = await _context.SubscriptionEvents
                .Where(e => e.UserId == user.Id)
                .OrderByDescending(e => e.OccurredAt)
                .Select(e => new
                {
                    EventType = e.EventType,
                    Plan = e.Tier,
                    Amount = e.Amount,
                    Currency = e.Currency,
                    Date = e.OccurredAt
                })
                .ToListAsync();
            exportData["BillingHistory"] = billingHistory;

            // 7. Export info
            exportData["ExportInfo"] = new
            {
                GeneratedAt = DateTime.UtcNow,
                Version = "2.0",
                Note = "This export contains only your personal and contact information. API credentials, trading configuration and system-internal records are not included."
            };

            var jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
            };

            Response.Headers.TryAdd("Content-Disposition", $"attachment; filename=AutoSignals_PersonalData_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json");
            return new FileContentResult(JsonSerializer.SerializeToUtf8Bytes(exportData, jsonOptions), "application/json");
        }
    }
}
