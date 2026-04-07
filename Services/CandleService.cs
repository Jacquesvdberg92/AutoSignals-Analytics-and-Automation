using AutoSignals.Data;
using AutoSignals.Models;
using Microsoft.EntityFrameworkCore;

namespace AutoSignals.Services
{
    /// <summary>
    /// Aggregates stored KLineAssetPrice snapshots (written every 5 min) into OHLCV candles.
    /// Zero extra network requests — all data comes from the local DB.
    /// </summary>
    public class CandleService
    {
        private readonly AutoSignalsDbContext _context;

        // Intervals exposed to callers, mapped to their duration in minutes.
        public static readonly IReadOnlyDictionary<string, int> ValidIntervals =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                { "5m",   5   },
                { "15m",  15  },
                { "30m",  30  },
                { "1h",   60  },
                { "4h",   240 },
                { "1d",   1440 },
                { "1w",   10080 },
            };

        public CandleService(AutoSignalsDbContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Returns up to <paramref name="limit"/> OHLCV candles for the given symbol, type and interval,
        /// aggregated from the raw 5-minute price snapshots stored in KLineAssetPrices.
        /// </summary>
        /// <param name="symbol">Symbol as stored in the DB, e.g. "BTC/USDT".</param>
        /// <param name="type">"spot" or "swap".</param>
        /// <param name="interval">One of the keys in <see cref="ValidIntervals"/>.</param>
        /// <param name="limit">Maximum number of candles to return.</param>
        public async Task<List<CandleDto>> GetCandlesAsync(
            string symbol,
            string type,
            string interval,
            int limit = 300)
        {
            if (!ValidIntervals.TryGetValue(interval, out var intervalMinutes))
                throw new ArgumentException($"Invalid interval '{interval}'.", nameof(interval));

            limit = Math.Clamp(limit, 1, 1500);

            // How far back we need to look to fill `limit` candles
            var since = DateTime.UtcNow.AddMinutes(-(long)intervalMinutes * limit);

            // Pull only the columns we need
            var snapshots = await _context.KLineAssetPrices
                .AsNoTracking()
                .Where(k => k.Symbol == symbol && k.Type == type && k.Time >= since)
                .OrderBy(k => k.Time)
                .Select(k => new { k.Time, k.Price, k.Open, k.High, k.Low, k.Close, k.Volume })
                .ToListAsync()
                .ConfigureAwait(false);

            if (snapshots.Count == 0)
                return new List<CandleDto>();

            var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            // For daily+ candles the stored 24h High/Low (from exchange tickers) give a proper range.
            // For intraday intervals we derive H/L from consecutive Price ticks.
            bool useStoredHl = intervalMinutes >= 1440;

            var candles = snapshots
                .GroupBy(s => FloorToInterval(s.Time, intervalMinutes, epoch))
                .OrderBy(g => g.Key)
                .Select(g =>
                {
                    var prices = g.Select(s => s.Price).ToList();
                    return new CandleDto
                    {
                        Time   = (long)(g.Key - epoch).TotalSeconds,
                        Open   = prices.First(),
                        High   = useStoredHl ? g.Max(s => s.High)  : prices.Max(),
                        Low    = useStoredHl ? g.Min(s => s.Low)   : prices.Min(),
                        Close  = prices.Last(),
                        Volume = g.Sum(s => s.Volume),
                    };
                })
                .TakeLast(limit)
                .ToList();

            return candles;
        }

        private static DateTime FloorToInterval(DateTime dt, int intervalMinutes, DateTime epoch)
        {
            var totalMinutes = (long)(dt.ToUniversalTime() - epoch).TotalMinutes;
            var floored = totalMinutes / intervalMinutes * intervalMinutes;
            return epoch.AddMinutes(floored);
        }
    }
}
