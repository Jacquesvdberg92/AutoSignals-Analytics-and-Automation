using AutoSignals.Data;
using AutoSignals.Models;
using AutoSignals.Models.Bots;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace AutoSignals.Services.Bots
{
    /// <summary>
    /// Tick-driven engine for Arbitrage Scanner bots. Registered as a singleton;
    /// uses IServiceScopeFactory for all scoped DB access.
    ///
    /// Phase 1: read-only scan-and-alert.
    ///   - Reads latest SPOT prices from all five per-exchange price tables (futures excluded —
    ///     futures positions cannot be transferred between exchanges for arbitrage)
    ///   - Groups by Symbol, finds max/min price across exchanges
    ///   - If netSpread >= MinSpreadPercent and not in cooldown → persist ArbitrageOpportunity + Telegram alert
    ///   - Prunes to last 500 rows per scanner after each tick
    ///   - No exchange writes in Phase 1
    /// </summary>
    public class ArbitrageScannerEngine : IBotEngine
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<ArbitrageScannerEngine> _logger;

        public BotType SupportedBotType => BotType.ArbitrageScanner;

        // Rolling log cap per scanner
        private const int MaxOpportunitiesPerScanner = 500;

        public ArbitrageScannerEngine(IServiceScopeFactory scopeFactory, ILogger<ArbitrageScannerEngine> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public async Task TickAsync(BotBase botBase, CancellationToken ct)
        {
            if (botBase is not ArbitrageScannerBot bot) return;
            if (bot.Status != BotStatus.Running) return;

            List<string> symbols;
            try
            {
                symbols = JsonSerializer.Deserialize<List<string>>(bot.WatchedSymbolsJson ?? "[]") ?? new();
            }
            catch
            {
                _logger.LogWarning("ArbitrageScannerEngine: Invalid WatchedSymbolsJson for bot {BotId}.", bot.Id);
                symbols = new();
            }

            if (symbols.Count == 0)
            {
                _logger.LogDebug("ArbitrageScannerEngine: Bot {BotId} has no watched symbols. Skipping tick.", bot.Id);
                return;
            }

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AutoSignalsDbContext>();

                // Build a unified price snapshot: Dictionary<symbol, List<(exchangeName, price)>>
                var snapshot = await BuildPriceSnapshotAsync(db, symbols, ct);

                int newOpportunities = 0;

                foreach (var (symbol, prices) in snapshot)
                {
                    if (prices.Count < 2) continue;

                    var maxEntry = prices.MaxBy(p => p.Price)!;
                    var minEntry = prices.MinBy(p => p.Price)!;

                    if (minEntry.Price <= 0) continue;

                    var spreadPct = (maxEntry.Price - minEntry.Price) / minEntry.Price * 100m;
                    var netSpread = spreadPct - bot.EstimatedFeePercent * 2m;

                    if (netSpread < bot.MinSpreadPercent) continue;

                    // Cooldown check — global per-bot (not per-symbol; conservative for Phase 1)
                    if (bot.LastAlertAt.HasValue
                        && DateTime.UtcNow < bot.LastAlertAt.Value.AddMinutes(bot.AlertCooldownMinutes))
                    {
                        _logger.LogDebug("ArbitrageScannerEngine: Bot {BotId} in cooldown. Opportunity suppressed for {Symbol}.", bot.Id, symbol);
                        continue;
                    }

                    var opportunity = new ArbitrageOpportunity
                    {
                        ScannerId = bot.Id,
                        Symbol = symbol,
                        BuyExchange = minEntry.Exchange,
                        SellExchange = maxEntry.Exchange,
                        BuyPrice = minEntry.Price,
                        SellPrice = maxEntry.Price,
                        SpreadPercent = spreadPct,
                        NetSpreadPercent = netSpread,
                        DetectedAt = DateTime.UtcNow,
                        Alerted = false
                    };

                    db.ArbitrageOpportunities.Add(opportunity);
                    bot.TotalOpportunitiesFound++;
                    bot.LastAlertAt = DateTime.UtcNow;
                    bot.UpdatedAt = DateTime.UtcNow;
                    newOpportunities++;

                    await db.SaveChangesAsync(ct);

                    opportunity.Alerted = true;
                    await db.SaveChangesAsync(ct);

                    // Fire Telegram alert
                    await SendTelegramAsync(bot, opportunity, scope, ct);
                }

                if (newOpportunities > 0)
                    _logger.LogInformation("ArbitrageScannerEngine: Bot {BotId} found {Count} opportunities this tick.", bot.Id, newOpportunities);

                // Prune to last MaxOpportunitiesPerScanner rows
                await PruneOpportunitiesAsync(db, bot.Id, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ArbitrageScannerEngine: Tick failed for bot {BotId}.", bot.Id);
                throw; // Rethrown so BotEngineHostedService can set Status = Error
            }

            bot.LastRunAt = DateTime.UtcNow;
            bot.UpdatedAt = DateTime.UtcNow;
        }

        // ── Price snapshot ────────────────────────────────────────────────────────

        private record ExchangePrice(string Symbol, string Exchange, decimal Price);

        /// <summary>
        /// Queries all five per-exchange price tables and returns the latest price
        /// per (symbol, exchange) pair for the requested symbols.
        /// </summary>
        private static async Task<Dictionary<string, List<(string Exchange, decimal Price)>>> BuildPriceSnapshotAsync(
            AutoSignalsDbContext db, List<string> symbols, CancellationToken ct)
        {
            var result = new Dictionary<string, List<(string, decimal)>>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in symbols) result[s] = new();

            var allPrices = new List<ExchangePrice>();
            allPrices.AddRange(await FetchBitgetAsync(db, symbols, ct));
            allPrices.AddRange(await FetchBinanceAsync(db, symbols, ct));
            allPrices.AddRange(await FetchBybitAsync(db, symbols, ct));
            allPrices.AddRange(await FetchOkxAsync(db, symbols, ct));
            allPrices.AddRange(await FetchKuCoinAsync(db, symbols, ct));

            foreach (var ep in allPrices)
            {
                if (result.TryGetValue(ep.Symbol, out var list))
                    list.Add((ep.Exchange, ep.Price));
            }

            return result;
        }

        private static async Task<List<ExchangePrice>> FetchBitgetAsync(AutoSignalsDbContext db, List<string> symbols, CancellationToken ct)
        {
            try
            {
                var rows = await db.BitgetAssetPrices
                    .Where(p => symbols.Contains(p.Symbol) && p.Type == "spot")
                    .GroupBy(p => p.Symbol)
                    .Select(g => new { Symbol = g.Key, Price = g.OrderByDescending(p => p.Time).First().Price })
                    .ToListAsync(ct);
                return rows.Select(r => new ExchangePrice(r.Symbol, "Bitget", r.Price)).ToList();
            }
            catch { return new(); }
        }

        private static async Task<List<ExchangePrice>> FetchBinanceAsync(AutoSignalsDbContext db, List<string> symbols, CancellationToken ct)
        {
            try
            {
                var rows = await db.BinanceAssetPrices
                    .Where(p => symbols.Contains(p.Symbol) && p.Type == "spot")
                    .GroupBy(p => p.Symbol)
                    .Select(g => new { Symbol = g.Key, Price = g.OrderByDescending(p => p.Time).First().Price })
                    .ToListAsync(ct);
                return rows.Select(r => new ExchangePrice(r.Symbol, "Binance", r.Price)).ToList();
            }
            catch { return new(); }
        }

        private static async Task<List<ExchangePrice>> FetchBybitAsync(AutoSignalsDbContext db, List<string> symbols, CancellationToken ct)
        {
            try
            {
                var rows = await db.BybitAssetPrices
                    .Where(p => symbols.Contains(p.Symbol) && p.Type == "spot")
                    .GroupBy(p => p.Symbol)
                    .Select(g => new { Symbol = g.Key, Price = g.OrderByDescending(p => p.Time).First().Price })
                    .ToListAsync(ct);
                return rows.Select(r => new ExchangePrice(r.Symbol, "Bybit", r.Price)).ToList();
            }
            catch { return new(); }
        }

        private static async Task<List<ExchangePrice>> FetchOkxAsync(AutoSignalsDbContext db, List<string> symbols, CancellationToken ct)
        {
            try
            {
                var rows = await db.OkxAssetPrices
                    .Where(p => symbols.Contains(p.Symbol) && p.Type == "spot")
                    .GroupBy(p => p.Symbol)
                    .Select(g => new { Symbol = g.Key, Price = g.OrderByDescending(p => p.Time).First().Price })
                    .ToListAsync(ct);
                return rows.Select(r => new ExchangePrice(r.Symbol, "OKX", r.Price)).ToList();
            }
            catch { return new(); }
        }

        private static async Task<List<ExchangePrice>> FetchKuCoinAsync(AutoSignalsDbContext db, List<string> symbols, CancellationToken ct)
        {
            try
            {
                var rows = await db.KuCoinAssetPrices
                    .Where(p => symbols.Contains(p.Symbol) && p.Type == "spot")
                    .GroupBy(p => p.Symbol)
                    .Select(g => new { Symbol = g.Key, Price = g.OrderByDescending(p => p.Time).First().Price })
                    .ToListAsync(ct);
                return rows.Select(r => new ExchangePrice(r.Symbol, "KuCoin", r.Price)).ToList();
            }
            catch { return new(); }
        }

        // ── Pruning ───────────────────────────────────────────────────────────────

        private static async Task PruneOpportunitiesAsync(AutoSignalsDbContext db, int scannerId, CancellationToken ct)
        {
            var count = await db.ArbitrageOpportunities
                .CountAsync(o => o.ScannerId == scannerId, ct);

            if (count <= MaxOpportunitiesPerScanner) return;

            var toDelete = count - MaxOpportunitiesPerScanner;
            var oldest = await db.ArbitrageOpportunities
                .Where(o => o.ScannerId == scannerId)
                .OrderBy(o => o.DetectedAt)
                .Take(toDelete)
                .ToListAsync(ct);

            db.ArbitrageOpportunities.RemoveRange(oldest);
            await db.SaveChangesAsync(ct);
        }

        // ── Telegram ──────────────────────────────────────────────────────────────

        private async Task SendTelegramAsync(ArbitrageScannerBot bot, ArbitrageOpportunity opp, IServiceScope scope, CancellationToken ct)
        {
            try
            {
                var notifier = scope.ServiceProvider.GetRequiredService<ITelegramNotifier>();
                var html =
                    $"📊 <b>Arbitrage Opportunity!</b>\n" +
                    $"Symbol: <code>{opp.Symbol}</code>\n" +
                    $"Buy on <b>{opp.BuyExchange}</b> @ {opp.BuyPrice:F4}\n" +
                    $"Sell on <b>{opp.SellExchange}</b> @ {opp.SellPrice:F4}\n" +
                    $"Spread: <b>{opp.SpreadPercent:F3}%</b> | Net: <b>{opp.NetSpreadPercent:F3}%</b>";

                await notifier.SendDirectMessageToUserAsync(bot.UserId, html, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "ArbitrageScannerEngine: Telegram notification failed for bot {BotId}.", bot.Id);
            }
        }
    }
}
