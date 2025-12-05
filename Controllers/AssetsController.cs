using AutoSignals.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using starterkit.Models;
using System.Diagnostics;


public class AssetsController : Controller
{
    private readonly ILogger<AssetsController> _logger;
    private readonly AutoSignalsDbContext _context;

    public AssetsController(ILogger<AssetsController> logger, AutoSignalsDbContext context)
    {
        _logger = logger;
        _context = context;
    }

    [Route("/Assets/dashboard")]
    public async Task <IActionResult> dashboard()
    {
        var generalAssets = _context.GeneralAssetPrices.ToList();
        var bitgetAssets = _context.BitgetAssetPrices.ToList();
        var binanceAssets = _context.BinanceAssetPrices.ToList();
        var bybitAssets = _context.BybitAssetPrices.ToList();
        var okxAssets = _context.OkxAssetPrices.ToList();
        //var kucoinAssets = _context.KuCoinAssetPrices.ToList();

        var combinedAssets = generalAssets.Select(g => new
        {
            Symbol = g.Symbol,
            GeneralPrice = g.Price,
            GeneralTime = g.Time,
            BitgetPrice = bitgetAssets.FirstOrDefault(b => b.Symbol == g.Symbol)?.Price,
            BinancePrice = binanceAssets.FirstOrDefault(b => b.Symbol == g.Symbol)?.Price,
            BybitPrice = bybitAssets.FirstOrDefault(b => b.Symbol == g.Symbol)?.Price,
            OkxPrice = okxAssets.FirstOrDefault(b => b.Symbol == g.Symbol)?.Price//,
            //KuCoinPrice = kucoinAssets.FirstOrDefault(b => b.Symbol == g.Symbol)?.Price
        }).ToList();

        ViewBag.GeneralAssets = generalAssets;
        ViewBag.BitgetAssets = bitgetAssets;
        ViewBag.BinanceAssets = binanceAssets;
        ViewBag.BybitAssets = bybitAssets;
        ViewBag.BybitAssets = okxAssets;
        //ViewBag.KuCoinAssets = kucoinAssets;
        ViewBag.CombinedAssets = combinedAssets;

        await TrackPageViewAsync("Assets Dashboard");

        return View(ViewBag);
    }

    public IActionResult Privacy()
    {
        return View();
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
