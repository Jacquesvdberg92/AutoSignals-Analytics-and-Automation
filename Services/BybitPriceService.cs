namespace AutoSignals.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using AutoSignals.Models;
    using Microsoft.EntityFrameworkCore;
    using AutoSignals.Data;
    using Bybit.Net.Clients;

    public class BybitPriceService : IBybitService
    {
        private readonly ccxt.bybit _bybit;
        private readonly AutoSignalsDbContext _context;
        private static readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

        public BybitPriceService(string apiKey, string apiSecret, AutoSignalsDbContext context)
        {
            _bybit = new ccxt.bybit(new Dictionary<string, object>
            {
                { "apiKey", apiKey },
                { "secret", apiSecret },
                { "enableRateLimit", true },
                { "options", new Dictionary<string, object>() }
            });
            _context = context;
        }

        public async Task<IEnumerable<object>> GetBybitMarketsAsync()
        {
            await _semaphore.WaitAsync();
            try
            {
                var markets = await _bybit.fetchMarkets(new Dictionary<string, object> { { "type", "swap" } }) as List<object>;

                if (markets == null)
                {
                    Console.WriteLine("Failed to fetch markets.");
                    return Enumerable.Empty<object>();
                }

                var usdtFuturesMarkets = new List<BybitMarket>();
                var fetchedSymbols = new HashSet<string>();

                foreach (var market in markets)
                {
                    if (market is Dictionary<string, object> marketDict &&
                        marketDict.TryGetValue("quote", out var quote) && quote.ToString() == "USDT" &&
                        marketDict.TryGetValue("type", out var type) && type.ToString() == "swap")
                    {
                        var limits = marketDict["limits"] as Dictionary<string, object>;
                        var cost = limits?["cost"] as Dictionary<string, object>;
                        var precision = marketDict["precision"] as Dictionary<string, object>;

                        var id = marketDict["id"].ToString();
                        var symbol = marketDict["symbol"].ToString();
                        fetchedSymbols.Add(id);

                        var existingMarket = await _context.BybitMarkets.FirstOrDefaultAsync(m => m.Symbol == id);

                        int minLever = 10; // Default to 10 if not found
                        int maxLever = 10; // Default to 10 if not found

                        if (existingMarket != null)
                        {
                            // Update existing market
                            existingMarket.BaseCoin = marketDict["base"].ToString();
                            existingMarket.QuoteCoin = marketDict["quote"].ToString();
                            existingMarket.MakerFeeRate = Convert.ToDecimal(marketDict["maker"]);
                            existingMarket.TakerFeeRate = Convert.ToDecimal(marketDict["taker"]);
                            existingMarket.MinTradeUSDT = Convert.ToDecimal(cost?["min"] ?? 0); // Default to 0 if null
                            existingMarket.PricePrecision = Convert.ToDecimal(precision["price"]);
                            var leverageLimits = marketDict["limits"] as Dictionary<string, object>;
                            var leverage = leverageLimits?["leverage"] as Dictionary<string, object>;
                            existingMarket.MinLever = Convert.ToInt32(Math.Floor(Convert.ToDecimal(leverage?["min"] ?? 10))); // Default to 10 if null
                            existingMarket.MaxLever = Convert.ToInt32(Math.Floor(Convert.ToDecimal(leverage?["max"] ?? 10))); // Default to 10 if null
                            existingMarket.AmountPrecision = Convert.ToDecimal(precision["amount"]);
                            existingMarket.Time = DateTime.Now;
                        }
                        else
                        {
                            // Add new market
                            var bybitMarket = new BybitMarket
                            {
                                Symbol = id,
                                BaseCoin = marketDict["base"].ToString(),
                                QuoteCoin = marketDict["quote"].ToString(),
                                MakerFeeRate = Convert.ToDecimal(marketDict["maker"]),
                                TakerFeeRate = Convert.ToDecimal(marketDict["taker"]),
                                MinTradeUSDT = Convert.ToDecimal(cost?["min"] ?? 0), // Default to 0 if null
                                MinLever = minLever,
                                MaxLever = maxLever,
                                PricePrecision = Convert.ToDecimal(precision["price"]),
                                AmountPrecision = Convert.ToDecimal(precision["amount"]),
                                Time = DateTime.Now
                            };
                            _context.BybitMarkets.Add(bybitMarket);
                        }
                    }
                }

                // Delete markets that are no longer present in the fetched data
                var currentMarkets = await _context.BybitMarkets.ToListAsync();
                var removedMarkets = currentMarkets.Where(m => !fetchedSymbols.Contains(m.Symbol)).ToList();
                if (removedMarkets.Any())
                {
                    _context.BybitMarkets.RemoveRange(removedMarkets);
                }

                await _context.SaveChangesAsync();

                return markets;
            }
            finally
            {
                _semaphore.Release();
            }
        }


        public async Task FetchAllBybitAssetPricesAsync()
        {
            // Fetch all existing markets from the database
            var markets = await _context.BybitMarkets.ToListAsync();
            var assetPricesToAdd = new List<BybitAssetPrice>();
            var assetPricesToUpdate = new List<BybitAssetPrice>();
            var fetchedSymbols = new HashSet<string>();

            // Cache existing asset prices to minimize DB reads
            var existingAssetPrices = await _context.BybitAssetPrices.ToDictionaryAsync(ap => ap.Symbol);
            var existingRemovedAssets = await _context.BybitRemovedAssets.ToDictionaryAsync(ra => ra.Symbol);

            foreach (var market in markets)
            {
                // Fetch ticker data for each market
                var ticker = await _bybit.fetchTicker(market.Symbol) as Dictionary<string, object>;
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
                            // If BybitAssetPrice supports these fields, set them; otherwise, ignore.
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
                            var assetPrice = new BybitAssetPrice
                            {
                                Symbol = market.Symbol,
                                Price = price,
                                // If BybitAssetPrice supports these fields, set them; otherwise, ignore.
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
                _context.BybitAssetPrices.AddRange(assetPricesToAdd);
            }

            // Find symbols to delete
            var symbolsToDelete = markets.Where(m => !fetchedSymbols.Contains(m.Symbol)).ToList();

            if (symbolsToDelete.Count > 0)
            {
                // Prepare to delete asset prices and markets
                var assetPricesToDelete = _context.BybitAssetPrices.Where(ap => symbolsToDelete.Any(m => m.Symbol == ap.Symbol));
                _context.BybitAssetPrices.RemoveRange(assetPricesToDelete);

                // Remove symbols from BybitMarkets
                _context.BybitMarkets.RemoveRange(symbolsToDelete);

                // Prepare removed assets for insertion without duplicates
                foreach (var symbol in symbolsToDelete.Select(m => m.Symbol))
                {
                    if (!existingRemovedAssets.ContainsKey(symbol))
                    {
                        var removedAsset = new BybitRemovedAsset
                        {
                            Symbol = symbol,
                            Time = DateTime.UtcNow
                        };
                        _context.BybitRemovedAssets.Add(removedAsset);
                    }
                }
            }

            if (assetPricesToUpdate.Count > 0)
            {
                _context.BybitAssetPrices.UpdateRange(assetPricesToUpdate);
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

        public async Task<decimal?> FetchBybitAssetPriceAsync(string symbol)
        {
            var ticker = await _bybit.fetchTicker(symbol) as Dictionary<string, object>;
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
                {
                    throw new InvalidOperationException("Database context is not initialized.");
                }

                var markets = await _context.BybitMarkets.Select(m => m.Symbol).ToListAsync();
                if (markets == null || !markets.Any())
                {
                    Console.WriteLine("No markets found.");
                    return;
                }

                var existingPrices = await _context.BybitAssetPrices.ToDictionaryAsync(ap => ap.Symbol);
                var client = new BybitSocketClient();
                var assetPricesToAdd = new List<BybitAssetPrice>();
                var assetPricesToUpdate = new List<BybitAssetPrice>();
                var updatedSymbols = new HashSet<string>();
                var processedSymbols = new HashSet<string>();
                object listLock = new object();

                foreach (var symbol in markets)
                {
                    var result = await client.V5LinearApi.SubscribeToTickerUpdatesAsync(symbol, data =>
                    {
                        if (data != null)
                        {
                            var price = data.Data.LastPrice;
                            var open = data.Data.PreOpenPrice;
                            var high = data.Data.HighPrice24h;
                            var low = data.Data.LowPrice24h;
                            var close = data.Data.LowPrice24h;
                            var volume = data.Data.Volume24h;

                            if (price != null)
                            {
                                lock (listLock)
                                {
                                    if (processedSymbols.Contains(symbol))
                                        return; // Already handled, skip

                                    if (existingPrices.TryGetValue(symbol, out var existingAssetPrice))
                                    {
                                        if (existingAssetPrice.Price != price)
                                        {
                                            existingAssetPrice.Price = (decimal)price;
                                            existingAssetPrice.Open = open ?? 0;
                                            existingAssetPrice.High = high ?? 0;
                                            existingAssetPrice.Low = low ?? 0;
                                            existingAssetPrice.Close = close ?? 0;
                                            existingAssetPrice.Volume = volume ?? 0;
                                            existingAssetPrice.Time = DateTime.UtcNow;
                                            assetPricesToUpdate.Add(existingAssetPrice);
                                            processedSymbols.Add(symbol);
                                        }
                                    }
                                    else
                                    {
                                        assetPricesToAdd.Add(new BybitAssetPrice
                                        {
                                            Symbol = symbol,
                                            Price = (decimal)price,
                                            Open = open ?? 0,
                                            High = high ?? 0,
                                            Low = low ?? 0,
                                            Close = close ?? 0,
                                            Volume = volume ?? 0,
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
                    {
                        Console.WriteLine($"Failed to subscribe to ticker updates for symbol: {symbol}");
                    }
                }

                await client.V5LinearApi.UnsubscribeAllAsync();

                // Perform database operations after iteration
                if (assetPricesToAdd.Count > 0)
                {
                    _context.BybitAssetPrices.AddRange(assetPricesToAdd);
                }

                if (assetPricesToUpdate.Count > 0)
                {
                    _context.BybitAssetPrices.UpdateRange(assetPricesToUpdate);
                }

                // Find symbols to delete
                var symbolsToDelete = markets.Except(updatedSymbols).ToList();

                if (symbolsToDelete.Count > 0)
                {
                    // Delete from BybitMarkets
                    var marketsToDelete = _context.BybitMarkets.Where(m => symbolsToDelete.Contains(m.Symbol));
                    _context.BybitMarkets.RemoveRange(marketsToDelete);

                    // Delete from BybitAssetPrices
                    var assetPricesToDelete = _context.BybitAssetPrices.Where(ap => symbolsToDelete.Contains(ap.Symbol));
                    _context.BybitAssetPrices.RemoveRange(assetPricesToDelete);

                    // Insert into BybitRemovedAssets
                    foreach (var symbol in symbolsToDelete)
                    {
                        var existingRemovedAsset = await _context.BybitRemovedAssets.FirstOrDefaultAsync(ra => ra.Symbol == symbol);
                        if (existingRemovedAsset == null)
                        {
                            var removedAsset = new BybitRemovedAsset
                            {
                                Symbol = symbol,
                                Time = DateTime.UtcNow
                            };
                            _context.BybitRemovedAssets.Add(removedAsset);
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
            var duplicateSymbols = _context.BybitAssetPrices.GroupBy(s => s.Symbol)
                                                             .Where(g => g.Count() > 1)
                                                             .SelectMany(g => g.OrderBy(s => s.Time)
                                                                              .Skip(1));

            _context.BybitAssetPrices.RemoveRange(duplicateSymbols);
            await _context.SaveChangesAsync();
        }


        public async Task<decimal> GetBalance(string apiKey, string apiSecret, string password)
        {
            // Placeholder for getting balance from Bybit
            return await Task.FromResult(1000.0m); // Example balance
        }
    }
}
