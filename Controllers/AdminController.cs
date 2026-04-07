using AutoSignals.Data;
using AutoSignals.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AutoSignals.Controllers
{
    [Authorize(Roles = "Admin")]
    public class AdminController : Controller
    {
        private readonly AdminSettingService _adminSettingService;
        private readonly AutoSignalsDbContext _context;
        private readonly KlineHistoryImportService _klineImport;

        public AdminController(
            AdminSettingService adminSettingService,
            AutoSignalsDbContext context,
            KlineHistoryImportService klineImport)
        {
            _adminSettingService = adminSettingService;
            _context = context;
            _klineImport = klineImport;
        }

        [HttpGet("/Admin/KlineSettings")]
        public async Task<IActionResult> KlineSettings()
        {
            ViewBag.KlineChartsEnabled = await _adminSettingService.IsEnabledAsync("KlineChartsEnabled");
            ViewBag.RowCount          = await _context.KLineAssetPrices.CountAsync();
            ViewBag.SymbolCount       = await _context.KLineAssetPrices
                                            .Select(k => new { k.Symbol, k.Type })
                                            .Distinct()
                                            .CountAsync();
            ViewBag.OldestSnapshot    = await _context.KLineAssetPrices
                                            .OrderBy(k => k.Time)
                                            .Select(k => (DateTime?)k.Time)
                                            .FirstOrDefaultAsync();
            ViewBag.NewestSnapshot    = await _context.KLineAssetPrices
                                            .OrderByDescending(k => k.Time)
                                            .Select(k => (DateTime?)k.Time)
                                            .FirstOrDefaultAsync();
            return View();
        }

        [HttpPost("/Admin/KlineSettings")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> KlineSettingsToggle(bool enabled)
        {
            await _adminSettingService.SetAsync("KlineChartsEnabled", enabled ? "true" : "false");
            TempData["Success"] = $"Kline data collection {(enabled ? "enabled" : "disabled")}.";
            return RedirectToAction(nameof(KlineSettings));
        }

        [HttpPost("/Admin/KlineImport")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> KlineImport(string exchange, string symbol, string interval, int limit)
        {
            try
            {
                var inserted = await _klineImport.ImportAsync(exchange, symbol.Trim(), interval, limit);
                TempData["Success"] = inserted > 0
                    ? $"Imported {inserted:N0} new {interval} candles for {symbol} from {KlineHistoryImportService.ExchangeLabels[exchange]}."
                    : $"No new candles to import — all {interval} data for {symbol} is already up to date.";
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Import failed: {ex.Message}";
            }

            return RedirectToAction(nameof(KlineSettings));
        }

        [HttpPost("/Admin/KlineBulkImport")]
        [ValidateAntiForgeryToken]
        public IActionResult KlineBulkImport()
        {
            var started = _klineImport.StartBulkImport();
            if (!started)
                TempData["Error"] = "A bulk import is already running.";

            return RedirectToAction(nameof(KlineSettings));
        }

        [HttpGet("/Admin/KlineBulkImportStatus")]
        public IActionResult KlineBulkImportStatus()
        {
            var p = KlineHistoryImportService.BulkProgress;
            return Json(new
            {
                isRunning      = p.IsRunning,
                total          = p.Total,
                completed      = p.Completed,
                inserted       = p.Inserted,
                errors         = p.Errors,
                percentComplete = p.PercentComplete,
                currentSymbol  = p.CurrentSymbol,
                startedAt      = p.StartedAt,
                finishedAt     = p.FinishedAt,
                lastError      = p.LastError,
            });
        }
    }
}
