using AutoSignals.Data;
using AutoSignals.Models;
using AutoSignals.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AutoSignals.Controllers
{
    public class SubscriptionController : Controller
    {
        private readonly ISubscriptionProvider _subscriptionProvider;
        private readonly ISubscriptionService _subscriptionService;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly AutoSignalsDbContext _context;
        private readonly ILogger<SubscriptionController> _logger;

        public SubscriptionController(
            ISubscriptionProvider subscriptionProvider,
            ISubscriptionService subscriptionService,
            UserManager<IdentityUser> userManager,
            AutoSignalsDbContext context,
            ILogger<SubscriptionController> logger)
        {
            _subscriptionProvider = subscriptionProvider;
            _subscriptionService = subscriptionService;
            _userManager = userManager;
            _context = context;
            _logger = logger;
        }

        // ── Checkout ──────────────────────────────────────────────────────────

        /// <summary>
        /// Creates a LemonSqueezy checkout session and redirects the user to the hosted page.
        /// </summary>
        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Checkout(int planId)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Challenge();

            var successUrl = Url.Action("Success", "Subscription", null, "https")!;
            var cancelUrl = Url.Action("Cancel", "Subscription", null, "https")!;

            try
            {
                var checkoutUrl = await _subscriptionProvider.CreateCheckoutSessionAsync(
                    user.Id, planId, successUrl, cancelUrl);

                return Redirect(checkoutUrl);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Checkout creation failed for user {UserId} plan {PlanId}.", user.Id, planId);
                TempData["Error"] = $"Could not start checkout: {ex.Message}";
                return RedirectToAction(nameof(Manage));
            }
        }

        // ── Success ───────────────────────────────────────────────────────────

        /// <summary>Post-payment confirmation page. Webhook has already activated the subscription.</summary>
        [HttpGet]
        [Authorize]
        public async Task<IActionResult> Success()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Challenge();

            var subData = await _subscriptionService.GetSubscriptionDataAsync(user.Id);
            return View(subData);
        }

        // ── Cancel ────────────────────────────────────────────────────────────

        /// <summary>User closed the LemonSqueezy checkout overlay without paying.</summary>
        [HttpGet]
        public IActionResult Cancel()
        {
            return View();
        }

        // ── Manage ────────────────────────────────────────────────────────────

        /// <summary>Displays the current subscription status and plan details.</summary>
        [HttpGet]
        [Authorize]
        public async Task<IActionResult> Manage()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Challenge();

            var subData = await _subscriptionService.GetSubscriptionDataAsync(user.Id);

            // Admin and Tester roles are treated as VIP for all feature gates.
            // If the DB row hasn't been upgraded yet, reflect that in the display.
            var roles = await _userManager.GetRolesAsync(user);
            if (subData != null && (roles.Contains("Admin") || roles.Contains("Tester")))
            {
                subData.SubscriptionTier   = SubscriptionTier.VIP;
                subData.SubscriptionStatus = SubscriptionStatus.Active;
            }

            var plans = await _context.SubscriptionPlans
                .AsNoTracking()
                .Where(p => p.IsActive)
                .ToListAsync();

            ViewBag.Plans = plans;
            return View(subData);
        }

        // ── Portal ────────────────────────────────────────────────────────────

        /// <summary>Redirects the authenticated user to the LemonSqueezy customer portal.</summary>
        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Portal()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Challenge();

            var returnUrl = Url.Action("Manage", "Subscription", null, Request.Scheme)!;

            try
            {
                var portalUrl = await _subscriptionProvider.GetBillingPortalUrlAsync(user.Id, returnUrl);
                return Redirect(portalUrl);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Portal redirect failed for user {UserId}.", user.Id);
                TempData["Error"] = "Could not open billing portal. Please try again.";
                return RedirectToAction("Manage");
            }
        }
    }
}
