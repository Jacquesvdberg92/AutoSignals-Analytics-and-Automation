using AutoSignals.Data;
using AutoSignals.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace AutoSignals.Services
{
    public class AveragePriceService
    {
        private readonly AutoSignalsDbContext _context;
        private readonly IServiceScopeFactory _scopeFactory;

        public AveragePriceService(AutoSignalsDbContext context, IServiceScopeFactory scopeFactory)
        {
            _context = context;
            _scopeFactory = scopeFactory;
        }

        public async Task CalculateAndSaveAveragePricesAsync()
        {
            var bitgetPrices = await _context.BitgetAssetPrices.ToListAsync();
            var binancePrices = await _context.BinanceAssetPrices.ToListAsync();
            var bybitPrices = await _context.BybitAssetPrices.ToListAsync();
            var okxPrices = await _context.OkxAssetPrices.ToListAsync();
            var kucoinPrices = await _context.KuCoinAssetPrices.ToListAsync();

            var allSymbols = bitgetPrices.Select(p => p.Symbol)
                .Union(binancePrices.Select(p => p.Symbol))
                .Union(bybitPrices.Select(p => p.Symbol))
                .Union(okxPrices.Select(p => p.Symbol))
                .Union(kucoinPrices.Select(p => p.Symbol))
                .Distinct();

            // Commented out kLine things
            // var klineAssetPrices = new List<KLineAssetPrice>();

            foreach (var symbol in allSymbols)
            {
                var priceValues = new List<decimal?> {
                    bitgetPrices.FirstOrDefault(p => p.Symbol == symbol)?.Price,
                    binancePrices.FirstOrDefault(p => p.Symbol == symbol)?.Price,
                    bybitPrices.FirstOrDefault(p => p.Symbol == symbol)?.Price,
                    okxPrices.FirstOrDefault(p => p.Symbol == symbol)?.Price,
                    kucoinPrices.FirstOrDefault(p => p.Symbol == symbol)?.Price
                };

                var openValues = new List<decimal?> {
                    bitgetPrices.FirstOrDefault(p => p.Symbol == symbol)?.Open,
                    binancePrices.FirstOrDefault(p => p.Symbol == symbol)?.Open,
                    bybitPrices.FirstOrDefault(p => p.Symbol == symbol)?.Open,
                    okxPrices.FirstOrDefault(p => p.Symbol == symbol)?.Open,
                    kucoinPrices.FirstOrDefault(p => p.Symbol == symbol)?.Open
                };

                var highValues = new List<decimal?> {
                    bitgetPrices.FirstOrDefault(p => p.Symbol == symbol)?.High,
                    binancePrices.FirstOrDefault(p => p.Symbol == symbol)?.High,
                    bybitPrices.FirstOrDefault(p => p.Symbol == symbol)?.High,
                    okxPrices.FirstOrDefault(p => p.Symbol == symbol)?.High,
                    kucoinPrices.FirstOrDefault(p => p.Symbol == symbol)?.High
                };

                var lowValues = new List<decimal?> {
                    bitgetPrices.FirstOrDefault(p => p.Symbol == symbol)?.Low,
                    binancePrices.FirstOrDefault(p => p.Symbol == symbol)?.Low,
                    bybitPrices.FirstOrDefault(p => p.Symbol == symbol)?.Low,
                    okxPrices.FirstOrDefault(p => p.Symbol == symbol)?.Low,
                    kucoinPrices.FirstOrDefault(p => p.Symbol == symbol)?.Low
                };

                var closeValues = new List<decimal?> {
                    bitgetPrices.FirstOrDefault(p => p.Symbol == symbol)?.Close,
                    binancePrices.FirstOrDefault(p => p.Symbol == symbol)?.Close,
                    bybitPrices.FirstOrDefault(p => p.Symbol == symbol)?.Close,
                    okxPrices.FirstOrDefault(p => p.Symbol == symbol)?.Close,
                    kucoinPrices.FirstOrDefault(p => p.Symbol == symbol)?.Close
                };

                var volumeValues = new List<decimal?> {
                    bitgetPrices.FirstOrDefault(p => p.Symbol == symbol)?.Volume,
                    binancePrices.FirstOrDefault(p => p.Symbol == symbol)?.Volume,
                    bybitPrices.FirstOrDefault(p => p.Symbol == symbol)?.Volume,
                    okxPrices.FirstOrDefault(p => p.Symbol == symbol)?.Volume,
                    kucoinPrices.FirstOrDefault(p => p.Symbol == symbol)?.Volume
                };

                decimal averagePrice = priceValues.Where(v => v.HasValue).Select(v => v.Value).DefaultIfEmpty(0).Average();
                decimal averageOpen = openValues.Where(v => v.HasValue).Select(v => v.Value).DefaultIfEmpty(0).Average();
                decimal averageHigh = highValues.Where(v => v.HasValue).Select(v => v.Value).DefaultIfEmpty(0).Average();
                decimal averageLow = lowValues.Where(v => v.HasValue).Select(v => v.Value).DefaultIfEmpty(0).Average();
                decimal averageClose = closeValues.Where(v => v.HasValue).Select(v => v.Value).DefaultIfEmpty(0).Average();
                decimal averageVolume = volumeValues.Where(v => v.HasValue).Select(v => v.Value).DefaultIfEmpty(0).Average();

                var times = new List<DateTime?> {
                    bitgetPrices.FirstOrDefault(p => p.Symbol == symbol)?.Time,
                    binancePrices.FirstOrDefault(p => p.Symbol == symbol)?.Time,
                    bybitPrices.FirstOrDefault(p => p.Symbol == symbol)?.Time,
                    okxPrices.FirstOrDefault(p => p.Symbol == symbol)?.Time,
                    kucoinPrices.FirstOrDefault(p => p.Symbol == symbol)?.Time
                };
                DateTime time = times.Where(t => t.HasValue).Select(t => t.Value).DefaultIfEmpty(DateTime.Now).Max();

                // Find existing record
                var existing = await _context.GeneralAssetPrices.FirstOrDefaultAsync(g => g.Symbol == symbol);

                if (existing != null)
                {
                    // Update existing record
                    existing.Price = averagePrice;
                    existing.Open = averageOpen;
                    existing.High = averageHigh;
                    existing.Low = averageLow;
                    existing.Close = averageClose;
                    existing.Volume = averageVolume;
                    existing.Time = time;
                }
                else
                {
                    // Optionally add new record if not found
                    _context.GeneralAssetPrices.Add(new GeneralAssetPrice
                    {
                        Symbol = symbol,
                        Price = averagePrice,
                        Open = averageOpen,
                        High = averageHigh,
                        Low = averageLow,
                        Close = averageClose,
                        Volume = averageVolume,
                        Time = time
                    });
                }

                // Commented out kLine things
                // klineAssetPrices.Add(new KLineAssetPrice
                // {
                //     Symbol = symbol,
                //     Price = averagePrice,
                //     Open = averageOpen,
                //     High = averageHigh,
                //     Low = averageLow,
                //     Close = averageClose,
                //     Volume = averageVolume,
                //     Time = time
                // });
            }

            // Delete records not updated in the last 24 hours
            var cutoff = DateTime.UtcNow.AddHours(-24);
            var oldRecords = await _context.GeneralAssetPrices
                .Where(g => g.Time < cutoff)
                .ToListAsync();
            if (oldRecords.Any())
            {
                _context.GeneralAssetPrices.RemoveRange(oldRecords);
            }

            try
            {
                // Save all changes to the database
                // _context.KLineAssetPrices.AddRange(klineAssetPrices);
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating average prices: {ex.Message}");
                Console.WriteLine("$Error Inner Ex: {ex.InnerException}");
                using (var errorLogScope = _scopeFactory.CreateScope())
                {
                    var errorLogService = errorLogScope.ServiceProvider.GetRequiredService<ErrorLogService>();
                    await errorLogService.LogErrorAsync(
                        $"Failed to save Average Prices",
                        ex.StackTrace, "AveragePriceService", $"Inner Ex: {ex.InnerException}");
                }
                throw;
            }
            
        }
    }
}
