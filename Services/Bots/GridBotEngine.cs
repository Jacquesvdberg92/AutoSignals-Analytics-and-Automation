using AutoSignals.Data;
using AutoSignals.Models;
using AutoSignals.Models.Bots;
using AutoSignals.Services.ExchangeAdapters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AutoSignals.Services.Bots
{
    /// <summary>
    /// Tick-driven engine for Grid bots. Registered as a singleton; uses IServiceScopeFactory
    /// for all scoped DB and adapter access.
    ///
    /// On first tick after start:
    ///   - Calculates price levels (arithmetic or geometric spacing)
    ///   - Places buy limit orders below current price and sell limit orders above
    ///
    /// On subsequent ticks:
    ///   - Calls GetOpenOrdersAsync to detect filled orders
    ///   - For each newly filled buy: places a sell one grid level up
    ///   - For each newly filled sell: places a buy one grid level down
    ///   - Checks breakout conditions
    /// </summary>
    public class GridBotEngine : IBotEngine
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly AesEncryptionService _encryption;
        private readonly ILogger<GridBotEngine> _logger;

        public BotType SupportedBotType => BotType.Grid;

        public GridBotEngine(IServiceScopeFactory scopeFactory, AesEncryptionService encryption, ILogger<GridBotEngine> logger)
        {
            _scopeFactory = scopeFactory;
            _encryption = encryption;
            _logger = logger;
        }

        // ── Main tick ─────────────────────────────────────────────────────────────

        public async Task TickAsync(BotBase botBase, CancellationToken ct)
        {
            if (botBase is not GridBot bot) return;
            if (bot.Status != BotStatus.Running) return;

            var currentPrice = await GetCurrentPriceAsync(bot.Symbol, ct);
            if (currentPrice <= 0m)
            {
                _logger.LogWarning("GridBotEngine: No price for {Symbol} (BotId={BotId}). Skipping.", bot.Symbol, bot.Id);
                return;
            }

            // Check breakout conditions first
            if (bot.StopOnLowerBreakout && currentPrice < bot.LowerPrice)
            {
                _logger.LogInformation("GridBotEngine: Lower breakout detected for bot {BotId}. Stopping.", bot.Id);
                await CancelAllGridOrdersAsync(bot, ct);
                bot.Status = BotStatus.Completed;
                bot.UpdatedAt = DateTime.UtcNow;
                await SendTelegramAsync(bot, $"⛔ <b>Grid Bot: Lower breakout</b>\nSymbol: <code>{bot.Symbol}</code>\nPrice {currentPrice:F4} dropped below lower bound {bot.LowerPrice:F4}. Bot stopped.", ct);
                return;
            }

            if (bot.StopOnUpperBreakout && currentPrice > bot.UpperPrice)
            {
                _logger.LogInformation("GridBotEngine: Upper breakout detected for bot {BotId}. Stopping.", bot.Id);
                await CancelAllGridOrdersAsync(bot, ct);
                bot.Status = BotStatus.Completed;
                bot.UpdatedAt = DateTime.UtcNow;
                await SendTelegramAsync(bot, $"⛔ <b>Grid Bot: Upper breakout</b>\nSymbol: <code>{bot.Symbol}</code>\nPrice {currentPrice:F4} rose above upper bound {bot.UpperPrice:F4}. Bot stopped.", ct);
                return;
            }

            if (!bot.GridInitialised)
            {
                await InitialiseGridAsync(bot, currentPrice, ct);
                bot.GridInitialised = true;
                bot.UpdatedAt = DateTime.UtcNow;
                await SendTelegramAsync(bot, $"🤖 <b>Grid Bot started</b>\nSymbol: <code>{bot.Symbol}</code>\nRange: {bot.LowerPrice:F4} – {bot.UpperPrice:F4}\nLevels: {bot.GridCount} | Mode: {bot.GridMode}{(bot.IsTest ? "\n⚠️ TEST MODE" : "")}", ct);
            }
            else
            {
                await SyncGridOrdersAsync(bot, ct);
            }

            bot.LastRunAt = DateTime.UtcNow;
            bot.UpdatedAt = DateTime.UtcNow;
        }

        // ── Grid initialisation ───────────────────────────────────────────────────

        private async Task InitialiseGridAsync(GridBot bot, decimal currentPrice, CancellationToken ct)
        {
            var levels = CalculatePriceLevels(bot);

            _logger.LogInformation("GridBotEngine: Initialising {Count} grid levels for bot {BotId}.", levels.Count, bot.Id);

            decimal totalInvested = 0m;
            foreach (var level in levels)
            {
                string side = level < currentPrice ? "buy" : "sell";
                var order = BuildOrder(bot, side, "limit", level, bot.OrderSizeUsd / level,
                    description: $"Grid {side} @ {level:F4} (bot {bot.Id})");

                await SendOrderAsync(bot, order, side, ct);
                totalInvested += bot.OrderSizeUsd;
            }

            bot.TotalInvested = totalInvested;
        }

        // ── Grid sync ─────────────────────────────────────────────────────────────

        private async Task SyncGridOrdersAsync(GridBot bot, CancellationToken ct)
        {
            if (bot.IsTest) return; // Test mode: orders are immediately "filled" on placement

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AutoSignalsDbContext>();
                var (adapter, creds) = await ResolveAdapterAsync(bot, scope, ct);

                // Fetch open orders from the exchange
                var exchangeOpenOrders = await adapter.GetOpenOrdersAsync(bot.Symbol, creds, ct);
                var exchangeOpenIds = exchangeOpenOrders.Select(o => o.ExternalOrderId).ToHashSet();

                // Find DB orders for this bot that were open last tick
                var dbOpenOrders = await db.Orders
                    .Where(o => o.BotId == bot.Id && o.Status == "open")
                    .ToListAsync(ct);

                var levels = CalculatePriceLevels(bot);

                foreach (var dbOrder in dbOpenOrders)
                {
                    if (string.IsNullOrEmpty(dbOrder.ExternalOrderId)) continue;

                    // If an order is no longer in the exchange's open list → it was filled
                    if (!exchangeOpenIds.Contains(dbOrder.ExternalOrderId))
                    {
                        dbOrder.Status = "filled";
                        dbOrder.LastSyncTime = DateTime.UtcNow;

                        await HandleFilledOrderAsync(bot, dbOrder, levels, db, adapter, creds, ct);
                    }
                }

                await db.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GridBotEngine: SyncGridOrdersAsync failed for bot {BotId}.", bot.Id);
            }
        }

        private async Task HandleFilledOrderAsync(
            GridBot bot, Order filledOrder, List<decimal> levels,
            AutoSignalsDbContext db, IExchangeOrderAdapter adapter, ExchangeCredentials creds,
            CancellationToken ct)
        {
            var filledPrice = (decimal)(filledOrder.Price ?? 0);
            if (filledPrice <= 0) return;

            bot.FilledOrderCount++;

            // Find the level index for this order
            int idx = FindNearestLevelIndex(levels, filledPrice);

            if (filledOrder.Side == "buy")
            {
                // Place a sell one level up
                if (idx + 1 < levels.Count)
                {
                    var counterPrice = levels[idx + 1];
                    var counterQty = bot.OrderSizeUsd / counterPrice;
                    var counterOrder = BuildOrder(bot, "sell", "limit", counterPrice, counterQty,
                        description: $"Grid sell @ {counterPrice:F4} (bot {bot.Id})");
                    await SendOrderAsync(bot, counterOrder, "sell", ct, db);

                    // Realised profit = sell level - buy level (per unit)
                    var qty = bot.OrderSizeUsd / filledPrice;
                    bot.TotalProfit += (counterPrice - filledPrice) * qty;
                }
            }
            else // sell
            {
                // Place a buy one level down
                if (idx - 1 >= 0)
                {
                    var counterPrice = levels[idx - 1];
                    var counterQty = bot.OrderSizeUsd / counterPrice;
                    var counterOrder = BuildOrder(bot, "buy", "limit", counterPrice, counterQty,
                        description: $"Grid buy @ {counterPrice:F4} (bot {bot.Id})");
                    await SendOrderAsync(bot, counterOrder, "buy", ct, db);
                }
            }

            bot.UpdatedAt = DateTime.UtcNow;
            _logger.LogInformation("GridBotEngine: Order filled ({Side} @ {Price}) for bot {BotId}. Counter order placed.",
                filledOrder.Side, filledPrice, bot.Id);
        }

        // ── Cancel all grid orders ────────────────────────────────────────────────

        private async Task CancelAllGridOrdersAsync(GridBot bot, CancellationToken ct)
        {
            if (bot.IsTest)
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AutoSignalsDbContext>();
                var openOrders = await db.Orders
                    .Where(o => o.BotId == bot.Id && o.Status == "open")
                    .ToListAsync(ct);
                foreach (var o in openOrders) o.Status = "cancelled";
                await db.SaveChangesAsync(ct);
                return;
            }

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AutoSignalsDbContext>();
                var (adapter, creds) = await ResolveAdapterAsync(bot, scope, ct);

                var openOrders = await db.Orders
                    .Where(o => o.BotId == bot.Id && o.Status == "open")
                    .ToListAsync(ct);

                foreach (var order in openOrders)
                {
                    if (!string.IsNullOrEmpty(order.ExternalOrderId))
                    {
                        var cancelled = await adapter.CancelOrderAsync(bot.Symbol, order.ExternalOrderId, creds, ct);
                        if (!cancelled)
                            _logger.LogWarning("GridBotEngine: Failed to cancel order {OrderId} for bot {BotId}.", order.ExternalOrderId, bot.Id);
                    }
                    order.Status = "cancelled";
                }

                await db.SaveChangesAsync(ct);
                _logger.LogInformation("GridBotEngine: Cancelled {Count} open orders for bot {BotId}.", openOrders.Count, bot.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GridBotEngine: CancelAllGridOrdersAsync failed for bot {BotId}.", bot.Id);
            }
        }

        // ── Price level calculation ───────────────────────────────────────────────

        internal static List<decimal> CalculatePriceLevels(GridBot bot)
        {
            int n = Math.Max(2, bot.GridCount);
            var levels = new List<decimal>(n + 1);

            if (bot.GridMode == GridMode.Arithmetic)
            {
                var step = (bot.UpperPrice - bot.LowerPrice) / n;
                for (int i = 0; i <= n; i++)
                    levels.Add(bot.LowerPrice + step * i);
            }
            else // Geometric
            {
                var ratio = (double)(bot.UpperPrice / bot.LowerPrice);
                var stepRatio = (decimal)Math.Pow(ratio, 1.0 / n);
                var price = bot.LowerPrice;
                for (int i = 0; i <= n; i++)
                {
                    levels.Add(price);
                    price *= stepRatio;
                }
            }

            return levels;
        }

        private static int FindNearestLevelIndex(List<decimal> levels, decimal price)
        {
            int best = 0;
            decimal minDiff = Math.Abs(levels[0] - price);
            for (int i = 1; i < levels.Count; i++)
            {
                var diff = Math.Abs(levels[i] - price);
                if (diff < minDiff) { minDiff = diff; best = i; }
            }
            return best;
        }

        // ── Price fetch ───────────────────────────────────────────────────────────

        private async Task<decimal> GetCurrentPriceAsync(string symbol, CancellationToken ct)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AutoSignalsDbContext>();
                return await db.GeneralAssetPrices
                    .Where(p => p.Symbol == symbol)
                    .OrderByDescending(p => p.Time)
                    .Select(p => p.Price)
                    .FirstOrDefaultAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GridBotEngine: Failed to fetch price for {Symbol}.", symbol);
                return 0m;
            }
        }

        // ── Order helpers ─────────────────────────────────────────────────────────

        private static Order BuildOrder(GridBot bot, string side, string type, decimal price, decimal qty, string description)
        {
            return new Order
            {
                BotId = bot.Id,
                UserId = bot.UserId,
                SignalId = 0,
                ExchangeId = bot.ExchangeConnectionId.ToString(),
                Symbol = bot.Symbol,
                Side = side,
                Price = type == "limit" ? (double?)price : null,
                Size = (double)qty,
                Leverage = bot.Leverage,
                IsIsolated = bot.IsIsolated,
                IsTest = bot.IsTest,
                Status = "pending",
                Description = description,
                Time = DateTime.UtcNow
            };
        }

        /// <summary>Send an order, persisting it in the provided db context or a dedicated scope.</summary>
        private async Task SendOrderAsync(GridBot bot, Order order, string side, CancellationToken ct, AutoSignalsDbContext? existingDb = null)
        {
            if (bot.IsTest)
            {
                order.Status = "open"; // grid orders stay open until filled
                order.ExchangeOrderStatus = "test_open";
                await PersistOrderAsync(order, ct, existingDb);
                return;
            }

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var (adapter, creds) = await ResolveAdapterAsync(bot, scope, ct);

                var result = side == "sell"
                    ? await adapter.SendStoplossOrderAsync(order, creds, ct)
                    : await adapter.SendEntryOrderAsync(order, creds, ct);

                if (!result.Success)
                {
                    _logger.LogWarning("GridBotEngine: Exchange rejected order for bot {BotId}: {Error}", bot.Id, result.ErrorMessage);
                    return;
                }

                order.ExternalOrderId = result.ExternalOrderId;
                order.ClientOrderId = result.ClientOrderId;
                order.ExchangeOrderStatus = result.Status;
                order.ExchangeResponseJson = result.Response?.ToString();
                order.Status = "open";

                await PersistOrderAsync(order, ct, existingDb);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GridBotEngine: SendOrderAsync failed for bot {BotId}.", bot.Id);
            }
        }

        private async Task PersistOrderAsync(Order order, CancellationToken ct, AutoSignalsDbContext? existingDb = null)
        {
            if (existingDb != null)
            {
                existingDb.Orders.Add(order);
                // Caller will call SaveChangesAsync
                return;
            }

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AutoSignalsDbContext>();
            db.Orders.Add(order);
            await db.SaveChangesAsync(ct);
        }

        // ── Infrastructure helpers ────────────────────────────────────────────────

        private async Task<(IExchangeOrderAdapter adapter, ExchangeCredentials creds)> ResolveAdapterAsync(
            GridBot bot, IServiceScope scope, CancellationToken ct)
        {
            var db = scope.ServiceProvider.GetRequiredService<AutoSignalsDbContext>();
            var factory = scope.ServiceProvider.GetRequiredService<ExchangeOrderAdapterFactory>();

            var connection = await db.UserExchangeConnections
                .Include(c => c.Exchange)
                .FirstOrDefaultAsync(c => c.Id == bot.ExchangeConnectionId, ct)
                ?? throw new InvalidOperationException($"ExchangeConnection {bot.ExchangeConnectionId} not found.");

            var apiKey = _encryption.Decrypt(connection.ApiKey ?? string.Empty);
            var apiSecret = _encryption.Decrypt(connection.ApiSecret ?? string.Empty);
            var passphrase = string.IsNullOrWhiteSpace(connection.ApiPassword)
                ? null
                : _encryption.Decrypt(connection.ApiPassword);

            var creds = new ExchangeCredentials(apiKey, apiSecret, passphrase);
            var adapter = await factory.GetRequiredAdapterAsync(connection.ExchangeId, ct);
            return (adapter, creds);
        }

        private async Task SendTelegramAsync(GridBot bot, string html, CancellationToken ct)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var notifier = scope.ServiceProvider.GetRequiredService<ITelegramNotifier>();
                await notifier.SendDirectMessageToUserAsync(bot.UserId, html, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "GridBotEngine: Telegram notification failed for bot {BotId}.", bot.Id);
            }
        }
    }
}
