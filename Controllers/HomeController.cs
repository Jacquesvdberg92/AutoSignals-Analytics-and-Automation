using AutoSignals.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using starterkit.Models;
using System.Diagnostics;

namespace starterkit.Controllers;

public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;
    private readonly AutoSignalsDbContext _context;

    public HomeController(ILogger<HomeController> logger, AutoSignalsDbContext context)
    {
        _logger = logger;
        _context = context;
    }

    [Route("/")]

    [Route("/index")]
    public async Task<IActionResult> index()
    {
        await TrackPageViewAsync("Landing Page");
        return View(); //empty the brackets to lod defualt Index
    }

    public async Task<IActionResult> ComingSoon()
    {
        await TrackPageViewAsync("ComingSoon");
        return View("~/Views/Shared/comingsoon.cshtml");
    }

    [Route("/terms-conditions")]
    public async Task<IActionResult> TermsConditions()
    {
        await TrackPageViewAsync("TermsConditions");
        return View("~/Views/Pages/terms_conditions.cshtml");
    }
    //////////////////////////////////////////////////////


    //////////////////////////////////////////////////////
    [Route("/pricing")]
    public async Task<IActionResult> Pricing()
    {
        await TrackPageViewAsync("Pricing");
        return View("~/Views/Pages/pricing.cshtml");
    }

    [Route("/comingsoon")]
    public async Task<IActionResult> Comingsoon()
    {
        await TrackPageViewAsync("Comingsoon");
        return View("~/Views/Pages/comingsoon.cshtml");
    }
    //////////////////////////////////////////////////////

    //////////////////////////////////////////////////////
    [Route("/education/basics")]
    public async Task<IActionResult> EduBasics()
    {
        await TrackPageViewAsync("EduBasics");
        return View("~/Views/Pages/edu_basics.cshtml");
    }

    [Route("/education/common-strategies")]
    public async Task<IActionResult> EduCommonStrategies()
    {
        await TrackPageViewAsync("EduCommonStrategies");
        return View("~/Views/Pages/edu_common_stratagies.cshtml");
    }

    [Route("/education/fundamental-analysis")]
    public async Task<IActionResult> EduFA()
    {
        await TrackPageViewAsync("EduFA");
        return View("~/Views/Pages/edu_fa.cshtml");
    }

    [Route("/education/leverage")]
    public async Task<IActionResult> EduLeverage()
    {
        await TrackPageViewAsync("EduLeverage");
        return View("~/Views/Pages/edu_leverage.cshtml");
    }

    [Route("/education/risk-management")]
    public async Task<IActionResult> EduRiskManagement()
    {
        await TrackPageViewAsync("EduRiskManagement");
        return View("~/Views/Pages/edu_risk_management.cshtml");
    }

    [Route("/education/technical-analysis")]
    public async Task<IActionResult> EduTA()
    {
        await TrackPageViewAsync("EduTA");
        return View("~/Views/Pages/edu_ta.cshtml");
    }

    [Route("/education/volatility")]
    public async Task<IActionResult> EduVolatility()
    {
        await TrackPageViewAsync("EduVolatility");
        return View("~/Views/Pages/edu_volitility.cshtml");
    }

    [Route("/education/wallets")]
    public async Task<IActionResult> EduWallets()
    {
        await TrackPageViewAsync("EduWallets");
        return View("~/Views/Pages/edu_wallets.cshtml");
    }

    public async Task <IActionResult> Privacy()
    {
        await TrackPageViewAsync("Privacy");
        return View();
    }
    //////////////////////////////////////////////////////////////

    /////////////////////////////////////////////////////
    [Route("FAQ")]
    public async Task<IActionResult> Faq()
    {
        await TrackPageViewAsync("Faq");
        return View("~/Views/Pages/faqs.cshtml");
    }

    [Route("APIConnection")]
    public async Task<IActionResult> ApiConnection()
    {
        await TrackPageViewAsync("APIConnection");
        return View("~/Views/Pages/FAQpages/faq_api_key.cshtml");
    }

    [Route("/account-needed")]
    [Route("AccountNeeded")]
    public async Task<IActionResult> AccountNeeded()
    {
        await TrackPageViewAsync("AccountNeeded");
        return View("~/Views/Pages/accountneeded.cshtml");
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }

    private async Task TrackPageViewAsync(string pageName)
    {
        var today = DateTime.UtcNow.Date;
        var analytics = await _context.Set<AutoSignals.Models.Analytics>()
            .FirstOrDefaultAsync(a => a.PageName == pageName && a.Date == today);

        if (analytics == null)
        {
            analytics = new AutoSignals.Models.Analytics
            {
                PageName = pageName,
                Date = today,
                Views = 1
            };
            _context.Add(analytics);
        }
        else
        {
            analytics.Views += 1;
            _context.Update(analytics);
        }

        await _context.SaveChangesAsync();
    }
}
