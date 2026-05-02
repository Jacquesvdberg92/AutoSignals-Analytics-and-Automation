using AutoSignals.Data;
using AutoSignals.Models;
using AutoSignals.Models.Bots;
using AutoSignals.Services.ExchangeAdapters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AutoSignals.Services.Bots
{
    /// <summary>
    /// Tick-driven engine for DCA bots. Registered as a singleton; uses IServiceScopeFactory
    /// for all scoped DB and adapter access. Mutates the BotBase object passed in by the hosted
    /// service — bot-property changes are saved by the hosted service's DbContext at the end of
    /// the tick. Orders are saved immediately in a dedicated scope.
    /// </summary>
    public class DcaBotEngine : IBotEngine
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly AesEncryptionService _encryption;
        private readonly ILogger<DcaBotEngine> _logger;

        public BotType SupportedBotType => BotType.DCA;

        public DcaBotEngine(IServiceScopeFactory scopeFactory, AesEncryptionService encryption, ILogger<DcaBotEngine> logger)
        {
            _scopeFactory = scopeFactory;
            _encryption = encryption;
            _logger = logger;
        }

        public async Task TickAsync(BotBase botBase, CancellationToken ct)
        {
            if (botBase is not DcaBot bot)
            {
                return;
            }

            if (bot.Status != BotStatus.Running)
            {
                return;
            }

            // Handle post-TP cooldown
            if (bot.CooldownUntil.HasValue)
            {
                if (DateTime.UtcNow < bot.CooldownUntil.Value)
                {
                    return; // Still cooling down
                }

                // Cooldown expired — reset for a new cycle
                bot.CooldownUntil = null;
                bot.CurrentSafetyOrderCount = 0;
                bot.AverageEntryPrice = null;
                bot.TotalInvested = 0m;
            }

            // Fetch current price from GeneralAssetPrices (no exchange call needed)
            var currentPrice = await GetCurrentPriceAsync(bot.Symbol, ct);
            if (currentPrice <= 0m)
            {
                _logger.LogWarning("DcaBotEngine: No price available for {Symbol} (BotId={BotId}). Skipping tick.", bot.Symbol, bot.Id);
                return;
            }

            if (bot.AverageEntryPrice == null)
            {
                // No active position — place the base order
                await PlaceBaseOrderAsync(bot, currentPrice, ct);
            }
            else
            {
                // Active position — first sync any open limit safety orders
                await SyncOpenSafetyOrdersAsync(bot, ct);

                // Re-check price after sync (state may have changed)
                currentPrice = await GetCurrentPriceAsync(bot.Symbol, ct);
                if (currentPrice <= 0m) return;

                // Check take-profit
                if (await CheckTakeProfitAsync(bot, currentPrice, ct))
                {
                    return; // Position closed
                }

                // Check hard stoploss
                if (await CheckStoplossAsync(bot, currentPrice, ct))
                {
                    return; // Position closed
                }

                // Check whether to place the next safety order
                await MaybePlaceSafetyOrderAsync(bot, currentPrice, ct);
            }

            bot.LastRunAt = DateTime.UtcNow;
            bot.UpdatedAt = DateTime.UtcNow;
        }

        // ── Price ─────────────────────────────────────────────────────────────────

        private async Task<decimal> GetCurrentPriceAsync(string symbol, CancellationToken ct)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AutoSignalsDbContext>();
                var price = await db.GeneralAssetPrices
                    .Where(p => p.Symbol == symbol)
                    .OrderByDescending(p => p.Time)
                    .Select(p => p.Price)
                    .FirstOrDefaultAsync(ct);
                return price;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DcaBotEngine: Failed to fetch price for {Symbol}.", symbol);
                return 0m;
            }
        }

        // ── Order placement ───────────────────────────────────────────────────────

        private async Task PlaceBaseOrderAsync(DcaBot bot, decimal currentPrice, CancellationToken ct)
        {
            var sizeUsd = bot.BaseOrderSizeUsd;
            var qty = sizeUsd / currentPrice;

            var order = BuildOrder(bot, "buy", "market", currentPrice, (double)qty, description: $"DCA base order for bot {bot.Id}");

            var filled = await SendOrderAsync(bot, order, ct);
            if (!filled) return;

            bot.AverageEntryPrice = currentPrice;
            bot.TotalInvested = sizeUsd;
            bot.UpdatedAt = DateTime.UtcNow;

            _logger.LogInformation("DcaBotEngine: Base order placed for bot {BotId} at {Price}.", bot.Id, currentPrice);

            await SendTelegramAsync(bot, $"🤖 <b>DCA Bot started</b>\nSymbol: <code>{bot.Symbol}</code>\nBase order: <b>{sizeUsd:F2} USD</b> @ {currentPrice:F4}\n{(bot.IsTest ? "⚠️ TEST MODE" : string.Empty)}", ct);
        }

        private async Task MaybePlaceSafetyOrderAsync(DcaBot bot, decimal currentPrice, CancellationToken ct)
        {
            if (bot.CurrentSafetyOrderCount >= bot.MaxSafetyOrders) return;
            if (bot.AverageEntryPrice == null) return;

            // Deviation for the NEXT safety order (compounding step scale)
            var deviation = bot.SafetyOrderPriceDeviation
                * (decimal)Math.Pow((double)bot.SafetyOrderStepScale, bot.CurrentSafetyOrderCount);
            var triggerPrice = bot.AverageEntryPrice.Value * (1m - deviation / 100m);

            if (currentPrice > triggerPrice) return; // Price hasn't dropped enough

            var soSize = bot.SafetyOrderSizeUsd
                * (decimal)Math.Pow((double)bot.SafetyOrderVolumeScale, bot.CurrentSafetyOrderCount);
            var qty = soSize / currentPrice;

            var order = BuildOrder(bot, "buy", "limit", currentPrice, (double)qty,
                description: $"DCA safety order #{bot.CurrentSafetyOrderCount + 1} for bot {bot.Id}");

            var filled = await SendOrderAsync(bot, order, ct);
            if (!filled) return;

            // Recalculate VWAP
            var prevQty = bot.TotalInvested / bot.AverageEntryPrice.Value;
            var newQty = prevQty + qty;
            bot.AverageEntryPrice = (bot.TotalInvested + soSize) / newQty;
            bot.TotalInvested += soSize;
            bot.CurrentSafetyOrderCount++;
            bot.UpdatedAt = DateTime.UtcNow;

            _logger.LogInformation("DcaBotEngine: Safety order #{Count} placed for bot {BotId} at {Price}.",
                bot.CurrentSafetyOrderCount, bot.Id, currentPrice);
        }

        private async Task<bool> CheckTakeProfitAsync(DcaBot bot, decimal currentPrice, CancellationToken ct)
        {
            if (bot.AverageEntryPrice == null) return false;

            var tpPrice = bot.AverageEntryPrice.Value * (1m + bot.TakeProfitPercent / 100m);
            if (currentPrice < tpPrice) return false;

            await ClosePositionAsync(bot, currentPrice, "TP hit", ct);

            await SendTelegramAsync(bot, $"✅ <b>DCA Bot: Take-Profit hit!</b>\nSymbol: <code>{bot.Symbol}</code>\nTP price: {tpPrice:F4} | Filled @ {currentPrice:F4}\nAvg entry: {bot.AverageEntryPrice:F4}", ct);
            return true;
        }

        private async Task<bool> CheckStoplossAsync(DcaBot bot, decimal currentPrice, CancellationToken ct)
        {
            if (bot.AverageEntryPrice == null || bot.StoplossPercent == null) return false;

            var slPrice = bot.AverageEntryPrice.Value * (1m - bot.StoplossPercent.Value / 100m);
            if (currentPrice > slPrice) return false;

            await ClosePositionAsync(bot, currentPrice, "SL hit", ct);

            await SendTelegramAsync(bot, $"🛑 <b>DCA Bot: Stoploss hit!</b>\nSymbol: <code>{bot.Symbol}</code>\nSL price: {slPrice:F4} | Filled @ {currentPrice:F4}\nAvg entry: {bot.AverageEntryPrice:F4}", ct);
            return true;
        }

        private async Task ClosePositionAsync(DcaBot bot, decimal currentPrice, string reason, CancellationToken ct)
        {
            if (bot.AverageEntryPrice == null) return;

            var totalQty = bot.TotalInvested / bot.AverageEntryPrice.Value;
            var order = BuildOrder(bot, "sell", "market", currentPrice, (double)totalQty,
                description: $"DCA close ({reason}) for bot {bot.Id}");

            await SendOrderAsync(bot, order, ct);

            _logger.LogInformation("DcaBotEngine: Position closed ({Reason}) for bot {BotId} at {Price}.", reason, bot.Id, currentPrice);

            if (bot.AutoRestart && bot.CooldownMinutes > 0)
            {
                bot.CooldownUntil = DateTime.UtcNow.AddMinutes(bot.CooldownMinutes);
                _logger.LogInformation("DcaBotEngine: Bot {BotId} entering cooldown until {Until}.", bot.Id, bot.CooldownUntil);
            }
            else if (bot.AutoRestart)
            {
                // Reset immediately for next cycle
                bot.CurrentSafetyOrderCount = 0;
                bot.AverageEntryPrice = null;
                bot.TotalInvested = 0m;
            }
            else
            {
                bot.Status = BotStatus.Completed;
            }

            bot.UpdatedAt = DateTime.UtcNow;
        }

        // ── Safety order sync ─────────────────────────────────────────────────────

        private async Task SyncOpenSafetyOrdersAsync(DcaBot bot, CancellationToken ct)
        {
            if (bot.IsTest) return; // Test mode orders are auto-filled on placement

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AutoSignalsDbContext>();
                var factory = scope.ServiceProvider.GetRequiredService<ExchangeOrderAdapterFactory>();

                var openOrders = await db.Orders
                    .Where(o => o.BotId == bot.Id && o.Status == "open" && o.Side == "buy")
                    .ToListAsync(ct);

                if (!openOrders.Any()) return;

                var (adapter, creds) = await ResolveAdapterAsync(bot, scope, ct);

                foreach (var order in openOrders)
                {
                    var syncResult = await adapter.SyncOrderAsync(order, creds, ct);
                    if (syncResult?.NormalizedStatus == "FILLED")
                    {
                        order.Status = "filled";
                        order.ExchangeOrderStatus = syncResult.ExchangeStatus;
                        order.LastSyncTime = DateTime.UtcNow;

                        // Recalculate VWAP with the filled safety order
                        var fillPrice = syncResult.AveragePrice ?? (decimal)(order.Price ?? (double)bot.AverageEntryPrice!.Value);
                        var fillQty = syncResult.FilledQuantity ?? (decimal)order.Size;
                        var prevQty = bot.TotalInvested / bot.AverageEntryPrice!.Value;
                        var fillUsd = fillPrice * fillQty;
                        bot.AverageEntryPrice = (bot.TotalInvested + fillUsd) / (prevQty + fillQty);
                        bot.TotalInvested += fillUsd;
                    }
                }

                await db.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DcaBotEngine: Failed to sync safety orders for bot {BotId}.", bot.Id);
            }
        }

        // ── Order helpers ─────────────────────────────────────────────────────────

        private static Order BuildOrder(DcaBot bot, string side, string type, decimal price, double qty, string description)
        {
            return new Order
            {
                BotId = bot.Id,
                UserId = bot.UserId,
                SignalId = 0,
                ExchangeId = bot.ExchangeConnectionId.ToString(),
                Symbol = bot.Symbol,
                Side = side,
                Price = type == "market" ? null : (double?)price,
                Size = qty,
                Leverage = bot.Leverage,
                IsIsolated = bot.IsIsolated,
                IsTest = bot.IsTest,
                Status = "pending",
                Description = description,
                Time = DateTime.UtcNow
            };
        }

        /// <summary>
        /// Sends an order to the exchange (or simulates in test mode).
        /// Saves the Order record and returns true if the order was accepted/filled.
        /// </summary>
        private async Task<bool> SendOrderAsync(DcaBot bot, Order order, CancellationToken ct)
        {
            if (bot.IsTest)
            {
                order.Status = "filled";
                order.ExchangeOrderStatus = "test_fill";
                await PersistOrderAsync(order, ct);
                return true;
            }

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var (adapter, creds) = await ResolveAdapterAsync(bot, scope, ct);

                var result = order.Side == "sell"
                    ? await adapter.SendStoplossOrderAsync(order, creds, ct)  // reuse for market close
                    : await adapter.SendEntryOrderAsync(order, creds, ct);

                if (!result.Success)
                {
                    _logger.LogWarning("DcaBotEngine: Exchange rejected order for bot {BotId}: {Error}", bot.Id, result.ErrorMessage);
                    return false;
                }

                order.ExternalOrderId = result.ExternalOrderId;
                order.ClientOrderId = result.ClientOrderId;
                order.ExchangeOrderStatus = result.Status;
                order.ExchangeResponseJson = result.Response?.ToString();
                order.Status = order.Side == "buy" && order.Price.HasValue ? "open" : "filled";

                await PersistOrderAsync(order, ct);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DcaBotEngine: SendOrderAsync failed for bot {BotId}.", bot.Id);
                return false;
            }
        }

        private async Task PersistOrderAsync(Order order, CancellationToken ct)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AutoSignalsDbContext>();
            db.Orders.Add(order);
            await db.SaveChangesAsync(ct);
        }

        // ── Infrastructure helpers ────────────────────────────────────────────────

        private async Task<(IExchangeOrderAdapter adapter, ExchangeCredentials creds)> ResolveAdapterAsync(
            DcaBot bot, IServiceScope scope, CancellationToken ct)
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

        private async Task SendTelegramAsync(DcaBot bot, string html, CancellationToken ct)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var notifier = scope.ServiceProvider.GetRequiredService<ITelegramNotifier>();
                await notifier.SendDirectMessageToUserAsync(bot.UserId, html, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "DcaBotEngine: Telegram notification failed for bot {BotId}.", bot.Id);
            }
        }
    }
}
