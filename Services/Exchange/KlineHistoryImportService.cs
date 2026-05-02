using AutoSignals.Data;
using AutoSignals.Models;
using Microsoft.EntityFrameworkCore;

namespace AutoSignals.Services
{
    // ── Progress state ────────────────────────────────────────────────────────

    public sealed class BulkImportProgress
    {
        private readonly object _lock = new();

        public bool      IsRunning     { get; private set; }
        public int       Total         { get; private set; }
        public int       Completed     { get; private set; }
        public int       Inserted      { get; private set; }
        public int       Errors        { get; private set; }
        public string    CurrentSymbol { get; private set; } = "";
        public DateTime? StartedAt     { get; private set; }
        public DateTime? FinishedAt    { get; private set; }
        public string?   LastError     { get; private set; }

        public int PercentComplete => Total > 0 ? (int)((double)Completed / Total * 100) : 0;

        internal void Begin()
        {
            lock (_lock)
            {
                IsRunning     = true;
                Total         = 0;
                Completed     = 0;
                Inserted      = 0;
                Errors        = 0;
                CurrentSymbol = "Loading symbols…";
                StartedAt     = DateTime.UtcNow;
                FinishedAt    = null;
                LastError     = null;
            }
        }

        internal void SetTotal(int total)
        {
            lock (_lock) { Total = total; CurrentSymbol = ""; }
        }

        internal void Tick(string symbol, int inserted)
        {
            lock (_lock)
            {
                CurrentSymbol = symbol;
                Completed++;
                Inserted += inserted;
            }
        }

        internal void TickError(string symbol, string error)
        {
            lock (_lock)
            {
                CurrentSymbol = symbol;
                Completed++;
                Errors++;
                LastError = $"{symbol}: {error}";
            }
        }

        internal void Finish()
        {
            lock (_lock)
            {
                IsRunning     = false;
                CurrentSymbol = "";
                FinishedAt    = DateTime.UtcNow;
            }
        }
    }

    // ── Service ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Registered as singleton. Fetches historical OHLCV candles from supported exchanges
    /// via ccxt (public endpoints, no API keys) and bulk-inserts them into KLineAssetPrices.
    /// </summary>
    public class KlineHistoryImportService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly object _startLock = new();

        public static BulkImportProgress BulkProgress { get; } = new();

        // Exchange key → (ccxt client factory, normalized Type for KLineAssetPrices)
        private static readonly IReadOnlyDictionary<string, (Func<ccxt.Exchange> Factory, string Type)> _exchanges =
            new Dictionary<string, (Func<ccxt.Exchange>, string)>(StringComparer.OrdinalIgnoreCase)
            {
                ["binance-spot"] = (
                    () => new ccxt.binance(new Dictionary<string, object> { { "enableRateLimit", true } }),
                    "spot"),

                ["binance-futures"] = (
                    () => new ccxt.binanceusdm(new Dictionary<string, object> { { "enableRateLimit", true } }),
                    "swap"),

                ["bybit-spot"] = (() =>
                {
                    var c = new ccxt.bybit(new Dictionary<string, object> { { "enableRateLimit", true } });
                    c.options["defaultType"] = "spot";
                    return c;
                }, "spot"),

                ["bybit-swap"] = (() =>
                {
                    var c = new ccxt.bybit(new Dictionary<string, object> { { "enableRateLimit", true } });
                    c.options["defaultType"] = "linear";
                    return c;
                }, "swap"),

                ["okx-spot"] = (
                    () => new ccxt.okx(new Dictionary<string, object> { { "enableRateLimit", true } }),
                    "spot"),

                ["okx-swap"] = (() =>
                {
                    var c = new ccxt.okx(new Dictionary<string, object> { { "enableRateLimit", true } });
                    c.options["defaultType"] = "swap";
                    c.options["defaultSettle"] = "USDT";
                    return c;
                }, "swap"),

                ["kucoin"] = (
                    () => new ccxt.kucoin(new Dictionary<string, object> { { "enableRateLimit", true } }),
                    "spot"),

                ["bitget-spot"] = (
                    () => new ccxt.bitget(new Dictionary<string, object> { { "enableRateLimit", true } }),
                    "spot"),

                ["bitget-swap"] = (() =>
                {
                    var c = new ccxt.bitget(new Dictionary<string, object> { { "enableRateLimit", true } });
                    c.options["defaultType"] = "swap";
                    return c;
                }, "swap"),
            };

        // Priority exchange order used by the bulk import for each market type
        private static readonly string[] SpotPriority = ["binance-spot", "bybit-spot", "okx-spot"];
        private static readonly string[] SwapPriority = ["binance-futures", "bybit-swap", "okx-swap"];

        public static IReadOnlyDictionary<string, string> ExchangeLabels { get; } =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["binance-spot"]    = "Binance — Spot",
                ["binance-futures"] = "Binance — Futures (USDT-M)",
                ["bybit-spot"]      = "Bybit — Spot",
                ["bybit-swap"]      = "Bybit — Perpetual",
                ["okx-spot"]        = "OKX — Spot",
                ["okx-swap"]        = "OKX — Perpetual",
                ["kucoin"]          = "KuCoin — Spot",
                ["bitget-spot"]     = "Bitget — Spot",
                ["bitget-swap"]     = "Bitget — Perpetual",
            };

        public KlineHistoryImportService(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        // ── Single-symbol import ──────────────────────────────────────────────

        /// <summary>
        /// Fetches the most recent <paramref name="limit"/> candles (max 1 000) for
        /// <paramref name="symbol"/> at the given <paramref name="interval"/> and inserts
        /// any rows whose timestamp is not already in the database.
        /// </summary>
        /// <returns>Number of new rows inserted.</returns>
        public async Task<int> ImportAsync(
            string exchangeKey,
            string symbol,
            string interval,
            int limit)
        {
            limit = Math.Clamp(limit, 1, 1000);

            if (!_exchanges.TryGetValue(exchangeKey, out var cfg))
                throw new ArgumentException($"Unknown exchange key '{exchangeKey}'.");

            var client = cfg.Factory();
            var type   = cfg.Type;

            var raw = await client.fetchOHLCV(symbol, interval, null, limit)
                          .ConfigureAwait(false) as List<object>;

            if (raw == null || raw.Count == 0)
                return 0;

            var rows = new List<KLineAssetPrice>(raw.Count);
            foreach (var item in raw)
            {
                if (item is not List<object> c || c.Count < 6) continue;

                rows.Add(new KLineAssetPrice
                {
                    Symbol = symbol,
                    Type   = type,
                    Time   = DateTimeOffset.FromUnixTimeMilliseconds(Convert.ToInt64(c[0])).UtcDateTime,
                    Open   = Convert.ToDecimal(c[1]),
                    High   = Convert.ToDecimal(c[2]),
                    Low    = Convert.ToDecimal(c[3]),
                    Close  = Convert.ToDecimal(c[4]),
                    Price  = Convert.ToDecimal(c[4]),
                    Volume = Convert.ToDecimal(c[5]),
                });
            }

            if (rows.Count == 0)
                return 0;

            using var scope   = _scopeFactory.CreateScope();
            var context       = scope.ServiceProvider.GetRequiredService<AutoSignalsDbContext>();
            var minTime       = rows.Min(r => r.Time);

            var existingTimes = new HashSet<DateTime>(
                await context.KLineAssetPrices
                    .AsNoTracking()
                    .Where(k => k.Symbol == symbol && k.Type == type && k.Time >= minTime)
                    .Select(k => k.Time)
                    .ToListAsync()
                    .ConfigureAwait(false));

            var newRows = rows.Where(r => !existingTimes.Contains(r.Time)).ToList();
            if (newRows.Count == 0)
                return 0;

            await context.KLineAssetPrices.AddRangeAsync(newRows).ConfigureAwait(false);
            await context.SaveChangesAsync().ConfigureAwait(false);
            return newRows.Count;
        }

        // ── Bulk import ───────────────────────────────────────────────────────

        /// <summary>
        /// Fires a background task that fetches 1 000 × 15m candles for every active symbol
        /// in GeneralAssetPrices. Uses Binance → Bybit → OKX as priority order.
        /// No-op if a bulk import is already running.
        /// </summary>
        /// <returns>True if the job was started; false if one was already running.</returns>
        public bool StartBulkImport()
        {
            lock (_startLock)
            {
                if (BulkProgress.IsRunning) return false;
                BulkProgress.Begin();
            }

            _ = Task.Run(() => RunBulkImportAsync(default));
            return true;
        }

        private async Task RunBulkImportAsync(CancellationToken ct)
        {
            try
            {
                // Load all active symbols from the DB
                using var scope   = _scopeFactory.CreateScope();
                var context       = scope.ServiceProvider.GetRequiredService<AutoSignalsDbContext>();

                var symbols = await context.GeneralAssetPrices
                    .AsNoTracking()
                    .Select(g => new { g.Symbol, g.Type })
                    .Distinct()
                    .OrderBy(g => g.Symbol)
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                BulkProgress.SetTotal(symbols.Count);

                foreach (var sym in symbols)
                {
                    if (ct.IsCancellationRequested) break;

                    var priority = sym.Type == "spot" ? SpotPriority : SwapPriority;
                    var ok       = false;

                    foreach (var exKey in priority)
                    {
                        try
                        {
                            var inserted = await ImportAsync(exKey, sym.Symbol, "15m", 1000)
                                               .ConfigureAwait(false);
                            BulkProgress.Tick(sym.Symbol, inserted);
                            ok = true;
                            break;
                        }
                        catch
                        {
                            // try next exchange in priority list
                        }
                    }

                    if (!ok)
                        BulkProgress.TickError(sym.Symbol, "all exchanges failed");
                }
            }
            catch (Exception ex)
            {
                BulkProgress.TickError("(fatal)", ex.Message);
            }
            finally
            {
                BulkProgress.Finish();
            }
        }
    }
}
