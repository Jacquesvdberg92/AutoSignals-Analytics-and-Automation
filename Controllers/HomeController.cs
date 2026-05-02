using AutoSignals.Data;
using AutoSignals.Models;
using AutoSignals.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using starterkit.Models;
using System.Diagnostics;

namespace starterkit.Controllers;

public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;
    private readonly IAnalyticsService _analyticsService;
    private readonly AutoSignalsDbContext _context;

    public HomeController(ILogger<HomeController> logger, IAnalyticsService analyticsService, AutoSignalsDbContext context)
    {
        _logger = logger;
        _context = context;
        _analyticsService = analyticsService;
    }

    [Route("/")]
    [Route("/index")]
    public async Task<IActionResult> index()
    {
        _analyticsService.Increment("Landing Page");
        var plans = await _context.SubscriptionPlans
            .AsNoTracking()
            .Where(p => p.IsActive)
            .OrderBy(p => p.Tier)
            .ThenBy(p => p.IsAnnual)
            .ToListAsync();
        ViewBag.ActiveProviderCount = await _context.SignalProviders
            .AsNoTracking()
            .CountAsync(p => p.IsActive);
        return View(plans);
    }

    public IActionResult ComingSoon()
    {
        _analyticsService.Increment("ComingSoon");
        return View("~/Views/Shared/comingsoon.cshtml");
    }

    [Route("/terms-conditions")]
    public IActionResult TermsConditions()
    {
        _analyticsService.Increment("TermsConditions");
        return View("~/Views/Pages/terms_conditions.cshtml");
    }
    //////////////////////////////////////////////////////


    //////////////////////////////////////////////////////
    [Route("/pricing")]
    public async Task<IActionResult> Pricing()
    {
        _analyticsService.Increment("Pricing");
        var plans = await _context.SubscriptionPlans
            .AsNoTracking()
            .Where(p => p.IsActive)
            .OrderBy(p => p.Tier)
            .ThenBy(p => p.IsAnnual)
            .ToListAsync();
        return View("~/Views/Pages/pricing.cshtml", plans);
    }

    [Route("/comingsoon")]
    public IActionResult Comingsoon()
    {
        _analyticsService.Increment("Comingsoon");
        return View("~/Views/Pages/comingsoon.cshtml");
    }
    //////////////////////////////////////////////////////

    //////////////////////////////////////////////////////
    [Route("/education/basics")]
    public IActionResult EduBasics()
    {
        _analyticsService.Increment("EduBasics");
        return View("~/Views/Pages/edu_basics.cshtml");
    }

    [Route("/education/common-strategies")]
    public IActionResult EduCommonStrategies()
    {
        _analyticsService.Increment("EduCommonStrategies");
        return View("~/Views/Pages/edu_common_stratagies.cshtml");
    }

    [Route("/education/fundamental-analysis")]
    public IActionResult EduFA()
    {
        _analyticsService.Increment("EduFA");
        return View("~/Views/Pages/edu_fa.cshtml");
    }

    [Route("/education/leverage")]
    public IActionResult EduLeverage()
    {
        _analyticsService.Increment("EduLeverage");
        return View("~/Views/Pages/edu_leverage.cshtml");
    }

    [Route("/education/risk-management")]
    public IActionResult EduRiskManagement()
    {
        _analyticsService.Increment("EduRiskManagement");
        return View("~/Views/Pages/edu_risk_management.cshtml");
    }

    [Route("/education/technical-analysis")]
    public IActionResult EduTA()
    {
        _analyticsService.Increment("EduTA");
        return View("~/Views/Pages/edu_ta.cshtml");
    }

    [Route("/education/volatility")]
    public IActionResult EduVolatility()
    {
        _analyticsService.Increment("EduVolatility");
        return View("~/Views/Pages/edu_volitility.cshtml");
    }

    [Route("/education/wallets")]
    public IActionResult EduWallets()
    {
        _analyticsService.Increment("EduWallets");
        return View("~/Views/Pages/edu_wallets.cshtml");
    }

    [Route("/telegram/miniapp-experiment")]
    public IActionResult TelegramMiniAppExperiment()
    {
        _analyticsService.Increment("TelegramMiniAppExperiment");
        return View("~/Views/Pages/telegram_miniapp_experiment.cshtml");
    }

    public IActionResult Privacy()
    {
        _analyticsService.Increment("Privacy");
        return View();
    }

    [Route("/privacy-policy")]
    public IActionResult PrivacyPolicy()
    {
        _analyticsService.Increment("PrivacyPolicy");
        return View("~/Views/Home/Privacy.cshtml");
    }

    [Route("/refund-policy")]
    public IActionResult RefundPolicy()
    {
        _analyticsService.Increment("RefundPolicy");
        return View("~/Views/Pages/refund_policy.cshtml");
    }
    //////////////////////////////////////////////////////////////

    /////////////////////////////////////////////////////
    [Route("FAQ")]
    public IActionResult Faq()
    {
        _analyticsService.Increment("Faq");
        return View("~/Views/Pages/faqs.cshtml");
    }

    [Route("APIConnection")]
    public IActionResult ApiConnection()
    {
        _analyticsService.Increment("APIConnection");
        return View("~/Views/Pages/FAQpages/faq_api_key.cshtml");
    }

    [Route("/account-needed")]
    [Route("AccountNeeded")]
    public IActionResult AccountNeeded()
    {
        _analyticsService.Increment("AccountNeeded");
        return View("~/Views/Pages/accountneeded.cshtml");
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }

    }
