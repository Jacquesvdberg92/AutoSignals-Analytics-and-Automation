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
        private readonly ccxt.kucoin _spot;
        private readonly ccxt.kucoinfutures _futures;
        private static readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        private readonly ErrorLogService _errorLogService;
        private readonly IServiceScopeFactory _scopeFactory;

        public KuCoinPriceService(string apiKey, string apiSecret, string password, ErrorLogService errorLogService, IServiceScopeFactory scopeFactory)
        {
            // Spot client
            _spot = new ccxt.kucoin(new Dictionary<string, object>
            {
                { "apiKey", apiKey },
                { "secret", apiSecret },
                { "password", password }
            });
            _spot.options["defaultType"] = "spot";

            // Futures client
            _futures = new ccxt.kucoinfutures(new Dictionary<string, object>
            {
                { "apiKey", apiKey },
                { "secret", apiSecret },
                { "password", password }
            });
            _futures.options["defaultType"] = "swap";

            _errorLogService = errorLogService;
            _scopeFactory = scopeFactory;
        }

        // Gets spot and futures USDT markets via API (mirrors Binance pattern)
        public async Task<IEnumerable<object>> GetKuCoinMarketsAsync()
        {
            await _semaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<AutoSignalsDbContext>();

                // Fetch spot and futures markets separately
                var spotMarketsTask = _spot.fetchMarkets();
                var futuresMarketsTask = _futures.fetchMarkets();

                await Task.WhenAll(spotMarketsTask, futuresMarketsTask).ConfigureAwait(false);

                var spotMarkets = spotMarketsTask.Result as List<object> ?? new List<object>();
                var futuresMarkets = futuresMarketsTask.Result as List<object> ?? new List<object>();

                var markets = new List<object>();
                markets.AddRange(spotMarkets);
                markets.AddRange(futuresMarkets);

                if (markets.Count == 0)
                {
                    Console.WriteLine("Failed to fetch KuCoin markets.");
                    return Enumerable.Empty<object>();
                }

                var kucoinMarketsToAdd = new List<KuCoinMarket>();
                var fetchedSymbols = new HashSet<string>();

                foreach (var market in markets)
                {
                    if (market is not Dictionary<string, object> marketDict)
                    {
                        continue;
                    }

                    // Only USDT-quoted
                    if (!marketDict.TryGetValue("quote", out var quote) ||
                        quote?.ToString() != "USDT")
                    {
                        continue;
                    }

                    // ccxt for KuCoin:
                    //  - spot  : type == "spot"
                    //  - swap  : type == "swap"
                    var marketType = marketDict.TryGetValue("type", out var typeVal)
                        ? typeVal?.ToString()
                        : string.Empty;

                    if (marketType != "spot" && marketType != "swap")
                    {
                        continue;
                    }

                    var baseCoin = marketDict["base"].ToString();
                    var quoteCoin = marketDict["quote"].ToString();

                    var limits = marketDict.TryGetValue("limits", out var limitsObj)
                        ? limitsObj as Dictionary<string, object>
                        : null;
                    var cost = limits != null && limits.TryGetValue("cost", out var costObj)
                        ? costObj as Dictionary<string, object>
                        : null;
                    var leverage = limits != null && limits.TryGetValue("leverage", out var levObj)
                        ? levObj as Dictionary<string, object>
                        : null;
                    var precision = marketDict.TryGetValue("precision", out var precObj)
                        ? precObj as Dictionary<string, object>
                        : null;

                    // Display symbol, e.g. "BTC/USDT", futures often "BTC/USDT:USDT"
                    var displaySymbol = marketDict["symbol"].ToString();

                    var isSpot = marketType == "spot";
                    var isFutures = marketType == "swap";

                    fetchedSymbols.Add(displaySymbol);

                    // KuCoin leverage: we do not have a tiers call here like Binance,
                    // so derive from limits.leverage if present, otherwise use defaults.
                    var minLever = 0;
                    var maxLever = 0;

                    if (isFutures && leverage != null)
                    {
                        if (leverage.TryGetValue("min", out var minLevObj))
                        {
                            minLever = Convert.ToInt32(minLevObj);
                        }
                        else
                        {
                            minLever = 1;
                        }

                        if (leverage.TryGetValue("max", out var maxLevObj))
                        {
                            maxLever = Convert.ToInt32(maxLevObj);
                        }
                        else
                        {
                            maxLever = 10;
                        }
                    }

                    var existingMarket = await context.KuCoinMarkets
                        .FirstOrDefaultAsync(m => m.Symbol == displaySymbol && m.Type == marketType)
                        .ConfigureAwait(false);

                    if (existingMarket != null)
                    {
                        existingMarket.BaseCoin = baseCoin;
                        existingMarket.QuoteCoin = quoteCoin;
                        existingMarket.MakerFeeRate = Convert.ToDecimal(marketDict["maker"]);
                        existingMarket.TakerFeeRate = Convert.ToDecimal(marketDict["taker"]);
                        existingMarket.MinTradeUSDT = Convert.ToDecimal(cost?["min"] ?? 0);
                        existingMarket.PricePrecision = Convert.ToDecimal(precision?["price"] ?? 0);
                        existingMarket.AmountPrecision = Convert.ToDecimal(precision?["amount"] ?? 0);
                        existingMarket.MinLever = minLever;
                        existingMarket.MaxLever = maxLever;
                        existingMarket.Type = marketType;
                        existingMarket.IsSpot = isSpot;
                        existingMarket.IsFutures = isFutures;
                        existingMarket.Time = DateTime.Now;
                    }
                    else
                    {
                        var kucoinMarket = new KuCoinMarket
                        {
                            Symbol = displaySymbol,
                            BaseCoin = baseCoin,
                            QuoteCoin = quoteCoin,
                            MakerFeeRate = Convert.ToDecimal(marketDict["maker"]),
                            TakerFeeRate = Convert.ToDecimal(marketDict["taker"]),
                            MinTradeUSDT = Convert.ToDecimal(cost?["min"] ?? 0),
                            MinLever = minLever,
                            MaxLever = maxLever,
                            PricePrecision = Convert.ToDecimal(precision?["price"] ?? 0),
                            AmountPrecision = Convert.ToDecimal(precision?["amount"] ?? 0),
                            Type = marketType,
                            IsSpot = isSpot,
                            IsFutures = isFutures,
                            Time = DateTime.Now
                        };

                        kucoinMarketsToAdd.Add(kucoinMarket);
                    }
                }

                if (kucoinMarketsToAdd.Count > 0)
                {
                    context.KuCoinMarkets.AddRange(kucoinMarketsToAdd);
                }

                await context.SaveChangesAsync().ConfigureAwait(false);

                // Clean up markets that disappeared from the exchange
                var currentSymbols = await context.KuCoinMarkets
                    .Select(m => m.Symbol)
                    .ToListAsync()
                    .ConfigureAwait(false);

                var symbolsToDelete = currentSymbols
                    .Where(s => !fetchedSymbols.Contains(s))
                    .ToList();

                if (symbolsToDelete.Count > 0)
                {
                    var marketsToDelete = context.KuCoinMarkets
                        .Where(m => symbolsToDelete.Contains(m.Symbol));

                    context.KuCoinMarkets.RemoveRange(marketsToDelete);

                    foreach (var symbolToRemove in symbolsToDelete)
                    {
                        var existingRemovedAsset = await context.KuCoinRemovedAssets
                            .FirstOrDefaultAsync(ra => ra.Symbol == symbolToRemove)
                            .ConfigureAwait(false);

                        if (existingRemovedAsset == null)
                        {
                            context.KuCoinRemovedAssets.Add(new KuCoinRemovedAsset
                            {
                                Symbol = symbolToRemove,
                                Time = DateTime.UtcNow
                            });
                        }
                    }

                    await context.SaveChangesAsync().ConfigureAwait(false);
                }

                return kucoinMarketsToAdd.Cast<object>();
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task FetchAllKuCoinAssetPricesAsync() // Deprecated
        {
            using (var scope = _scopeFactory.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<AutoSignalsDbContext>();

                var markets = await context.KuCoinMarkets.ToListAsync();
                var assetPricesToAdd = new List<KuCoinAssetPrice>();
                var assetPricesToUpdate = new List<KuCoinAssetPrice>();
                var fetchedSymbols = new HashSet<string>();

                // Cache existing asset prices to minimize DB reads
                var existingAssetPrices = await context.KuCoinAssetPrices.ToDictionaryAsync(ap => ap.Symbol);
                var existingRemovedAssets = await context.KuCoinRemovedAssets.ToDictionaryAsync(ra => ra.Symbol);

                foreach (var market in markets)
                {
                    var ticker = await _futures.fetchTicker(market.Symbol) as Dictionary<string, object>;
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
                    context.KuCoinAssetPrices.AddRange(assetPricesToAdd);
                }

                // Find symbols to delete
                var symbolsToDelete = markets.Where(m => !fetchedSymbols.Contains(m.Symbol)).ToList();

                if (symbolsToDelete.Count > 0)
                {
                    // Prepare to delete asset prices and markets
                    var assetPricesToDelete = context.KuCoinAssetPrices.Where(ap => symbolsToDelete.Any(m => m.Symbol == ap.Symbol));
                    context.KuCoinAssetPrices.RemoveRange(assetPricesToDelete);

                    // Remove symbols from KuCoinMarkets
                    context.KuCoinMarkets.RemoveRange(symbolsToDelete);

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
                            context.KuCoinRemovedAssets.Add(removedAsset);
                        }
                    }
                }

                if (assetPricesToUpdate.Count > 0)
                {
                    context.KuCoinAssetPrices.UpdateRange(assetPricesToUpdate);
                }

                int retryCount = 0;
                while (retryCount < 3)
                {
                    try
                    {
                        await context.SaveChangesAsync();
                        break; // Success, exit loop
                    }
                    catch (Exception ex)
                    {
                        retryCount++;
                        Console.WriteLine($"Error saving changes to the database (attempt {retryCount}): {ex.Message}");
                        if (ex is DbUpdateConcurrencyException concurrencyEx)
                        {
                            foreach (var entry in concurrencyEx.Entries)
                                await entry.ReloadAsync();
                        }
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
            
        }

        public async Task FetchAllKuCoinAssetPricesV2Async()
        {
            using (var scope = _scopeFactory.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<AutoSignalsDbContext>();

                var markets = await context.KuCoinMarkets.ToListAsync().ConfigureAwait(false);
                var assetPricesToAdd = new List<KuCoinAssetPrice>();
                var assetPricesToUpdate = new List<KuCoinAssetPrice>();
                var fetchedSymbols = new HashSet<string>();

                // Cache existing asset prices (keyed by Symbol_Type)
                var existingAssetPrices = await context.KuCoinAssetPrices
                    .ToDictionaryAsync(ap => $"{ap.Symbol}_{ap.Type}")
                    .ConfigureAwait(false);

                var spotMarkets = markets.Where(m => m.IsSpot).ToList();
                var futuresMarkets = markets.Where(m => m.IsFutures).ToList();

                if (spotMarkets.Any())
                {
                    await FetchKuCoinTickersByTypeAsync(
                        spotMarkets,
                        "spot",
                        existingAssetPrices,
                        assetPricesToAdd,
                        assetPricesToUpdate,
                        fetchedSymbols).ConfigureAwait(false);
                }

                if (futuresMarkets.Any())
                {
                    await FetchKuCoinTickersByTypeAsync(
                        futuresMarkets,
                        "swap",
                        existingAssetPrices,
                        assetPricesToAdd,
                        assetPricesToUpdate,
                        fetchedSymbols).ConfigureAwait(false);
                }

                if (assetPricesToAdd.Count > 0)
                {
                    context.KuCoinAssetPrices.AddRange(assetPricesToAdd);
                }

                // Remove symbols that were not fetched anymore
                var symbolsToDelete = markets.Where(m => !fetchedSymbols.Contains(m.Symbol)).ToList();
                var symbolsToRemove = symbolsToDelete.Select(m => m.Symbol).ToList();

                if (symbolsToRemove.Count > 0)
                {
                    var assetPricesToDelete = await context.KuCoinAssetPrices
                        .Where(ap => symbolsToRemove.Contains(ap.Symbol))
                        .ToListAsync()
                        .ConfigureAwait(false);

                    context.KuCoinAssetPrices.RemoveRange(assetPricesToDelete);
                    context.KuCoinMarkets.RemoveRange(symbolsToDelete);

                    foreach (var symbol in symbolsToRemove)
                    {
                        context.KuCoinRemovedAssets.Add(new KuCoinRemovedAsset
                        {
                            Symbol = symbol,
                            Time = DateTime.UtcNow
                        });
                    }
                }

                if (assetPricesToUpdate.Count > 0)
                {
                    context.KuCoinAssetPrices.UpdateRange(assetPricesToUpdate);
                }

                var retryCount = 0;
                while (retryCount < 3)
                {
                    try
                    {
                        await context.SaveChangesAsync().ConfigureAwait(false);
                        break;
                    }
                    catch (Exception ex)
                    {
                        retryCount++;
                        Console.WriteLine($"Error saving KuCoin prices V2 (attempt {retryCount}): {ex.Message}");
                        if (ex is DbUpdateConcurrencyException concurrencyEx)
                        {
                            foreach (var entry in concurrencyEx.Entries)
                                await entry.ReloadAsync();
                        }
                        if (retryCount >= 3)
                        {
                            await _errorLogService.LogErrorAsync(
                                $"Failed to save KuCoin asset prices V2 after 3 attempts: {ex.Message}",
                                ex.StackTrace,
                                nameof(FetchAllKuCoinAssetPricesV2Async)).ConfigureAwait(false);
                            throw;
                        }

                        await Task.Delay(1000).ConfigureAwait(false);
                    }
                }
            }
        }

        private async Task FetchKuCoinTickersByTypeAsync(
        List<KuCoinMarket> markets,
        string marketType,
        Dictionary<string, KuCoinAssetPrice> existingAssetPrices,
        List<KuCoinAssetPrice> assetPricesToAdd,
        List<KuCoinAssetPrice> assetPricesToUpdate,
        HashSet<string> fetchedSymbols)
        {
            // Choose the correct client
            var client = marketType == "spot" ? (ccxt.Exchange)_spot : _futures;

            int retryCount = 0;
            Dictionary<string, object> tickers = null;

            while (retryCount < 3)
            {
                try
                {
                    var symbols = markets.Select(m => m.Symbol).Distinct().ToArray();

                    // KuCoin's fetchTickers should support symbols array, but if it misbehaves
                    // you can mirror the Binance spot workaround and fetch all then filter.
                    tickers = await client.fetchTickers(symbols) as Dictionary<string, object>;

                    if (tickers != null && tickers.Count > 0)
                    {
                        break;
                    }
                }
                catch (Exception ex)
                {
                    if (ex.Message.Contains("Too many requests", StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine($"Too many requests fetching KuCoin {marketType} tickers. Retrying in 5 seconds...");
                        await Task.Delay(5000).ConfigureAwait(false);
                        retryCount++;
                    }
                    else
                    {
                        Console.WriteLine($"Error fetching KuCoin {marketType} tickers: {ex.Message} Inner: {ex.InnerException?.Message}. Response: {tickers}");
                        await _errorLogService.LogErrorAsync(
                            $"Error fetching KuCoin {marketType} tickers: {ex.Message}",
                            ex.StackTrace,
                            nameof(FetchKuCoinTickersByTypeAsync)).ConfigureAwait(false);
                        break;
                    }
                }
            }

            if (tickers == null)
            {
                Console.WriteLine($"Failed to fetch KuCoin {marketType} tickers after retries.");
                return;
            }

            foreach (var market in markets)
            {
                if (!tickers.TryGetValue(market.Symbol, out var tickerObj))
                {
                    // Optional alt key handling similar to Binance if KuCoin returns different keys
                    var altKey = market.Symbol.Replace(":", string.Empty);
                    if (!tickers.TryGetValue(altKey, out tickerObj))
                    {
                        continue;
                    }
                }

                if (tickerObj is not Dictionary<string, object> tickerDict)
                {
                    continue;
                }

                if (!tickerDict.ContainsKey("last"))
                {
                    continue;
                }

                var price = Convert.ToDecimal(tickerDict["last"]);
                var open = tickerDict.ContainsKey("open") ? Convert.ToDecimal(tickerDict["open"]) : 0;
                var high = tickerDict.ContainsKey("high") ? Convert.ToDecimal(tickerDict["high"]) : 0;
                var low = tickerDict.ContainsKey("low") ? Convert.ToDecimal(tickerDict["low"]) : 0;
                var close = tickerDict.ContainsKey("close") ? Convert.ToDecimal(tickerDict["close"]) : 0;
                var volume = tickerDict.ContainsKey("baseVolume") ? Convert.ToDecimal(tickerDict["baseVolume"]) : 0;

                var key = $"{market.Symbol}_{market.Type}";

                if (existingAssetPrices.TryGetValue(key, out var existingAssetPrice))
                {
                    if (existingAssetPrice.Price != price)
                    {
                        existingAssetPrice.Price = price;
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
                    if (!assetPricesToAdd.Any(ap => ap.Symbol == market.Symbol && ap.Type == market.Type))
                    {
                        var assetPrice = new KuCoinAssetPrice
                        {
                            Symbol = market.Symbol,
                            Type = market.Type,
                            Price = price,
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

                fetchedSymbols.Add(market.Symbol);
            }
        }

        public async Task<decimal?> FetchKuCoinAssetPriceAsync(string symbol)
        {
            var ticker = await _futures.fetchTicker(symbol) as Dictionary<string, object>;
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
                using (var scope = _scopeFactory.CreateScope())
                {
                    var context = scope.ServiceProvider.GetRequiredService<AutoSignalsDbContext>();

                    if (context == null)
                        throw new InvalidOperationException("Database context is not initialized.");

                    var markets = await context.KuCoinMarkets.Select(m => m.Symbol).ToListAsync();
                    if (markets == null || !markets.Any())
                    {
                        Console.WriteLine("No markets found.");
                        return;
                    }

                    var existingPrices = await context.KuCoinAssetPrices.ToDictionaryAsync(ap => ap.Symbol);
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
                        context.KuCoinAssetPrices.AddRange(assetPricesToAdd);

                    if (assetPricesToUpdate.Count > 0)
                        context.KuCoinAssetPrices.UpdateRange(assetPricesToUpdate);

                    // Find symbols to delete
                    var symbolsToDelete = markets.Except(updatedSymbols).ToList();
                    if (symbolsToDelete.Count > 0)
                    {
                        var marketsToDelete = context.KuCoinMarkets
                            .Where(m => symbolsToDelete.Contains(m.Symbol));
                        context.KuCoinMarkets.RemoveRange(marketsToDelete);

                        var assetPricesToDelete = context.KuCoinAssetPrices
                            .Where(ap => symbolsToDelete.Contains(ap.Symbol));
                        context.KuCoinAssetPrices.RemoveRange(assetPricesToDelete);

                        foreach (var symbol in symbolsToDelete)
                        {
                            if (!await context.KuCoinRemovedAssets.AnyAsync(ra => ra.Symbol == symbol))
                            {
                                context.KuCoinRemovedAssets.Add(new KuCoinRemovedAsset
                                {
                                    Symbol = symbol,
                                    Time = DateTime.UtcNow
                                });
                            }
                        }
                    }

                    try
                    {
                        await context.SaveChangesAsync();
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
                
            }
            finally
            {
                _semaphore.Release();
            }
        }


        public async Task DeleteDuplicates()
        {

            using (var scope = _scopeFactory.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<AutoSignalsDbContext>();

                var duplicateSymbols = context.KuCoinAssetPrices.GroupBy(s => s.Symbol)
                                                             .Where(g => g.Count() > 1)
                                                             .SelectMany(g => g.OrderBy(s => s.Time)
                                                                              .Skip(1));

                context.KuCoinAssetPrices.RemoveRange(duplicateSymbols);
                await context.SaveChangesAsync();
            }
            
        }

        public async Task<decimal> GetBalance(string apiKey, string apiSecret, string password)
        {
            // Placeholder for getting balance from KuCoin
            return await Task.FromResult(1000.0m); // Example balance
        }
    }
}
