using AutoSignals.Data;
using AutoSignals.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AutoSignals.Services
{
    public class AveragePriceService
    {
        private readonly IServiceScopeFactory _scopeFactory;

        public AveragePriceService(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        public async Task CalculateAndSaveAveragePricesAsync()
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AutoSignalsDbContext>();

            // Load all exchange prices in-memory once
            var bitgetPrices = await context.BitgetAssetPrices.AsNoTracking().ToListAsync().ConfigureAwait(false);
            var binancePrices = await context.BinanceAssetPrices.AsNoTracking().ToListAsync().ConfigureAwait(false);
            var bybitPrices = await context.BybitAssetPrices.AsNoTracking().ToListAsync().ConfigureAwait(false);
            var okxPrices = await context.OkxAssetPrices.AsNoTracking().ToListAsync().ConfigureAwait(false);
            var kucoinPrices = await context.KuCoinAssetPrices.AsNoTracking().ToListAsync().ConfigureAwait(false);

            // Build a combined view grouped by (Symbol, Type) so we can handle spot vs swap separately
            var groupedBySymbolAndType =
                bitgetPrices.Select(p => new { p.Symbol, p.Type })
                    .Concat(binancePrices.Select(p => new { p.Symbol, p.Type }))
                    .Concat(bybitPrices.Select(p => new { p.Symbol, p.Type }))
                    .Concat(okxPrices.Select(p => new { p.Symbol, p.Type }))
                    .Concat(kucoinPrices.Select(p => new { p.Symbol, p.Type }))
                    .Distinct()
                    .ToList();

            // Pre-load existing general prices to avoid N+1 DB calls
            var existingGeneralPrices = await context.GeneralAssetPrices
                .ToDictionaryAsync(g => $"{g.Symbol}__{g.Type}")
                .ConfigureAwait(false);

            foreach (var item in groupedBySymbolAndType)
            {
                var symbol = item.Symbol;
                var type = NormalizeType(item.Type); // normalize spot/swap strings if necessary

                // Collect all matching entries across exchanges for this (symbol, type)
                var allForKey = new List<(decimal? Price,
                                           decimal? Open,
                                           decimal? High,
                                           decimal? Low,
                                           decimal? Close,
                                           decimal? Volume,
                                           DateTime? Time)>
                {
                    // Bitget
                    bitgetPrices
                        .Where(p => p.Symbol == symbol && NormalizeType(p.Type) == type)
                        .Select(p => (p.Price as decimal?, p.Open as decimal?, p.High as decimal?, p.Low as decimal?, p.Close as decimal?, p.Volume as decimal?, p.Time as DateTime?))
                        .FirstOrDefault(),
                    // Binance
                    binancePrices
                        .Where(p => p.Symbol == symbol && NormalizeType(p.Type) == type)
                        .Select(p => (p.Price as decimal?, p.Open as decimal?, p.High as decimal?, p.Low as decimal?, p.Close as decimal?, p.Volume as decimal?, p.Time as DateTime?))
                        .FirstOrDefault(),
                    // Bybit
                    bybitPrices
                        .Where(p => p.Symbol == symbol && NormalizeType(p.Type) == type)
                        .Select(p => (p.Price as decimal?, p.Open as decimal?, p.High as decimal?, p.Low as decimal?, p.Close as decimal?, p.Volume as decimal?, p.Time as DateTime?))
                        .FirstOrDefault(),
                    // OKX
                    okxPrices
                        .Where(p => p.Symbol == symbol && NormalizeType(p.Type) == type)
                        .Select(p => (p.Price as decimal?, p.Open as decimal?, p.High as decimal?, p.Low as decimal?, p.Close as decimal?, p.Volume as decimal?, p.Time as DateTime?))
                        .FirstOrDefault(),
                    // KuCoin
                    kucoinPrices
                        .Where(p => p.Symbol == symbol && NormalizeType(p.Type) == type)
                        .Select(p => (p.Price as decimal?, p.Open as decimal?, p.High as decimal?, p.Low as decimal?, p.Close as decimal?, p.Volume as decimal?, p.Time as DateTime?))
                        .FirstOrDefault()
                }.Where(t => t != default).ToList();

                if (allForKey.Count == 0)
                {
                    continue;
                }

                decimal Avg(IEnumerable<decimal?> src) => src.Where(v => v.HasValue).Select(v => v.Value).DefaultIfEmpty(0m).Average();

                var averagePrice = Avg(allForKey.Select(x => x.Price));
                var averageOpen = Avg(allForKey.Select(x => x.Open));
                var averageHigh = Avg(allForKey.Select(x => x.High));
                var averageLow = Avg(allForKey.Select(x => x.Low));
                var averageClose = Avg(allForKey.Select(x => x.Close));
                var averageVolume = Avg(allForKey.Select(x => x.Volume));

                var latestTime = allForKey
                    .Select(x => x.Time)
                    .Where(t => t.HasValue)
                    .Select(t => t.Value)
                    .DefaultIfEmpty(DateTime.UtcNow)
                    .Max();

                var key = $"{symbol}__{type}";

                if (existingGeneralPrices.TryGetValue(key, out var existing))
                {
                    existing.Price = averagePrice;
                    existing.Open = averageOpen;
                    existing.High = averageHigh;
                    existing.Low = averageLow;
                    existing.Close = averageClose;
                    existing.Volume = averageVolume;
                    existing.Time = latestTime;
                }
                else
                {
                    var newEntity = new GeneralAssetPrice
                    {
                        Symbol = symbol,
                        Type = type,
                        Price = averagePrice,
                        Open = averageOpen,
                        High = averageHigh,
                        Low = averageLow,
                        Close = averageClose,
                        Volume = averageVolume,
                        Time = latestTime
                    };
                    context.GeneralAssetPrices.Add(newEntity);
                    existingGeneralPrices[key] = newEntity;
                }
            }

            // Delete records not updated in the last 24 hours
            var cutoff = DateTime.UtcNow.AddHours(-24);

            var oldRecords = await context.GeneralAssetPrices
                .Where(g => g.Time < cutoff)
                .ToListAsync()
                .ConfigureAwait(false);

            if (oldRecords.Count > 0)
            {
                context.GeneralAssetPrices.RemoveRange(oldRecords);
            }

            try
            {
                await context.SaveChangesAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating average prices: {ex.Message}");
                Console.WriteLine($"Error Inner Ex: {ex.InnerException}");

                using var errorScope = _scopeFactory.CreateScope();
                var errorLogService = errorScope.ServiceProvider.GetRequiredService<ErrorLogService>();

                await errorLogService.LogErrorAsync(
                    "Failed to save Average Prices",
                    ex.StackTrace,
                    nameof(AveragePriceService),
                    $"Inner Ex: {ex.InnerException}");

                throw;
            }
        }

        private static string NormalizeType(string type)
        {
            if (string.IsNullOrWhiteSpace(type))
            {
                return "unknown";
            }

            // Normalize common variants to "spot" or "swap"
            type = type.Trim().ToLowerInvariant();

            return type switch
            {
                "spot" => "spot",
                "swap" => "swap",
                "perpetual" => "swap",
                _ => type
            };
        }
    }
}
