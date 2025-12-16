using AutoSignals.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using starterkit.Models;
using System.Diagnostics;

public class AssetsController : Controller
{
    private readonly ILogger<AssetsController> _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    public AssetsController(ILogger<AssetsController> logger, IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    [Route("/Assets/dashboard")]
    public async Task<IActionResult> dashboard()
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AutoSignalsDbContext>();

        // Load all prices once
        var generalAssets = await context.GeneralAssetPrices.AsNoTracking().ToListAsync().ConfigureAwait(false);
        var bitgetAssets = await context.BitgetAssetPrices.AsNoTracking().ToListAsync().ConfigureAwait(false);
        var binanceAssets = await context.BinanceAssetPrices.AsNoTracking().ToListAsync().ConfigureAwait(false);
        var bybitAssets = await context.BybitAssetPrices.AsNoTracking().ToListAsync().ConfigureAwait(false);
        var okxAssets = await context.OkxAssetPrices.AsNoTracking().ToListAsync().ConfigureAwait(false);
        //var kucoinAssets = await context.KuCoinAssetPrices.AsNoTracking().ToListAsync().ConfigureAwait(false);

        // Helper to build combined view for a specific type (swap / spot)
        object BuildCombinedRow(
            AutoSignals.Models.GeneralAssetPrice g,
            IEnumerable<dynamic> bitget,
            IEnumerable<dynamic> binance,
            IEnumerable<dynamic> bybit,
            IEnumerable<dynamic> okx)
        {
            var bitgetPrice = bitget.FirstOrDefault(b => b.Symbol == g.Symbol && b.Type == g.Type)?.Price;
            var binancePrice = binance.FirstOrDefault(b => b.Symbol == g.Symbol && b.Type == g.Type)?.Price;
            var bybitPrice = bybit.FirstOrDefault(b => b.Symbol == g.Symbol && b.Type == g.Type)?.Price;
            var okxPrice = okx.FirstOrDefault(b => b.Symbol == g.Symbol && b.Type == g.Type)?.Price;
            //var kucoinPrice = kucoin.FirstOrDefault(b => b.Symbol == g.Symbol && b.Type == g.Type)?.Price;

            return new
            {
                Symbol = g.Symbol,
                Type = g.Type,
                GeneralPrice = g.Price,
                GeneralTime = g.Time,
                BitgetPrice = (decimal?)bitgetPrice,
                BinancePrice = (decimal?)binancePrice,
                BybitPrice = (decimal?)bybitPrice,
                OkxPrice = (decimal?)okxPrice
                //KuCoinPrice = (decimal?)kucoinPrice
            };
        }

        var swapGeneral = generalAssets.Where(g => g.Type == "swap").ToList();
        var spotGeneral = generalAssets.Where(g => g.Type == "spot").ToList();

        var swapAssets = swapGeneral
            .Select(g => BuildCombinedRow(g, bitgetAssets, binanceAssets, bybitAssets, okxAssets))
            .ToList();

        var spotAssets = spotGeneral
            .Select(g => BuildCombinedRow(g, bitgetAssets, binanceAssets, bybitAssets, okxAssets))
            .ToList();

        ViewBag.SwapAssets = swapAssets;
        ViewBag.SpotAssets = spotAssets;

        await TrackPageViewAsync(context, "Assets Dashboard").ConfigureAwait(false);

        return View();
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

    private async Task TrackPageViewAsync(AutoSignalsDbContext context, string pageName)
    {
        var today = DateTime.UtcNow.Date;
        var analytics = await context.Set<AutoSignals.Models.Analytics>()
            .FirstOrDefaultAsync(a => a.PageName == pageName && a.Date == today)
            .ConfigureAwait(false);

        if (analytics == null)
        {
            analytics = new AutoSignals.Models.Analytics
            {
                PageName = pageName,
                Date = today,
                Views = 1
            };
            context.Add(analytics);
        }
        else
        {
            analytics.Views += 1;
            context.Update(analytics);
        }

        await context.SaveChangesAsync().ConfigureAwait(false);
    }
}
