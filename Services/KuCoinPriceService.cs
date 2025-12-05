namespace AutoSignals.Services
{
    using AutoSignals.Data;
    using AutoSignals.Models;
    using ccxt;
    using CryptoExchange.Net.Objects;
    using Kucoin.Net.Clients;
    using Kucoin.Net.Interfaces.Clients;
    using Kucoin.Net.Objects;
    using Microsoft.EntityFrameworkCore;
    using Newtonsoft.Json;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;

    public class KuCoinPriceService : IKuCoinService
    {
        private readonly ccxt.kucoinfutures _kucoin;
        private readonly AutoSignalsDbContext _context;
        private static readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

        public KuCoinPriceService(string apiKey, string apiSecret, string password, AutoSignalsDbContext context)
        {
            _kucoin = new ccxt.kucoinfutures(new Dictionary<string, object>
            {
                { "apiKey", apiKey },
                { "secret", apiSecret },
                { "password", password }
            });
            _context = context;
        }

        public async Task<IEnumerable<object>> GetKuCoinMarketsAsync()
        {
            var markets = await _kucoin.fetchMarkets() as List<object>;

            if (markets == null)
            {
                Console.WriteLine("Failed to fetch futures markets.");
                return Enumerable.Empty<object>();
            }

            var usdtSwapMarkets = new List<KuCoinMarket>();
            var fetchedSymbols = new HashSet<string>();

            foreach (var market in markets)
            {
                if (market is Dictionary<string, object> marketDict &&
                    marketDict.TryGetValue("quoteId", out var quote) && quote.ToString() == "USDT")
                {
                    var limits = marketDict["limits"] as Dictionary<string, object>;
                    var cost = limits["cost"] as Dictionary<string, object>;
                    var leverage = limits["leverage"] as Dictionary<string, object>;
                    var precision = marketDict["precision"] as Dictionary<string, object>;

                    var symbol = marketDict["symbol"].ToString().Replace("/", "").Replace(":USDT", "");

                    var existingMarket = await _context.KuCoinMarkets.FirstOrDefaultAsync(m => m.Symbol == symbol);

                    if (existingMarket != null)
                    {
                        // Update existing market
                        existingMarket.BaseCoin = marketDict["base"].ToString();
                        existingMarket.QuoteCoin = marketDict["quote"].ToString();
                        existingMarket.MakerFeeRate = Convert.ToDecimal(marketDict["maker"]);
                        existingMarket.TakerFeeRate = Convert.ToDecimal(marketDict["taker"]);
                        existingMarket.MinTradeUSDT = Convert.ToDecimal(cost["min"]);
                        existingMarket.MinLever = Convert.ToInt32(leverage["min"]);
                        existingMarket.MaxLever = Convert.ToInt32(leverage["max"]);
                        existingMarket.PricePrecision = Convert.ToDecimal(precision["price"]);
                        existingMarket.AmountPrecision = Convert.ToDecimal(precision["amount"]);
                        existingMarket.Time = DateTime.Now;
                    }
                    else
                    {
                        // Add new market
                        var kucoinMarket = new KuCoinMarket
                        {
                            Symbol = symbol,
                            BaseCoin = marketDict["base"].ToString(),
                            QuoteCoin = marketDict["quote"].ToString(),
                            MakerFeeRate = Convert.ToDecimal(marketDict["maker"]),
                            TakerFeeRate = Convert.ToDecimal(marketDict["taker"]),
                            MinTradeUSDT = Convert.ToDecimal(cost["min"]),
                            MinLever = Convert.ToInt32(leverage["min"]),
                            MaxLever = Convert.ToInt32(leverage["max"]),
                            PricePrecision = Convert.ToDecimal(precision["price"]),
                            AmountPrecision = Convert.ToDecimal(precision["amount"]),
                            Time = DateTime.Now
                        };
                        usdtSwapMarkets.Add(kucoinMarket);
                    }

                    // Add symbol to fetched symbols set
                    fetchedSymbols.Add(symbol);
                }
            }

            // Save new and updated markets to database
            if (usdtSwapMarkets.Count > 0)
            {
                _context.KuCoinMarkets.AddRange(usdtSwapMarkets);
            }
            await _context.SaveChangesAsync();

            // Find symbols to delete
            var currentSymbols = await _context.KuCoinMarkets.Select(m => m.Symbol).ToListAsync();
            var symbolsToDelete = currentSymbols.Except(fetchedSymbols).ToList();

            if (symbolsToDelete.Count > 0)
            {
                // Delete from KuCoinMarkets
                var marketsToDelete = _context.KuCoinMarkets.Where(m => symbolsToDelete.Contains(m.Symbol));
                _context.KuCoinMarkets.RemoveRange(marketsToDelete);

                // Insert into KuCoinRemovedAssets
                foreach (var symbol in symbolsToDelete)
                {
                    var existingRemovedAsset = await _context.KuCoinRemovedAssets.FirstOrDefaultAsync(ra => ra.Symbol == symbol);
                    if (existingRemovedAsset == null)
                    {
                        var removedAsset = new KuCoinRemovedAsset
                        {
                            Symbol = symbol,
                            Time = DateTime.UtcNow
                        };
                        _context.KuCoinRemovedAssets.Add(removedAsset);
                    }
                }

                await _context.SaveChangesAsync();
            }

            return usdtSwapMarkets.Cast<object>();
        }

        public async Task FetchAllKuCoinAssetPricesAsync()
        {
            var markets = await _context.KuCoinMarkets.ToListAsync();
            var assetPricesToAdd = new List<KuCoinAssetPrice>();
            var assetPricesToUpdate = new List<KuCoinAssetPrice>();
            var fetchedSymbols = new HashSet<string>();

            // Cache existing asset prices to minimize DB reads
            var existingAssetPrices = await _context.KuCoinAssetPrices.ToDictionaryAsync(ap => ap.Symbol);
            var existingRemovedAssets = await _context.KuCoinRemovedAssets.ToDictionaryAsync(ra => ra.Symbol);

            foreach (var market in markets)
            {
                var ticker = await _kucoin.fetchTicker(market.Symbol) as Dictionary<string, object>;
                if (ticker != null && ticker.ContainsKey("last"))
                {
                    var price = Convert.ToDecimal(ticker["last"]);
                    var open = ticker.ContainsKey("open") ? Convert.ToDecimal(ticker["open"]) : 0;
                    var high = ticker.ContainsKey("high") ? Convert.ToDecimal(ticker["high"]) : 0;
                    var low = ticker.ContainsKey("low") ? Convert.ToDecimal(ticker["low"]) : 0;
                    var close = ticker.ContainsKey("close") ? Convert.ToDecimal(ticker["close"]) : 0;
                    var volume = ticker.ContainsKey("baseVolume") ? Convert.ToDecimal(ticker["baseVolume"]) : 0;

                    if (existingAssetPrices.TryGetValue(market.Symbol, out var existingAssetPrice))
                    {
                        // Update existing asset price only if the price has changed significantly
                        if (existingAssetPrice.Price != price)
                        {
                            existingAssetPrice.Price = price;
                            // If KuCoinAssetPrice supports these fields, set them; otherwise, ignore.
                            existingAssetPrice.Open = open;
                            existingAssetPrice.High = high;
                            existingAssetPrice.Low = low;
                            existingAssetPrice.Close = close;
                            existingAssetPrice.Volume = volume;
                            existingAssetPrice.Time = DateTime.UtcNow;
                            assetPricesToUpdate.Add(existingAssetPrice);
                        }
                    }
                    else
                    {
                        // Prevent duplicate symbols in assetPricesToAdd
                        if (!assetPricesToAdd.Any(ap => ap.Symbol == market.Symbol))
                        {
                            var assetPrice = new KuCoinAssetPrice
                            {
                                Symbol = market.Symbol,
                                Price = price,
                                // If KuCoinAssetPrice supports these fields, set them; otherwise, ignore.
                                Open = open,
                                High = high,
                                Low = low,
                                Close = close,
                                Volume = volume,
                                Time = DateTime.UtcNow
                            };
                            assetPricesToAdd.Add(assetPrice);
                        }
                    }

                    // Add symbol to fetched symbols set
                    fetchedSymbols.Add(market.Symbol);
                }
            }

            // Batch add new asset prices
            if (assetPricesToAdd.Count > 0)
            {
                _context.KuCoinAssetPrices.AddRange(assetPricesToAdd);
            }

            // Find symbols to delete
            var symbolsToDelete = markets.Where(m => !fetchedSymbols.Contains(m.Symbol)).ToList();

            if (symbolsToDelete.Count > 0)
            {
                // Prepare to delete asset prices and markets
                var assetPricesToDelete = _context.KuCoinAssetPrices.Where(ap => symbolsToDelete.Any(m => m.Symbol == ap.Symbol));
                _context.KuCoinAssetPrices.RemoveRange(assetPricesToDelete);

                // Remove symbols from KuCoinMarkets
                _context.KuCoinMarkets.RemoveRange(symbolsToDelete);

                // Prepare removed assets for insertion without duplicates
                foreach (var symbol in symbolsToDelete.Select(m => m.Symbol))
                {
                    if (!existingRemovedAssets.ContainsKey(symbol))
                    {
                        var removedAsset = new KuCoinRemovedAsset
                        {
                            Symbol = symbol,
                            Time = DateTime.UtcNow
                        };
                        _context.KuCoinRemovedAssets.Add(removedAsset);
                    }
                }
            }

            if (assetPricesToUpdate.Count > 0)
            {
                _context.KuCoinAssetPrices.UpdateRange(assetPricesToUpdate);
            }

            int retryCount = 0;
            while (retryCount < 3)
            {
                try
                {
                    await _context.SaveChangesAsync();
                    break; // Success, exit loop
                }
                catch (Exception ex)
                {
                    retryCount++;
                    Console.WriteLine($"Error saving changes to the database (attempt {retryCount}): {ex.Message}");
                    if (retryCount >= 3)
                    {
                        Console.WriteLine($"Final error saving changes to the database: {ex.Message}");
                        Console.WriteLine($"Stack Trace: {ex.StackTrace}");
                        Console.WriteLine($"Inner Exception: {ex.InnerException?.Message}");
                        throw;
                    }
                    await Task.Delay(1000); // Wait a second before retrying
                }
            }
        }

        public async Task<decimal?> FetchKuCoinAssetPriceAsync(string symbol)
        {
            var ticker = await _kucoin.fetchTicker(symbol) as Dictionary<string, object>;
            if (ticker != null && ticker.ContainsKey("last"))
            {
                return Convert.ToDecimal(ticker["last"]);
            }
            else
            {
                Console.WriteLine($"Failed to fetch price for symbol: {symbol}");
                return null;
            }
        }

        public async Task GetTickerPricesViaWebSocketAsync()
        {
            await _semaphore.WaitAsync();
            try
            {
                if (_context == null)
                    throw new InvalidOperationException("Database context is not initialized.");

                var markets = await _context.KuCoinMarkets.Select(m => m.Symbol).ToListAsync();
                if (markets == null || !markets.Any())
                {
                    Console.WriteLine("No markets found.");
                    return;
                }

                var existingPrices = await _context.KuCoinAssetPrices.ToDictionaryAsync(ap => ap.Symbol);
                var assetPricesToAdd = new List<KuCoinAssetPrice>();
                var assetPricesToUpdate = new List<KuCoinAssetPrice>();
                var updatedSymbols = new HashSet<string>();
                var processedSymbols = new HashSet<string>();
                object listLock = new object();

                var logFactory = new LoggerFactory();
                var client = new KucoinSocketClient(logFactory);

                foreach (var symbol in markets)
                {
                    var result = await client.FuturesApi.SubscribeToTickerUpdatesAsync(symbol, data =>
                    {
                        if (data?.Data != null)
                        {
                            var lastPrice = data.Data.BestBidPrice;
                            var open = data.Data.BestBidPrice;
                            var high = data.Data.BestBidPrice;
                            var low = data.Data.BestBidPrice;
                            var close = data.Data.BestBidPrice;
                            var volume = data.Data.BestAskQuantity;

                            if (lastPrice != null)
                            {
                                lock (listLock)
                                {
                                    if (processedSymbols.Contains(symbol))
                                        return;

                                    if (existingPrices.TryGetValue(symbol, out var existingAssetPrice))
                                    {
                                        if (existingAssetPrice.Price != lastPrice)
                                        {
                                            existingAssetPrice.Price = (decimal)lastPrice;
                                            existingAssetPrice.Open = open;
                                            existingAssetPrice.High = high;
                                            existingAssetPrice.Low = low;
                                            existingAssetPrice.Close = close;
                                            existingAssetPrice.Volume = volume;
                                            existingAssetPrice.Time = DateTime.UtcNow;
                                            assetPricesToUpdate.Add(existingAssetPrice);
                                            processedSymbols.Add(symbol);
                                        }
                                    }
                                    else
                                    {
                                        assetPricesToAdd.Add(new KuCoinAssetPrice
                                        {
                                            Symbol = symbol,
                                            Price = (decimal)lastPrice,
                                            Open = open,
                                            High = high,
                                            Low = low,
                                            Close = close,
                                            Volume = volume,
                                            Time = DateTime.UtcNow
                                        });
                                        processedSymbols.Add(symbol);
                                    }

                                    updatedSymbols.Add(symbol);
                                }
                            }
                        }
                    });

                    if (!result.Success)
                        Console.WriteLine($"Failed to subscribe to ticker updates for symbol: {symbol}");
                }

                // Unsubscribe from all after processing (like Binance)
                await client.FuturesApi.UnsubscribeAllAsync();

                // --- DB operations (single-threaded, safe) ---
                if (assetPricesToAdd.Count > 0)
                    _context.KuCoinAssetPrices.AddRange(assetPricesToAdd);

                if (assetPricesToUpdate.Count > 0)
                    _context.KuCoinAssetPrices.UpdateRange(assetPricesToUpdate);

                // Find symbols to delete
                var symbolsToDelete = markets.Except(updatedSymbols).ToList();
                if (symbolsToDelete.Count > 0)
                {
                    var marketsToDelete = _context.KuCoinMarkets
                        .Where(m => symbolsToDelete.Contains(m.Symbol));
                    _context.KuCoinMarkets.RemoveRange(marketsToDelete);

                    var assetPricesToDelete = _context.KuCoinAssetPrices
                        .Where(ap => symbolsToDelete.Contains(ap.Symbol));
                    _context.KuCoinAssetPrices.RemoveRange(assetPricesToDelete);

                    foreach (var symbol in symbolsToDelete)
                    {
                        if (!await _context.KuCoinRemovedAssets.AnyAsync(ra => ra.Symbol == symbol))
                        {
                            _context.KuCoinRemovedAssets.Add(new KuCoinRemovedAsset
                            {
                                Symbol = symbol,
                                Time = DateTime.UtcNow
                            });
                        }
                    }
                }

                try
                {
                    await _context.SaveChangesAsync();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error saving changes to the database: {ex.Message}");
                    if (ex.InnerException != null)
                    {
                        Console.WriteLine($"Inner Exception: {ex.InnerException.Message}");
                        Console.WriteLine($"Inner Exception Stack Trace: {ex.InnerException.StackTrace}");
                    }
                    Console.WriteLine($"Stack Trace: {ex.StackTrace}");
                    throw;
                }
            }
            finally
            {
                _semaphore.Release();
            }
        }


        public async Task DeleteDuplicates()
        {
            var duplicateSymbols = _context.KuCoinAssetPrices.GroupBy(s => s.Symbol)
                                                             .Where(g => g.Count() > 1)
                                                             .SelectMany(g => g.OrderBy(s => s.Time)
                                                                              .Skip(1));

            _context.KuCoinAssetPrices.RemoveRange(duplicateSymbols);
            await _context.SaveChangesAsync();
        }

        public async Task<decimal> GetBalance(string apiKey, string apiSecret, string password)
        {
            // Placeholder for getting balance from KuCoin
            return await Task.FromResult(1000.0m); // Example balance
        }
    }
}
