using AutoSignals.Data;
using AutoSignals.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using starterkit.Models;
using System.Diagnostics;

public class AssetsController : Controller
{
    private readonly ILogger<AssetsController> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IAnalyticsService _analyticsService;
    private readonly CandleService _candleService;

    public AssetsController(ILogger<AssetsController> logger, IServiceScopeFactory scopeFactory, IAnalyticsService analyticsService, CandleService candleService)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _analyticsService = analyticsService;
        _candleService = candleService;
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
        var kucoinAssets = await context.KuCoinAssetPrices.AsNoTracking().ToListAsync().ConfigureAwait(false);

        // Helper to build combined view for a specific type (swap / spot)
        object BuildCombinedRow(
            AutoSignals.Models.GeneralAssetPrice g,
            IEnumerable<dynamic> bitget,
            IEnumerable<dynamic> binance,
            IEnumerable<dynamic> bybit,
            IEnumerable<dynamic> okx,
            IEnumerable<dynamic> kucoin)
        {
            var bitgetPrice = bitget.FirstOrDefault(b => b.Symbol == g.Symbol && b.Type == g.Type)?.Price;
            var binancePrice = binance.FirstOrDefault(b => b.Symbol == g.Symbol && b.Type == g.Type)?.Price;
            var bybitPrice = bybit.FirstOrDefault(b => b.Symbol == g.Symbol && b.Type == g.Type)?.Price;
            var okxPrice = okx.FirstOrDefault(b => b.Symbol == g.Symbol && b.Type == g.Type)?.Price;
            var kucoinPrice = kucoin.FirstOrDefault(b => b.Symbol == g.Symbol && b.Type == g.Type)?.Price;

            return new
            {
                Symbol = g.Symbol,
                Type = g.Type,
                GeneralPrice = g.Price,
                GeneralTime = g.Time,
                BitgetPrice = (decimal?)bitgetPrice,
                BinancePrice = (decimal?)binancePrice,
                BybitPrice = (decimal?)bybitPrice,
                OkxPrice = (decimal?)okxPrice,
                KuCoinPrice = (decimal?)kucoinPrice
            };
        }

        var swapGeneral = generalAssets.Where(g => g.Type == "swap").ToList();
        var spotGeneral = generalAssets.Where(g => g.Type == "spot").ToList();

        var swapAssets = swapGeneral
            .Select(g => BuildCombinedRow(g, bitgetAssets, binanceAssets, bybitAssets, okxAssets, kucoinAssets))
            .ToList();

        var spotAssets = spotGeneral
            .Select(g => BuildCombinedRow(g, bitgetAssets, binanceAssets, bybitAssets, okxAssets, kucoinAssets))
            .ToList();

        ViewBag.SwapAssets = swapAssets;
        ViewBag.SpotAssets = spotAssets;

        _analyticsService.Increment("Assets Dashboard");

        return View();
    }

    [Route("/Assets/Candles")]
    public async Task<IActionResult> Candles()
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AutoSignalsDbContext>();

        var symbols = await context.GeneralAssetPrices
            .AsNoTracking()
            .OrderBy(g => g.Symbol)
            .Select(g => new { g.Symbol, g.Type })
            .Distinct()
            .ToListAsync()
            .ConfigureAwait(false);

        ViewBag.SpotSymbols = symbols.Where(s => s.Type == "spot").Select(s => s.Symbol).Distinct().OrderBy(s => s).ToList();
        ViewBag.FuturesSymbols = symbols.Where(s => s.Type == "swap").Select(s => s.Symbol).Distinct().OrderBy(s => s).ToList();

        _analyticsService.Increment("Asset Candles");
        return View();
    }

    [HttpGet("/api/candles")]
    public async Task<IActionResult> GetCandles(
        [FromQuery] string symbol,
        [FromQuery] string type = "swap",
        [FromQuery] string interval = "5m",
        [FromQuery] int limit = 300)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            return BadRequest("symbol is required.");

        if (!CandleService.ValidIntervals.ContainsKey(interval))
            return BadRequest($"Invalid interval. Allowed: {string.Join(", ", CandleService.ValidIntervals.Keys)}");

        var candles = await _candleService.GetCandlesAsync(symbol, type, interval, limit).ConfigureAwait(false);
        return Json(candles);
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

    }
