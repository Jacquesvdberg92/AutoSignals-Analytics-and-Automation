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
    public class SubscriptionController : Controller
    {
        private readonly ISubscriptionProvider _subscriptionProvider;
        private readonly ISubscriptionService _subscriptionService;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly SignInManager<IdentityUser> _signInManager;
        private readonly AutoSignalsDbContext _context;
        private readonly ILogger<SubscriptionController> _logger;
        private readonly NOWPaymentsSubscriptionProvider _nowPaymentsProvider;
        private readonly NOWPaymentsWebhookService _webhookService;

        public SubscriptionController(
            ISubscriptionProvider subscriptionProvider,
            ISubscriptionService subscriptionService,
            UserManager<IdentityUser> userManager,
            SignInManager<IdentityUser> signInManager,
            AutoSignalsDbContext context,
            ILogger<SubscriptionController> logger,
            NOWPaymentsSubscriptionProvider nowPaymentsProvider,
            NOWPaymentsWebhookService webhookService)
        {
            _subscriptionProvider = subscriptionProvider;
            _subscriptionService = subscriptionService;
            _userManager = userManager;
            _signInManager = signInManager;
            _context = context;
            _logger = logger;
            _nowPaymentsProvider = nowPaymentsProvider;
            _webhookService = webhookService;
        }

        // ── Checkout ──────────────────────────────────────────────────────────

        /// <summary>
        /// Creates a NOWPayments invoice and redirects the user to the hosted crypto payment page.
        /// </summary>
        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Checkout(int planId)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Challenge();

            _logger.LogInformation(
                "NOWPayments user flow step A1: checkout requested. UserId={UserId} PlanId={PlanId}",
                user.Id, planId);

            // planId is embedded in the success URL so the Success action can run the IPN fallback
            // if the webhook was never delivered.
            var successUrl = Url.Action("Success", "Subscription", new { planId }, "https")!;
            var cancelUrl  = Url.Action("Cancel",  "Subscription", null,          "https")!;

            try
            {
                var checkoutUrl = await _subscriptionProvider.CreateCheckoutSessionAsync(
                    user.Id, planId, successUrl, cancelUrl);

                _logger.LogInformation(
                    "NOWPayments user flow step A2: checkout URL created. UserId={UserId} PlanId={PlanId} CheckoutUrl={CheckoutUrl}",
                    user.Id, planId, checkoutUrl);

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

        /// <summary>
        /// Post-payment confirmation page.
        /// Primary path: the IPN webhook has already activated the subscription.
        /// Fallback path: if the IPN was never delivered, the planId query param (embedded
        /// by Checkout) lets us poll NOWPayments directly and activate immediately.
        /// </summary>
        [HttpGet]
        [Authorize]
        public async Task<IActionResult> Success(
            int? planId,
            [FromQuery(Name = "payment_id")] string? paymentId,
            [FromQuery(Name = "orderId")] string? orderId,
            [FromQuery(Name = "attempt")] int attempt = 0)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Challenge();

            paymentId ??= Request.Query["paymentId"].FirstOrDefault();
            paymentId = string.IsNullOrWhiteSpace(paymentId) ? null : paymentId.Trim();
            orderId = string.IsNullOrWhiteSpace(orderId) ? null : orderId.Trim();
            ViewBag.PaymentId = paymentId;
            ViewBag.OrderId = orderId;
            ViewBag.Attempt = attempt;

            var querySnapshot = string.Join("&", Request.Query
                .Select(kvp => $"{kvp.Key}={string.Join(",", kvp.Value.Select(v => v?.Length > 128 ? v[..128] + "..." : v))}"));

            _logger.LogInformation(
                "NOWPayments user flow step B1: success page hit. UserId={UserId} PlanId={PlanId} OrderId={OrderId} PaymentId={PaymentId} Attempt={Attempt}",
                user.Id, planId, orderId, paymentId, attempt);
            _logger.LogInformation(
                "NOWPayments success redirect query snapshot. UserId={UserId} Query={Query}",
                user.Id, querySnapshot);

            var subData = await _subscriptionService.GetSubscriptionDataAsync(user.Id);
            var isActive = subData?.SubscriptionStatus == SubscriptionStatus.Active
                        && subData?.SubscriptionProvider == "NOWPayments";

            // IPN fallback — runs when the subscription is not yet active.
            // Primary path uses the planId query parameter from checkout.
            // Resilient path: if planId is missing, scan active plans for a finished payment.
            if (!isActive)
            {
                _logger.LogInformation(
                    "NOWPayments user flow step B2: subscription not yet active; starting fallback lookup. UserId={UserId}",
                    user.Id);

                string? matchedOrderId = null;
                string? rawJson = null;

                // Best fallback key: exact orderId returned in success redirect.
                if (!string.IsNullOrWhiteSpace(orderId))
                {
                    try
                    {
                        rawJson = await _nowPaymentsProvider.GetFinishedPaymentRawByOrderIdAsync(orderId);
                        if (rawJson != null)
                            matchedOrderId = orderId;

                        _logger.LogInformation(
                            "NOWPayments user flow step B3: orderId fallback lookup complete. UserId={UserId} OrderId={OrderId} Found={Found}",
                            user.Id, orderId, rawJson != null);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex,
                            "NOWPayments success-page fallback failed for orderId {OrderId}.",
                            orderId);
                    }
                }

                // Best signal from NOWPayments redirect: explicit payment_id.
                if (rawJson == null && !string.IsNullOrWhiteSpace(paymentId))
                {
                    _logger.LogInformation(
                        "NOWPayments user flow step B4: trying payment_id fallback. UserId={UserId} PaymentId={PaymentId}",
                        user.Id, paymentId);

                    try
                    {
                        var byIdRaw = await _nowPaymentsProvider.GetPaymentRawAsync(paymentId);
                        if (!string.IsNullOrWhiteSpace(byIdRaw))
                        {
                            using var byIdDoc = JsonDocument.Parse(byIdRaw);
                            var byIdRoot = byIdDoc.RootElement;

                            var byIdStatus = byIdRoot.TryGetProperty("payment_status", out var byIdPs)
                                ? byIdPs.GetString()
                                : null;
                            var byIdOrderId = byIdRoot.TryGetProperty("order_id", out var byIdOid)
                                ? byIdOid.GetString()
                                : null;

                            // Safety check: only process finished payments that belong to this user.
                            if (string.Equals(byIdStatus, "finished", StringComparison.OrdinalIgnoreCase)
                                && !string.IsNullOrWhiteSpace(byIdOrderId)
                                && byIdOrderId.StartsWith($"{user.Id}:", StringComparison.Ordinal))
                            {
                                rawJson = byIdRaw;
                                matchedOrderId = byIdOrderId;
                            }

                            _logger.LogInformation(
                                "NOWPayments user flow step B5: payment_id fallback complete. UserId={UserId} PaymentId={PaymentId} Found={Found} MatchedOrderId={MatchedOrderId}",
                                user.Id, paymentId, rawJson != null, matchedOrderId);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex,
                            "NOWPayments success-page fallback failed for payment_id {PaymentId}.",
                            paymentId);
                    }
                }

                if (rawJson == null)
                {
                    _logger.LogWarning(
                        "NOWPayments success-page: no finished payment found for user {UserId}. " +
                        "OrderId={OrderId} PaymentId={PaymentId}. Payment may still be confirming on-chain.",
                        user.Id, orderId, paymentId);
                }

                if (rawJson != null && matchedOrderId != null)
                {
                    _logger.LogInformation(
                        "NOWPayments success-page fallback: found finished payment for order {OrderId}. Processing.",
                        matchedOrderId);
                    await _webhookService.HandleEventAsync(rawJson);
                    subData = await _subscriptionService.GetSubscriptionDataAsync(user.Id);

                    _logger.LogInformation(
                        "NOWPayments user flow step B6: finished payment processed and subscription reloaded. UserId={UserId} OrderId={OrderId}",
                        user.Id, matchedOrderId);
                }
                else
                {
                    // Payment may still be confirming on-chain — auto-refresh the page.
                    ViewBag.PaymentPending = true;
                    ViewBag.PlanId = planId;
                    ViewBag.PaymentId = paymentId;
                    ViewBag.OrderId = orderId;

                    _logger.LogInformation(
                        "NOWPayments user flow step B6: payment still pending confirmation. UserId={UserId} OrderId={OrderId} PaymentId={PaymentId}",
                        user.Id, orderId, paymentId);
                }
            }
            else
            {
                _logger.LogInformation(
                    "NOWPayments user flow step B2: subscription already active on success page. UserId={UserId}",
                    user.Id);
            }

            await RefreshSignInIfSubscriptionClaimsStaleAsync(user, subData);

            return View(subData);
        }

        private async Task RefreshSignInIfSubscriptionClaimsStaleAsync(IdentityUser user, UserData? subData)
        {
            if (subData?.SubscriptionStatus != SubscriptionStatus.Active)
                return;

            bool hasVipEquivalent = User.IsInRole("VIP") || User.IsInRole("Tester") || User.IsInRole("Admin");
            bool hasProEquivalent = hasVipEquivalent || User.IsInRole("Pro") || User.IsInRole("Subscriber");

            bool requiresVipEquivalent = subData.SubscriptionTier == SubscriptionTier.VIP;
            bool hasRequiredRoleClaims = requiresVipEquivalent ? hasVipEquivalent : hasProEquivalent;

            if (hasRequiredRoleClaims)
                return;

            _logger.LogInformation(
                "NOWPayments role-claim refresh: refreshing sign-in cookie for user {UserId}. Tier={Tier}",
                user.Id, subData.SubscriptionTier);

            await _signInManager.RefreshSignInAsync(user);
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
