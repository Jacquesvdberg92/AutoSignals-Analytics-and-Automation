namespace AutoSignals.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using ccxt;
    using Binance.Net.Clients;
    using AutoSignals.Models;
    using Microsoft.EntityFrameworkCore;
    using AutoSignals.Data;

    public class BinancePriceService : IBinanceService
    {
        private readonly ccxt.binanceusdm _futures;
        private readonly ccxt.binance _spot;
        private static readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        private readonly ErrorLogService _errorLogService;
        private readonly IServiceScopeFactory _scopeFactory;

        public BinancePriceService(string apiKey, string apiSecret, ErrorLogService errorLogService, IServiceScopeFactory scopeFactory)
        {
            _futures = new ccxt.binanceusdm(new Dictionary<string, object>
            {
                { "apiKey", apiKey },
                { "secret", apiSecret },
                { "enableRateLimit", true }
            });
            _futures.options["defaultType"] = "swap";

            _spot = new ccxt.binance(new Dictionary<string, object>
            {
                { "apiKey", apiKey },
                { "secret", apiSecret },
                { "enableRateLimit", true }
            });
            _spot.options["defaultType"] = "spot";

            _errorLogService = errorLogService;
            _scopeFactory = scopeFactory;
        }

        // Gets spot and futures USDT markets via API
        public async Task<IEnumerable<object>> GetBinanceMarketsAsync()
        {
            await _semaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                using (var scope = _scopeFactory.CreateScope())
                {
                    var context = scope.ServiceProvider.GetRequiredService<AutoSignalsDbContext>();

                    // Fetch spot and futures markets separately from the configured clients
                    var spotMarketsTask = _spot.fetchMarkets();
                    var futuresMarketsTask = _futures.fetchMarkets();

                    await Task.WhenAll(spotMarketsTask, futuresMarketsTask).ConfigureAwait(false);

                    var spotMarkets = spotMarketsTask.Result as List<object> ?? new List<object>();
                    var futuresMarkets = futuresMarketsTask.Result as List<object> ?? new List<object>();

                    // Collect futures symbols (USDT quote only) to fetch leverage tiers in bulk
                    var futuresSymbols = futuresMarkets
                        .OfType<Dictionary<string, object>>()
                        .Where(m =>
                            m.TryGetValue("quote", out var q) &&
                            q?.ToString() == "USDT" &&
                            m.TryGetValue("type", out var t) &&
                            t?.ToString() == "swap")
                        .Select(m => m["symbol"].ToString())
                        .Distinct()
                        .ToList();

                    // ccxt expects an array of symbols; get tiers for all futures symbols
                    var leverageInfo = new Dictionary<string, object>();
                    if (futuresSymbols.Count > 0)
                    {
                        var tiersResult = await _futures.fetchLeverageTiers(futuresSymbols.ToArray());
                        leverageInfo = tiersResult as Dictionary<string, object> ?? new Dictionary<string, object>();
                    }

                    // Merge all markets; we will later filter by type (spot / swap) and USDT quote
                    var markets = new List<object>();
                    markets.AddRange(spotMarkets);
                    markets.AddRange(futuresMarkets);

                    if (markets.Count == 0)
                    {
                        Console.WriteLine("Failed to fetch Binance markets.");
                        return Enumerable.Empty<object>();
                    }

                    var binanceMarketsToAdd = new List<BinanceMarket>();
                    var fetchedSymbols = new HashSet<string>(); // track actual stored Symbol values

                    foreach (var market in markets)
                    {
                        if (market is not Dictionary<string, object> marketDict)
                        {
                            continue;
                        }

                        if (!marketDict.TryGetValue("quote", out var quote) ||
                            quote?.ToString() != "USDT")
                        {
                            continue;
                        }

                        var marketType = marketDict.TryGetValue("type", out var typeVal)
                            ? typeVal?.ToString()
                            : string.Empty;

                        // ccxt binance/usdm structure
                        // spot  : type == "spot"
                        // futures: type == "swap"
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
                        var precision = marketDict.TryGetValue("precision", out var precObj)
                            ? precObj as Dictionary<string, object>
                            : null;

                        // ccxt:
                        //  - "symbol": "BTC/USDT" (display)
                        //  - "id"    : "BTCUSDT" for spot, "BTCUSDT" / "BTCUSDT_240927" etc. for futures on some exchanges
                        var displaySymbol = marketDict["symbol"].ToString();
                        var id = marketDict["id"].ToString();

                        // Decide how to store Symbol:
                        //  - Spot   : "BTC/USDT"
                        //  - Futures: "BTC/USDT:USDT"
                        //var storedSymbol = marketType == "spot"
                        //    ? displaySymbol                     // BTC/USDT
                        //    : $"{displaySymbol}:USDT";          // BTC/USDT:USDT
                        var isSpot = marketType == "spot";
                        var isFutures = marketType == "swap";

                        fetchedSymbols.Add(displaySymbol);

                        int minLever = 0;
                        int maxLever = 0;

                        if (isFutures)
                        {
                            // Min is always 1, max comes from Tier 1 (first tier) if available
                            minLever = 1;
                            var symbol = marketDict["symbol"].ToString();

                            if (leverageInfo.TryGetValue(symbol, out var tierObj) &&
                                tierObj is List<object> tierList &&
                                tierList.Count > 0 &&
                                tierList[0] is Dictionary<string, object> firstTier &&
                                firstTier.TryGetValue("maxLeverage", out var maxLevObj))
                            {
                                maxLever = Convert.ToInt32(maxLevObj);
                            }
                            else if (marketDict.TryGetValue("info", out var infoObj) &&
                                     infoObj is Dictionary<string, object> infoDict &&
                                     infoDict.TryGetValue("leverageFilter", out var leverageFilterObj) &&
                                     leverageFilterObj is Dictionary<string, object> leverageFilterDict &&
                                     leverageFilterDict.TryGetValue("maxLeverage", out var maxLevFallback))
                            {
                                // Fallback: use maxLeverage from leverageFilter if Tier 1 info is not available
                                maxLever = Convert.ToInt32(maxLevFallback);
                            }
                            else
                            {
                                // Last-resort default if neither source is available
                                maxLever = 10;
                            }
                        }

                        var existingMarket = await context.BinanceMarkets
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
                            existingMarket.Time = DateTime.Now;

                        }
                        else
                        {
                            var binanceMarket = new BinanceMarket
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

                            binanceMarketsToAdd.Add(binanceMarket);
                        }
                    }

                    if (binanceMarketsToAdd.Count > 0)
                    {
                        context.BinanceMarkets.AddRange(binanceMarketsToAdd);
                    }

                    await context.SaveChangesAsync().ConfigureAwait(false);

                    // Clean up markets that disappeared from the exchange
                    var currentSymbols = await context.BinanceMarkets
                        .Select(m => m.Symbol)
                        .ToListAsync()
                        .ConfigureAwait(false);

                    var symbolsToDelete = currentSymbols
                        .Where(s => !fetchedSymbols.Contains(s))
                        .ToList();

                    if (symbolsToDelete.Count > 0)
                    {
                        var marketsToDelete = context.BinanceMarkets
                            .Where(m => symbolsToDelete.Contains(m.Symbol));

                        context.BinanceMarkets.RemoveRange(marketsToDelete);

                        foreach (var symbolToRemove in symbolsToDelete)
                        {
                            var existingRemovedAsset = await context.BinanceRemovedAssets
                                .FirstOrDefaultAsync(ra => ra.Symbol == symbolToRemove)
                                .ConfigureAwait(false);

                            if (existingRemovedAsset == null)
                            {
                                context.BinanceRemovedAssets.Add(new BinanceRemovedAsset
                                {
                                    Symbol = symbolToRemove,
                                    Time = DateTime.UtcNow
                                });
                            }
                        }

                        await context.SaveChangesAsync().ConfigureAwait(false);
                    }

                    return binanceMarketsToAdd.Cast<object>();
                }
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task FetchAllBinanceAssetPricesAsync() // depricated
        {
            using (var scope = _scopeFactory.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<AutoSignalsDbContext>();
                // Fetch all existing markets from the database
                var markets = await context.BinanceMarkets.ToListAsync();
                var assetPricesToAdd = new List<BinanceAssetPrice>();
                var assetPricesToUpdate = new List<BinanceAssetPrice>();
                var fetchedSymbols = new HashSet<string>();

                // Cache existing asset prices to minimize DB reads
                var existingAssetPrices = await context.BinanceAssetPrices.ToDictionaryAsync(ap => ap.Symbol);
                var existingRemovedAssets = await context.BinanceRemovedAssets.ToDictionaryAsync(ra => ra.Symbol);

                foreach (var market in markets)
                {
                    // Fetch ticker data for each market
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
                                var assetPrice = new BinanceAssetPrice
                                {
                                    Symbol = market.Symbol,
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

                        // Add symbol to fetched symbols set
                        fetchedSymbols.Add(market.Symbol);
                    }
                }

                // Batch add new asset prices
                if (assetPricesToAdd.Count > 0)
                {
                    context.BinanceAssetPrices.AddRange(assetPricesToAdd);
                }

                // Find symbols to delete
                var symbolsToDelete = markets.Where(m => !fetchedSymbols.Contains(m.Symbol)).ToList();

                if (symbolsToDelete.Count > 0)
                {
                    // Prepare to delete asset prices and markets
                    var assetPricesToDelete = context.BinanceAssetPrices.Where(ap => symbolsToDelete.Any(m => m.Symbol == ap.Symbol));
                    context.BinanceAssetPrices.RemoveRange(assetPricesToDelete);

                    // Remove symbols from BinanceMarkets
                    context.BinanceMarkets.RemoveRange(symbolsToDelete);

                    // Prepare removed assets for insertion without duplicates
                    foreach (var symbol in symbolsToDelete.Select(m => m.Symbol))
                    {
                        if (!existingRemovedAssets.ContainsKey(symbol))
                        {
                            var removedAsset = new BinanceRemovedAsset
                            {
                                Symbol = symbol,
                                Time = DateTime.UtcNow
                            };
                            context.BinanceRemovedAssets.Add(removedAsset);
                        }
                    }
                }

                if (assetPricesToUpdate.Count > 0)
                {
                    context.BinanceAssetPrices.UpdateRange(assetPricesToUpdate);
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

        public async Task FetchAllBinanceAssetPricesV2Async()
        {
            using (var scope = _scopeFactory.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<AutoSignalsDbContext>();

                var markets = await context.BinanceMarkets.ToListAsync().ConfigureAwait(false);
                var assetPricesToAdd = new List<BinanceAssetPrice>();
                var assetPricesToUpdate = new List<BinanceAssetPrice>();
                var fetchedSymbols = new HashSet<string>();

                // Cache existing asset prices (keyed by Symbol_Type to distinguish spot/futures if needed)
                var existingAssetPrices = await context.BinanceAssetPrices
                    .ToDictionaryAsync(ap => $"{ap.Symbol}_{ap.Type}")
                    .ConfigureAwait(false);
                

                // Group markets by type (spot vs futures/swap)
                var spotMarkets = markets.Where(m => m.IsSpot).ToList();
                var futuresMarkets = markets.Where(m => m.IsFutures).ToList();

                // Batch fetch spot tickers
                if (spotMarkets.Any())
                {
                    await FetchBinanceTickersByTypeAsync(
                        spotMarkets,
                        "spot",
                        existingAssetPrices,
                        assetPricesToAdd,
                        assetPricesToUpdate,
                        fetchedSymbols).ConfigureAwait(false);
                }

                // Batch fetch futures (swap) tickers
                if (futuresMarkets.Any())
                {
                    await FetchBinanceTickersByTypeAsync(
                        futuresMarkets,
                        "swap",
                        existingAssetPrices,
                        assetPricesToAdd,
                        assetPricesToUpdate,
                        fetchedSymbols).ConfigureAwait(false);
                }

                if (assetPricesToAdd.Count > 0)
                {
                    context.BinanceAssetPrices.AddRange(assetPricesToAdd);
                }

                // Remove symbols that were not fetched anymore
               var symbolsToDelete = markets.Where(m => !fetchedSymbols.Contains(m.Symbol)).ToList();
               var symbolsToRemove = symbolsToDelete.Select(m => m.Symbol).ToList();

                if (symbolsToRemove.Count > 0)
                {
                    var assetPricesToDelete = await context.BinanceAssetPrices
                        .Where(ap => symbolsToRemove.Contains(ap.Symbol))
                        .ToListAsync()
                        .ConfigureAwait(false);

                    context.BinanceAssetPrices.RemoveRange(assetPricesToDelete);
                    context.BinanceMarkets.RemoveRange(symbolsToDelete);

                    foreach (var symbol in symbolsToRemove)
                    {
                        context.BinanceRemovedAssets.Add(new BinanceRemovedAsset
                        {
                            Symbol = symbol,
                            Time = DateTime.UtcNow
                        });

                    }
                }

                if (assetPricesToUpdate.Count > 0)
                {
                    context.BinanceAssetPrices.UpdateRange(assetPricesToUpdate);
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
                        Console.WriteLine($"Error saving Binance prices V2 (attempt {retryCount}): {ex.Message}");
                        if (retryCount >= 3)
                        {
                            await _errorLogService.LogErrorAsync(
                                $"Failed to save Binance asset prices V2 after 3 attempts: {ex.Message}",
                                ex.StackTrace,
                                nameof(FetchAllBinanceAssetPricesV2Async)).ConfigureAwait(false);
                            throw;
                        }

                        await Task.Delay(1000).ConfigureAwait(false);
                    }
                }
            }
        }

        private async Task FetchBinanceTickersByTypeAsync(
            List<BinanceMarket> markets,
            string marketType,
            Dictionary<string, BinanceAssetPrice> existingAssetPrices,
            List<BinanceAssetPrice> assetPricesToAdd,
            List<BinanceAssetPrice> assetPricesToUpdate,
            HashSet<string> fetchedSymbols)
        {
            var client = marketType == "spot" ? (ccxt.Exchange)_spot : _futures;

            int retryCount = 0;
            Dictionary<string, object> tickers = null;

            while (retryCount < 3)
            {
                try
                {
                    var symbols = markets.Select(m => m.Symbol).Distinct().ToArray();


                    if (marketType == "spot")
                    {
                        tickers = await client.fetchTickers() as Dictionary<string, object>;
                        var filterTickers = new Dictionary<string, object>();
                        foreach (var symbol in symbols)
                        {
                            if (tickers.TryGetValue(symbol, out var tickerObj))
                            {
                                filterTickers[symbol] = tickerObj;
                            }
                        }
                        // Use filtered tickers, for some reason fetchTickers with symbols array does not work for spot
                        // (but works for futures)
                        // Unable to cast object of type 'System.String' to type 'System.Collections.Generic.IDictionary`2[System.String,System.Object]'.So we fetch all and filter manually
                        tickers = filterTickers;
                    }
                    else
                    {
                        tickers = await client.fetchTickers(symbols) as Dictionary<string, object>;
                    }
                       
                    
                    if (tickers != null && tickers.Count > 0)
                    {
                        break;
                    }
                }
                catch (Exception ex)
                {
                    if (ex.Message.Contains("Too many requests", StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine($"Too many requests fetching Binance {marketType} tickers. Retrying in 5 seconds...");
                        await Task.Delay(5000).ConfigureAwait(false);
                        retryCount++;
                    }
                    else
                    {
                        Console.WriteLine($"Error fetching Binance {marketType} tickers: {ex.Message} Inner: {ex.InnerException?.Message}. Response: {tickers}");
                        await _errorLogService.LogErrorAsync(
                            $"Error fetching Binance {marketType} tickers: {ex.Message}",
                            ex.StackTrace,ex.InnerException?.Message,
                            nameof(FetchBinanceTickersByTypeAsync)).ConfigureAwait(false);
                        break;
                    }
                }
            }

            if (tickers == null)
            {
                Console.WriteLine($"Failed to fetch Binance {marketType} tickers after retries.");
                return;
            }

            foreach (var market in markets)
            {
                // Try direct key
                if (!tickers.TryGetValue(market.Symbol, out var tickerObj))
                {
                    // Try alternative key forms if needed, e.g. for futures:
                    // "BTCUSDT" or "BTCUSDT:USDT" vs "BTC/USDT"
                    var altKey = market.Symbol.Replace(":", string.Empty);    // BTCUSDT:USDT -> BTCUSDT
                    if (!tickers.TryGetValue(altKey, out tickerObj))
                    {
                        continue;
                    }
                }

                // Ensure it really is a dict
                if (tickerObj is not Dictionary<string, object> tickerDict)
                {
                    // Skip entries that are strings / codes / info
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
                        var assetPrice = new BinanceAssetPrice
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



        public async Task<decimal?> FetchBinanceAssetPriceAsync(string symbol)
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
                using var scope = _scopeFactory.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<AutoSignalsDbContext>();

                // Load all markets
                var markets = await context.BitgetMarkets
                    .Where(m => m.IsFutures)
                    .ToListAsync();

                if (context == null)
                    throw new InvalidOperationException("Database context is not initialized.");

                if (markets == null || !markets.Any())
                {
                    Console.WriteLine("No markets found.");
                    return;
                }

                var existingPrices = await context.BinanceAssetPrices
                    .ToDictionaryAsync(ap => ap.Symbol);

                var client = new BinanceSocketClient();
                var assetPricesToAdd = new List<BinanceAssetPrice>();
                var assetPricesToUpdate = new List<BinanceAssetPrice>();
                var updatedSymbols = new HashSet<string>();
                var processedSymbols = new HashSet<string>();
                object listLock = new object();

                foreach (var symbol in markets)
                {
                    var result = await client.UsdFuturesApi.ExchangeData.SubscribeToTickerUpdatesAsync(symbol.Symbol, data =>
                    {
                        if (data?.Data != null)
                        {
                            var price = data.Data.LastPrice;
                            var open = data.Data.OpenPrice;
                            var high = data.Data.HighPrice;
                            var low = data.Data.LowPrice;
                            var close = data.Data.LastPrice;
                            var volume = data.Data.Volume;

                            if (price != null)
                            {
                                lock (listLock)
                                {
                                    if (processedSymbols.Contains(symbol.Symbol))
                                        return; // Already handled, skip

                                    if (existingPrices.TryGetValue(symbol.Symbol, out var existingAssetPrice))
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
                                            processedSymbols.Add(symbol.Symbol);
                                        }
                                    }
                                    else
                                    {
                                        assetPricesToAdd.Add(new BinanceAssetPrice
                                        {
                                            Symbol = symbol.Symbol,
                                            Price = price,
                                            Open = open,
                                            High = high,
                                            Low = low,
                                            Close = close,
                                            Volume = volume,
                                            Time = DateTime.UtcNow
                                        });
                                        
                                    }
                                    processedSymbols.Add(symbol.Symbol);
                                    updatedSymbols.Add(symbol.Symbol);
                                }
                            }
                        }
                    });

                    if (!result.Success)
                        Console.WriteLine($"Failed to subscribe to ticker updates for symbol: {symbol.Symbol}");
                }

                // Let updates accumulate (you could replace this with Task.Delay(-1) for long-running mode)
                // await Task.Delay(10000); 

                await client.UsdFuturesApi.UnsubscribeAllAsync();

                // --- DB operations (single-threaded, safe) ---
                // Ensure only unique asset prices are added to prevent SQL unique constraint violations.
                // This filters out any symbols that already exist in the database or are duplicated in the add list.
                if (assetPricesToAdd.Count > 0)
                {
                    var existingSymbols = await context.BinanceAssetPrices.Select(ap => ap.Symbol).ToListAsync();
                    assetPricesToAdd = assetPricesToAdd
                        .Where(ap => !existingSymbols.Contains(ap.Symbol))
                        .GroupBy(ap => ap.Symbol)
                        .Select(g => g.First())
                        .ToList();

                    context.BinanceAssetPrices.AddRange(assetPricesToAdd);
                }

                if (assetPricesToUpdate.Count > 0)
                    context.BinanceAssetPrices.UpdateRange(assetPricesToUpdate);

                // Find symbols to delete
                // Identify symbols that never received updates
                //var symbolsToDelete = futuresSymbols
                //    .Where(s => !updatedSymbols.ContainsKey(s))
                //    .ToList();

                //if (symbolsToDelete.Count > 0)
                //{
                //    var marketsToDelete = context.BitgetMarkets
                //        .Where(m => symbolsToDelete.Contains(m.Symbol));
                //    context.BitgetMarkets.RemoveRange(marketsToDelete);

                //    var assetPricesToDelete = await context.BitgetAssetPrices
                //        .Where(ap => symbolsToDelete.Contains(ap.Symbol))
                //        .ToListAsync();
                //    context.BitgetAssetPrices.RemoveRange(assetPricesToDelete);

                //    foreach (var symbol in symbolsToDelete)
                //    {
                //        if (!await context.BitgetRemovedAssets.AnyAsync(r => r.Symbol == symbol))
                //        {
                //            context.BitgetRemovedAssets.Add(new BitgetRemovedAsset
                //            {
                //                Symbol = symbol,
                //                Time = DateTime.UtcNow
                //            });
                //        }
                //    }
                //}

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

                var duplicateSymbols = context.BinanceAssetPrices.GroupBy(s => s.Symbol)
                    .Where(g => g.Count() > 1)
                    .SelectMany(g => g.OrderBy(s => s.Time).Skip(1));

                context.BinanceAssetPrices.RemoveRange(duplicateSymbols);
                await context.SaveChangesAsync();
            }
        }


        public async Task<decimal> GetBalance(string apiKey, string apiSecret, string password)
        {
            // Placeholder for getting balance from Binance
            return await Task.FromResult(1000.0m); // Example balance
        }

    }
}
