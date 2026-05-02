using AutoSignals.Data;
using AutoSignals.Models;
using Microsoft.EntityFrameworkCore;

namespace AutoSignals.Services
{
    /// <summary>
    /// Background service that runs a full historical Kline import once per night,
    /// firing between 02:00 and 05:00 Kyiv time (Europe/Kyiv).
    /// Each symbol is tried against every configured exchange so no gaps remain.
    /// </summary>
    public class KlineNightlyImportHostedService : BackgroundService
    {
        private static readonly TimeZoneInfo KyivTz =
            TimeZoneInfo.FindSystemTimeZoneById(
                OperatingSystem.IsWindows() ? "FLE Standard Time" : "Europe/Kiev");

        // Window: fire at 02:00 Kyiv, abort if still running at 05:00 Kyiv
        private static readonly TimeSpan WindowStart = new(2, 0, 0);
        private static readonly TimeSpan WindowEnd   = new(5, 0, 0);

        private static readonly string[] SpotExchanges  = ["binance-spot", "bybit-spot",    "okx-spot",  "kucoin",     "bitget-spot"];
        private static readonly string[] SwapExchanges  = ["binance-futures", "bybit-swap", "okx-swap", "bitget-swap"];

        private readonly IServiceScopeFactory               _scopeFactory;
        private readonly KlineHistoryImportService          _importService;
        private readonly ILogger<KlineNightlyImportHostedService> _logger;

        public KlineNightlyImportHostedService(
            IServiceScopeFactory scopeFactory,
            KlineHistoryImportService importService,
            ILogger<KlineNightlyImportHostedService> logger)
        {
            _scopeFactory  = scopeFactory;
            _importService = importService;
            _logger        = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("KlineNightlyImportHostedService started. " +
                "Will run nightly between {Start}–{End} Kyiv time.", WindowStart, WindowEnd);

            while (!stoppingToken.IsCancellationRequested)
            {
                var delay = TimeUntilNextWindow();
                _logger.LogInformation("Next nightly Kline import in {Delay:hh\\:mm\\:ss}.", delay);

                try
                {
                    await Task.Delay(delay, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                // Use a linked token so the job aborts at 05:00 Kyiv automatically
                using var windowCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                windowCts.CancelAfter(WindowEnd - WindowStart);   // 3-hour hard deadline

                _logger.LogInformation("Nightly Kline import starting.");
                try
                {
                    await RunNightlyImportAsync(windowCts.Token);
                    _logger.LogInformation("Nightly Kline import finished.");
                }
                catch (OperationCanceledException)
                {
                    _logger.LogWarning("Nightly Kline import cancelled (window closed or app shutting down).");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Nightly Kline import encountered an unhandled error.");
                }

                // Small back-off so we don't re-trigger immediately if the job finishes in <1 s
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken).ConfigureAwait(false);
            }
        }

        // ── Core logic ────────────────────────────────────────────────────────

        private async Task RunNightlyImportAsync(CancellationToken ct)
        {
            using var scope   = _scopeFactory.CreateScope();
            var context       = scope.ServiceProvider.GetRequiredService<AutoSignalsDbContext>();

            var symbols = await context.GeneralAssetPrices
                .AsNoTracking()
                .Select(g => new { g.Symbol, g.Type })
                .Distinct()
                .OrderBy(g => g.Symbol)
                .ToListAsync(ct)
                .ConfigureAwait(false);

            _logger.LogInformation("Nightly Kline import: {Count} symbols to process.", symbols.Count);

            int totalInserted = 0;
            int errors        = 0;

            foreach (var sym in symbols)
            {
                if (ct.IsCancellationRequested) break;

                var exchanges = sym.Type == "spot" ? SpotExchanges : SwapExchanges;
                bool success  = false;

                foreach (var exKey in exchanges)
                {
                    if (ct.IsCancellationRequested) break;
                    try
                    {
                        var inserted = await _importService
                            .ImportAsync(exKey, sym.Symbol, "15m", 1000)
                            .ConfigureAwait(false);

                        totalInserted += inserted;
                        success        = true;

                        if (inserted > 0)
                            _logger.LogDebug("Nightly import: {Symbol} via {Exchange} → {Inserted} rows.",
                                sym.Symbol, exKey, inserted);

                        break; // got data — move to next symbol
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug("Nightly import: {Symbol} via {Exchange} failed: {Msg}",
                            sym.Symbol, exKey, ex.Message);
                    }
                }

                if (!success)
                {
                    errors++;
                    _logger.LogWarning("Nightly import: {Symbol} failed on all exchanges.", sym.Symbol);
                }
            }

            _logger.LogInformation(
                "Nightly Kline import complete. Inserted={Inserted}, Errors={Errors}/{Total}.",
                totalInserted, errors, symbols.Count);
        }

        // ── Scheduling helpers ────────────────────────────────────────────────

        /// <summary>Returns the delay until the next 02:00 Kyiv window opens.</summary>
        private static TimeSpan TimeUntilNextWindow()
        {
            var nowKyiv   = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, KyivTz);
            var todayOpen = nowKyiv.Date + WindowStart;

            // If we are already past today's window start, aim for tomorrow
            var nextOpen  = nowKyiv < todayOpen ? todayOpen : todayOpen.AddDays(1);

            var nextOpenUtc = TimeZoneInfo.ConvertTimeToUtc(nextOpen, KyivTz);
            var delay       = nextOpenUtc - DateTime.UtcNow;

            return delay < TimeSpan.Zero ? TimeSpan.Zero : delay;
        }
    }
}
