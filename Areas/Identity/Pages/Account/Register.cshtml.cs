// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading;
using System.Threading.Tasks;
using AutoSignals.Data;
using AutoSignals.Models;
using AutoSignals.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AutoSignals.Areas.Identity.Pages.Account
{
    public class RegisterModel : PageModel
    {
        private readonly SignInManager<IdentityUser> _signInManager;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly IUserStore<IdentityUser> _userStore;
        private readonly IUserEmailStore<IdentityUser> _emailStore;
        private readonly ILogger<RegisterModel> _logger;
        private readonly IEmailSender _emailSender;
        private readonly AutoSignalsDbContext _context;
        private readonly RecaptchaService _recaptchaService;
        private readonly IConfiguration _configuration;

        private readonly ErrorLogService _errorLogService;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly AesEncryptionService _encryptionService;
        private readonly ExchangeBalanceService _exchangeBalanceService;

        public string RecaptchaSiteKey { get; set; }

        public List<SelectListItem> AvailableExchanges { get; private set; } = new();

        public RegisterModel(
            UserManager<IdentityUser> userManager,
            IUserStore<IdentityUser> userStore,
            SignInManager<IdentityUser> signInManager,
            ILogger<RegisterModel> logger,
            IEmailSender emailSender,
            AutoSignalsDbContext context,
            RecaptchaService recaptchaService,
            IConfiguration configuration,
            ErrorLogService errorLogService,
            IServiceScopeFactory scopeFactory,
            AesEncryptionService encryptionService,
            ExchangeBalanceService exchangeBalanceService)
        {
            _userManager = userManager;
            _userStore = userStore;
            _emailStore = GetEmailStore();
            _signInManager = signInManager;
            _logger = logger;
            _emailSender = emailSender;
            _context = context;
            _recaptchaService = recaptchaService;
            _configuration = configuration;
            _errorLogService = errorLogService;
            _scopeFactory = scopeFactory;
            _encryptionService = encryptionService;

            RecaptchaSiteKey = _configuration["Recaptcha:SiteKey"];
            _exchangeBalanceService = exchangeBalanceService;
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
        public string ReturnUrl { get; set; }

        /// <summary>
        ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public IList<AuthenticationScheme> ExternalLogins { get; set; }

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
            [EmailAddress]
            [Display(Name = "Email")]
            public string Email { get; set; }

            /// <summary>
            ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
            ///     directly from your code. This API may change or be removed in future releases.
            /// </summary>
            [Required]
            [StringLength(100, ErrorMessage = "The {0} must be at least {2} and at max {1} characters long.", MinimumLength = 6)]
            [DataType(DataType.Password)]
            [Display(Name = "Password")]
            public string Password { get; set; }

            /// <summary>
            ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
            ///     directly from your code. This API may change or be removed in future releases.
            /// </summary>
            [DataType(DataType.Password)]
            [Display(Name = "Confirm password")]
            [Compare("Password", ErrorMessage = "The password and confirmation password do not match.")]
            public string ConfirmPassword { get; set; }


            // UserData.cs:6-8
            [Display(Name = "Nickname")]
            [StringLength(64)]
            public string NickName { get; set; }

            [Display(Name = "Telegram Id")]
            [StringLength(64)]
            public string TelegramId { get; set; }

            [Display(Name = "Telegram Notifications")]
            [StringLength(16)]
            public string TelegramNotifications { get; set; }

            [Display(Name = "Email Notifications")]
            [StringLength(16)]
            public string EmailNotifications { get; set; }

            [Display(Name = "X")]
            [StringLength(128)]
            public string X { get; set; }

            [Display(Name = "Instagram")]
            [StringLength(128)]
            public string Instagram { get; set; }

            [Display(Name = "Facebook")]
            [StringLength(128)]
            public string Facebook { get; set; }

            [Display(Name = "Start Balance")]
            [StringLength(32)]
            public string StartBalance { get; set; }

            // Admin/system-ish, but exposing for now
            [Display(Name = "Subscription Active")]
            [StringLength(8)]
            public string SubscriptionActive { get; set; }

            // Birth date (new)
            [Display(Name = "Birth Date")]
            [DataType(DataType.Date)]
            public DateOnly? BirthDate { get; set; }

            // UserData.cs:10-14 placeholders (refine later)
            [Display(Name = "Exchange")]
            public int? ExchangeId { get; set; }

            [Display(Name = "API Key")]
            public string ApiKey { get; set; }

            [Display(Name = "API Secret")]
            public string ApiSecret { get; set; }

            [Display(Name = "API Password")]
            public string ApiPassword { get; set; }

            [Display(Name = "API Test Result")]
            public string ApiTestResult { get; set; }
        }


        public async Task OnGetAsync(string returnUrl = null)
        {
            ReturnUrl = returnUrl;
            ExternalLogins = (await _signInManager.GetExternalAuthenticationSchemesAsync()).ToList();
            RecaptchaSiteKey = _configuration["Recaptcha:SiteKey"];

            await LoadExchangesAsync();
        }

        public async Task<IActionResult> OnPostAsync(string returnUrl = null, [FromForm(Name = "g-recaptcha-response")] string recaptchaResponse = null)
        {
            returnUrl ??= Url.Content("~/");
            ExternalLogins = (await _signInManager.GetExternalAuthenticationSchemesAsync()).ToList();
            RecaptchaSiteKey = _configuration["Recaptcha:SiteKey"];

            // Needed when redisplaying the page due to validation / captcha errors
            await LoadExchangesAsync();

            // Validate reCAPTCHA
            var recaptchaResult = await _recaptchaService.VerifyAsyncFull(recaptchaResponse);
            if (recaptchaResult == null || !recaptchaResult.Success || recaptchaResult.Score < 0.5)
            {
                ModelState.AddModelError(string.Empty, "CAPTCHA validation failed. Please try again.");
                return Page();
            }

            if (ModelState.IsValid)
            {
                var user = CreateUser();

                await _userStore.SetUserNameAsync(user, Input.Email, CancellationToken.None);
                await _emailStore.SetEmailAsync(user, Input.Email, CancellationToken.None);
                var result = await _userManager.CreateAsync(user, Input.Password);

                if (result.Succeeded)
                {
                    _logger.LogInformation("User created a new account with password.");

                    var userId = await _userManager.GetUserIdAsync(user);

                    // Test API with plain credentials (same approach as SettingsController)
                    var apiKeyPlain = Input.ApiKey ?? string.Empty;
                    var apiSecretPlain = Input.ApiSecret ?? string.Empty;
                    var apiPasswordPlain = Input.ApiPassword ?? string.Empty;

                    var apiTestResult = "0";
                    try
                    {
                        var balance = await _exchangeBalanceService.GetExchangeBalanceAsync(
                            Input.ExchangeId,
                            apiKeyPlain,
                            apiSecretPlain,
                            apiPasswordPlain);

                        apiTestResult = balance > 0m ? "1" : "0";
                    }
                    catch
                    {
                        apiTestResult = "0";
                    }

                    var userData = new UserData
                    {
                        Id = userId,
                        Time = DateTime.UtcNow,

                        SubscriptionActive = string.IsNullOrWhiteSpace(Input.SubscriptionActive) ? "1" : Input.SubscriptionActive,

                        NickName = Input.NickName,
                        TelegramId = Input.TelegramId,
                        TelegramNotifications = Input.TelegramNotifications,
                        EmailNotifications = Input.EmailNotifications,

                        X = Input.X,
                        Instagram = Input.Instagram,
                        Facebook = Input.Facebook,

                        StartBalance = Input.StartBalance,
                        BirthDate = Input.BirthDate,

                        ExchangeId = Input.ExchangeId,

                        // Encrypt API credentials before saving (same as SettingsController)
                        ApiKey = _encryptionService.Encrypt(apiKeyPlain),
                        ApiSecret = _encryptionService.Encrypt(apiSecretPlain),
                        ApiPassword = _encryptionService.Encrypt(apiPasswordPlain),

                        ApiTestResult = apiTestResult
                    };

                    _context.UsersData.Add(userData);
                    await _context.SaveChangesAsync();

                    // Assign the "Tester" role to the user - for now while I am still developing the app
                    var roleAssignmentResult = await _userManager.AddToRoleAsync(user, "Tester");
                    if (!roleAssignmentResult.Succeeded)
                    {
                        foreach (var error in roleAssignmentResult.Errors)
                        {
                            ModelState.AddModelError(string.Empty, error.Description);
                        }
                        return Page();
                    }

                    var code = await _userManager.GenerateEmailConfirmationTokenAsync(user);
                    code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
                    var callbackUrl = Url.Page(
                        "/Account/ConfirmEmail",
                        pageHandler: null,
                        values: new { area = "Identity", userId = userId, code = code, returnUrl = returnUrl },
                        protocol: Request.Scheme);

                    await _emailSender.SendEmailAsync(
                        Input.Email,
                        "Please Confirm Your Email Address",
                        $@"
    <html>
        <body style='font-family: Arial, sans-serif; background-color: #f4f4f4; padding: 20px;'>
            <table style='width: 100%; max-width: 600px; margin: 0 auto; background-color: white; padding: 20px; border-radius: 8px;'>
                <tr>
                    <td style='text-align: center;'>
                        <h2 style='color: #4CAF50;'>Welcome to AutoSignals!</h2>
                        <p style='color: #555;'>Thank you for registering with us. 
Please confirm your email address to activate your account.</p>
                    </td>
                </tr>
                <tr>
                    <td style='text-align: center;'>
                        <a href='{HtmlEncoder.Default.Encode(callbackUrl)}' 
                            style='background-color: #4CAF50; color: white; text-decoration: none; padding: 15px 30px; font-size: 16px; border-radius: 5px;'>Confirm Your Email</a>
                    </td>
                </tr>
                <tr>
                    <td style='text-align: center; padding-top: 20px;'>
                        <p style='color: #888;'>If you did not create an account, you can safely ignore this email.</p>
                        <p style='color: #888;'>This email is not monitored, please do not reply.</p>
                    </td>
                </tr>
            </table>
        </body>
    </html>"
                    );

                    if (_userManager.Options.SignIn.RequireConfirmedAccount)
                    {
                        return RedirectToPage("RegisterConfirmation", new { email = Input.Email, returnUrl = returnUrl });
                    }

                    await _signInManager.SignInAsync(user, isPersistent: false);
                    return LocalRedirect(returnUrl);
                }

                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }
            }

            // If we got this far, something failed, redisplay form
            return Page();
        }

        public sealed class ApiTestRequest
        {
            public int? ExchangeId { get; init; }
            public string ApiKey { get; init; }
            public string ApiSecret { get; init; }
            public string ApiPassword { get; init; }
        }

        public sealed class ApiTestResponse
        {
            public bool Success { get; init; }
            public string Message { get; init; }
            public decimal? Balance { get; init; }
        }

        public async Task<IActionResult> OnPostTestApiAsync([FromBody] ApiTestRequest request)
        {
            if (request == null)
                return new JsonResult(new ApiTestResponse { Success = false, Message = "Invalid request." });

            if (request.ExchangeId is null)
                return new JsonResult(new ApiTestResponse { Success = false, Message = "Select an exchange first." });

            var apiKey = request.ApiKey ?? string.Empty;
            var apiSecret = request.ApiSecret ?? string.Empty;
            var apiPassword = request.ApiPassword ?? string.Empty;

            try
            {
                var balance = await _exchangeBalanceService.GetExchangeBalanceAsync(
                    request.ExchangeId,
                    apiKey,
                    apiSecret,
                    apiPassword);

                return new JsonResult(new ApiTestResponse
                {
                    Success = balance > 0m,
                    Balance = balance,
                    Message = balance > 0m ? "API OK." : "API test failed (0 balance or credentials invalid)."
                });
            }
            catch (Exception ex)
            {
                return new JsonResult(new ApiTestResponse
                {
                    Success = false,
                    Message = ex.Message
                });
            }
        }

        private async Task LoadExchangesAsync()
        {
            AvailableExchanges = await _context.Exchanges
                .Where(e => e.IsEnabled)
                .OrderBy(e => e.Name)
                .Select(e => new SelectListItem { Value = e.Id.ToString(), Text = e.Name })
                .ToListAsync();

            AvailableExchanges.Insert(0, new SelectListItem { Value = "", Text = "Select..." });
        }

        private IdentityUser CreateUser()
        {
            try { return Activator.CreateInstance<IdentityUser>(); }
            catch
            {
                throw new InvalidOperationException($"Can't create an instance of '{nameof(IdentityUser)}'. " +
                    $"Ensure that '{nameof(IdentityUser)}' is not an abstract class and has a parameterless constructor, or alternatively " +
                    $"override the register page in /Areas/Identity/Pages/Account/Register.cshtml");
            }
        }

        private IUserEmailStore<IdentityUser> GetEmailStore()
        {
            if (!_userManager.SupportsUserEmail)
                throw new NotSupportedException("The default UI requires a user store with email support.");

            return (IUserEmailStore<IdentityUser>)_userStore;
        }
    }
}
