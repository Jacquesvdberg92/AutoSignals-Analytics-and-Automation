namespace AutoSignals.Services
{
    using AutoSignals.Data;
    using AutoSignals.Models;
    using Bybit.Net.Clients;
    using Microsoft.EntityFrameworkCore;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using static System.Formats.Asn1.AsnWriter;

    public class BybitPriceService : IBybitService
    {
        private readonly ccxt.bybit _bybit;
        private static readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        private readonly ErrorLogService _errorLogService;
        private readonly IServiceScopeFactory _scopeFactory;

        public BybitPriceService(string apiKey, string apiSecret, ErrorLogService errorLogService, IServiceScopeFactory scopeFactory)
        {
            _bybit = new ccxt.bybit(new Dictionary<string, object>
            {
                { "apiKey", apiKey },
                { "secret", apiSecret },
                { "enableRateLimit", true },
                { "options", new Dictionary<string, object>() }
            });
            _errorLogService = errorLogService;
            _scopeFactory = scopeFactory;

        }

        public async Task<IEnumerable<object>> GetBybitMarketsAsync()
        {
            await _semaphore.WaitAsync();
            try
            {
                using (var scope = _scopeFactory.CreateScope())
                {
                    var context = scope.ServiceProvider.GetRequiredService<AutoSignalsDbContext>();

                    // Let ccxt return all market types, we will filter in code
                    var markets = await _bybit.fetchMarkets(new Dictionary<string, object>()) as List<object>;

                    if (markets == null)
                    {
                        Console.WriteLine("Failed to fetch Bybit markets.");
                        return Enumerable.Empty<object>();
                    }

                    var usdtMarkets = new List<BybitMarket>();
                    var fetchedSymbols = new HashSet<string>();

                    foreach (var market in markets)
                    {
                        if (market is not Dictionary<string, object> marketDict)
                        {
                            continue;
                        }

                        // Only USDT-quoted markets
                        if (!marketDict.TryGetValue("quote", out var quoteObj) ||
                            quoteObj?.ToString() != "USDT")
                        {
                            continue;
                        }

                        // Type may be "spot", "swap", "future", etc.
                        var marketType = marketDict.TryGetValue("type", out var typeObj)
                            ? typeObj?.ToString()
                            : string.Empty;

                        if (marketType != "spot" && marketType != "swap")
                        {
                            continue;
                        }

                        // Spot / futures flags – be defensive about presence
                        var isSpot = marketDict.TryGetValue("spot", out var spotFlag) &&
                                     spotFlag?.ToString().Equals("true", StringComparison.OrdinalIgnoreCase) == true;

                        var isFutures = marketDict.TryGetValue("swap", out var swapFlag) &&
                                        swapFlag?.ToString().Equals("true", StringComparison.OrdinalIgnoreCase) == true;

                        // Use Bybit's id as the DB symbol (your existing schema does this)
                        var id = marketDict.TryGetValue("id", out var idObj)
                            ? idObj?.ToString()
                            : null;
                        var symbol = marketDict.TryGetValue("symbol", out var symbolObj)
                            ? symbolObj?.ToString()
                            : null;

                        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(symbol))
                        {
                            continue;
                        }

                        // Limits / precision – guard against null/missing
                        var limits = marketDict.TryGetValue("limits", out var limitsObj)
                            ? limitsObj as Dictionary<string, object>
                            : null;
                        var cost = limits != null && limits.TryGetValue("cost", out var costObj)
                            ? costObj as Dictionary<string, object>
                            : null;
                        var leverageLimits = limits != null && limits.TryGetValue("leverage", out var levObj)
                            ? levObj as Dictionary<string, object>
                            : null;

                        var precision = marketDict.TryGetValue("precision", out var precObj)
                            ? precObj as Dictionary<string, object>
                            : null;

                        decimal minTradeUsdt = 0;
                        if (cost != null && cost.TryGetValue("min", out var minCostObj) && minCostObj != null)
                        {
                            minTradeUsdt = Convert.ToDecimal(minCostObj);
                        }

                        decimal pricePrecision = 0;
                        decimal amountPrecision = 0;

                        if (precision != null)
                        {
                            if (precision.TryGetValue("price", out var pricePrecObj) && pricePrecObj != null)
                            {
                                pricePrecision = Convert.ToDecimal(pricePrecObj);
                            }

                            if (precision.TryGetValue("amount", out var amountPrecObj) && amountPrecObj != null)
                            {
                                amountPrecision = Convert.ToDecimal(amountPrecObj);
                            }
                        }

                        int minLever = 1;
                        int maxLever = 1;

                        if (leverageLimits != null)
                        {
                            if (leverageLimits.TryGetValue("min", out var levMinObj) && levMinObj != null)
                            {
                                minLever = Convert.ToInt32(Math.Floor(Convert.ToDecimal(levMinObj)));
                            }

                            if (leverageLimits.TryGetValue("max", out var levMaxObj) && levMaxObj != null)
                            {
                                maxLever = Convert.ToInt32(Math.Floor(Convert.ToDecimal(levMaxObj)));
                            }
                        }

                        // Track fetched symbol by symbol (your DB uses id in BybitMarket.Symbol)
                        fetchedSymbols.Add(symbol);

                        var existingMarket = await context.BybitMarkets
                            .FirstOrDefaultAsync(m => m.Symbol == symbol && m.Type == marketType);

                        if (existingMarket != null)
                        {
                            existingMarket.BaseCoin = marketDict["base"].ToString();
                            existingMarket.QuoteCoin = marketDict["quote"].ToString();
                            existingMarket.MakerFeeRate = Convert.ToDecimal(marketDict["maker"]);
                            existingMarket.TakerFeeRate = Convert.ToDecimal(marketDict["taker"]);
                            existingMarket.MinTradeUSDT = minTradeUsdt;
                            existingMarket.MinLever = minLever;
                            existingMarket.MaxLever = maxLever;
                            existingMarket.PricePrecision = pricePrecision;
                            existingMarket.AmountPrecision = amountPrecision;
                            existingMarket.Type = marketType;
                            existingMarket.IsFutures = isFutures;
                            existingMarket.IsSpot = isSpot;
                            existingMarket.Time = DateTime.Now;
                        }
                        else
                        {
                            var bybitMarket = new BybitMarket
                            {
                                Symbol = symbol,
                                BaseCoin = marketDict["base"].ToString(),
                                QuoteCoin = marketDict["quote"].ToString(),
                                MakerFeeRate = Convert.ToDecimal(marketDict["maker"]),
                                TakerFeeRate = Convert.ToDecimal(marketDict["taker"]),
                                MinTradeUSDT = minTradeUsdt,
                                MinLever = minLever,
                                MaxLever = maxLever,
                                PricePrecision = pricePrecision,
                                AmountPrecision = amountPrecision,
                                Type = marketType,
                                IsFutures = isFutures,
                                IsSpot = isSpot,
                                Time = DateTime.Now
                            };
                            usdtMarkets.Add(bybitMarket);
                        }
                    }

                    // Save new markets
                    if (usdtMarkets.Count > 0)
                    {
                        context.BybitMarkets.AddRange(usdtMarkets);
                    }

                    await context.SaveChangesAsync();

                    // Find symbols to delete
                    var currentSymbols = await context.BitgetMarkets.Select(m => m.Symbol).ToListAsync();
                    var symbolsToDelete = currentSymbols.Except(fetchedSymbols).ToList();

                    if (symbolsToDelete.Count > 0)
                    {
                        // Delete from BitgetMarkets
                        var marketsToDelete = context.BitgetMarkets.Where(m => symbolsToDelete.Contains(m.Symbol));
                        context.BitgetMarkets.RemoveRange(marketsToDelete);

                        // Insert into BitgetRemovedAssets
                        foreach (var symbol in symbolsToDelete)
                        {
                            var existingRemovedAsset = await context.BitgetRemovedAssets.FirstOrDefaultAsync(ra => ra.Symbol == symbol);
                            if (existingRemovedAsset == null)
                            {
                                var removedAsset = new BitgetRemovedAsset
                                {
                                    Symbol = symbol,
                                    Time = DateTime.UtcNow
                                };
                                context.BitgetRemovedAssets.Add(removedAsset);
                            }
                        }

                        await context.SaveChangesAsync();
                    }

                    return usdtMarkets.Cast<object>();
                }
            }
            finally
            {
                _semaphore.Release();
            }
        }


        public async Task FetchAllBybitAssetPricesAsync() // depricated
        {
            using (var scope = _scopeFactory.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<AutoSignalsDbContext>();

                // Fetch all existing markets from the database
                var markets = await context.BybitMarkets.ToListAsync();
                var assetPricesToAdd = new List<BybitAssetPrice>();
                var assetPricesToUpdate = new List<BybitAssetPrice>();
                var fetchedSymbols = new HashSet<string>();

                // Cache existing asset prices to minimize DB reads
                var existingAssetPrices = await context.BybitAssetPrices.ToDictionaryAsync(ap => ap.Symbol);
                var existingRemovedAssets = await context.BybitRemovedAssets.ToDictionaryAsync(ra => ra.Symbol);

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
                    context.BybitAssetPrices.AddRange(assetPricesToAdd);
                }

                // Find symbols to delete
                var symbolsToDelete = markets.Where(m => !fetchedSymbols.Contains(m.Symbol)).ToList();

                if (symbolsToDelete.Count > 0)
                {
                    // Prepare to delete asset prices and markets
                    var assetPricesToDelete = context.BybitAssetPrices.Where(ap => symbolsToDelete.Any(m => m.Symbol == ap.Symbol));
                    context.BybitAssetPrices.RemoveRange(assetPricesToDelete);

                    // Remove symbols from BybitMarkets
                    context.BybitMarkets.RemoveRange(symbolsToDelete);

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
                            context.BybitRemovedAssets.Add(removedAsset);
                        }
                    }
                }

                if (assetPricesToUpdate.Count > 0)
                {
                    context.BybitAssetPrices.UpdateRange(assetPricesToUpdate);
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

        public async Task FetchAllBybitAssetPricesV2Async()
        {
            using (var scope = _scopeFactory.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<AutoSignalsDbContext>();

                var markets = await context.BybitMarkets.ToListAsync();
                var assetPricesToAdd = new List<BybitAssetPrice>();
                var assetPricesToUpdate = new List<BybitAssetPrice>();
                var fetchedSymbols = new HashSet<string>();

                // Cache existing asset prices with Symbol+Type key, similar to Bitget
                var existingAssetPrices = await context.BybitAssetPrices
                    .ToDictionaryAsync(ap => $"{ap.Symbol}_{ap.Type}");
                var existingRemovedAssets = await context.BybitRemovedAssets
                    .ToDictionaryAsync(ra => ra.Symbol);

                var spotMarkets = markets.Where(m => m.IsSpot).ToList();
                var futuresMarkets = markets.Where(m => m.IsFutures).ToList();

                if (spotMarkets.Any())
                {
                    await FetchTickersByTypeAsync(
                        spotMarkets,
                        "spot",
                        existingAssetPrices,
                        assetPricesToAdd,
                        assetPricesToUpdate,
                        fetchedSymbols);
                }

                if (futuresMarkets.Any())
                {
                    // Bybit futures in ccxt usually map to "swap"
                    await FetchTickersByTypeAsync(
                        futuresMarkets,
                        "swap",
                        existingAssetPrices,
                        assetPricesToAdd,
                        assetPricesToUpdate,
                        fetchedSymbols);
                }

                if (assetPricesToAdd.Count > 0)
                {
                    context.BybitAssetPrices.AddRange(assetPricesToAdd);
                }

                var symbolsToDelete = markets.Where(m => !fetchedSymbols.Contains(m.Symbol)).ToList();
                var symbolsToRemove = symbolsToDelete.Select(m => m.Symbol).ToList();

                if (symbolsToRemove.Count > 0)
                {
                    var assetPricesToDelete = await context.BybitAssetPrices
                        .Where(ap => symbolsToRemove.Contains(ap.Symbol))
                        .ToListAsync();
                    context.BybitAssetPrices.RemoveRange(assetPricesToDelete);

                    context.BybitMarkets.RemoveRange(symbolsToDelete);

                    foreach (var symbol in symbolsToRemove)
                    {
                        if (!existingRemovedAssets.ContainsKey(symbol))
                        {
                            context.BybitRemovedAssets.Add(new BybitRemovedAsset
                            {
                                Symbol = symbol,
                                Time = DateTime.UtcNow
                            });
                        }
                    }
                }

                if (assetPricesToUpdate.Count > 0)
                {
                    context.BybitAssetPrices.UpdateRange(assetPricesToUpdate);
                }

                int retryCount = 0;
                while (retryCount < 3)
                {
                    try
                    {
                        await context.SaveChangesAsync();
                        break;
                    }
                    catch (Exception ex)
                    {
                        retryCount++;
                        Console.WriteLine($"Error saving changes to the database (attempt {retryCount}): {ex.Message}");
                        if (retryCount >= 3)
                        {
                            await _errorLogService.LogErrorAsync(
                                $"Failed to save Bybit asset prices V2 after 3 attempts: {ex.Message}",
                                ex.StackTrace,
                                nameof(FetchAllBybitAssetPricesV2Async));
                            Console.WriteLine($"Final error saving changes to the database: {ex.Message}");
                            Console.WriteLine($"Stack Trace: {ex.StackTrace}");
                            Console.WriteLine($"Inner Exception: {ex.InnerException?.Message}");
                            throw;
                        }
                        await Task.Delay(1000);
                    }
                }
            }
        }

        private async Task FetchTickersByTypeAsync(
            List<BybitMarket> markets,
            string marketType,
            Dictionary<string, BybitAssetPrice> existingAssetPrices,
            List<BybitAssetPrice> assetPricesToAdd,
            List<BybitAssetPrice> assetPricesToUpdate,
            HashSet<string> fetchedSymbols)
        {
            int retryCount = 0;
            Dictionary<string, object> tickers = null;

            while (retryCount < 3)
            {
                try
                {
                    // ccxt: fetch all tickers for given type
                    tickers = await _bybit.fetchTickers(null, new Dictionary<string, object>
                    {
                        { "type", marketType }
                    }) as Dictionary<string, object>;

                    if (tickers != null)
                    {
                        break;
                    }
                }
                catch (Exception ex)
                {
                    if (ex.Message.Contains("Too many requests", StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine($"Too many requests fetching {marketType} tickers. Retrying in 5 seconds...");
                        await Task.Delay(5000);
                        retryCount++;
                    }
                    else
                    {
                        Console.WriteLine($"Error fetching {marketType} tickers: {ex.Message}");
                        await _errorLogService.LogErrorAsync(
                            $"Error fetching {marketType} tickers: {ex.Message}",
                            ex.StackTrace,
                            nameof(FetchTickersByTypeAsync));
                        break;
                    }
                }
            }

            if (tickers == null)
            {
                Console.WriteLine($"Failed to fetch {marketType} tickers after retries.");
                return;
            }

            foreach (var market in markets)
            {
                if (tickers.TryGetValue(market.Symbol, out var tickerObj) &&
                    tickerObj is Dictionary<string, object> ticker)
                {
                    if (ticker.ContainsKey("last"))
                    {
                        var price = Convert.ToDecimal(ticker["last"]);
                        var open = ticker.ContainsKey("open") ? Convert.ToDecimal(ticker["open"]) : 0;
                        var high = ticker.ContainsKey("high") ? Convert.ToDecimal(ticker["high"]) : 0;
                        var low = ticker.ContainsKey("low") ? Convert.ToDecimal(ticker["low"]) : 0;
                        var close = ticker.ContainsKey("close") ? Convert.ToDecimal(ticker["close"]) : 0;
                        var volume = ticker.ContainsKey("baseVolume") ? Convert.ToDecimal(ticker["baseVolume"]) : 0;

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
                                var assetPrice = new BybitAssetPrice
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
            using (var scope = _scopeFactory.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<AutoSignalsDbContext>();

                await _semaphore.WaitAsync();
                try
                {
                    if (context == null)
                    {
                        throw new InvalidOperationException("Database context is not initialized.");
                    }

                    var markets = await context.BybitMarkets.Select(m => m.Symbol).ToListAsync();
                    if (markets == null || !markets.Any())
                    {
                        Console.WriteLine("No markets found.");
                        return;
                    }

                    var existingPrices = await context.BybitAssetPrices.ToDictionaryAsync(ap => ap.Symbol);
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
                        context.BybitAssetPrices.AddRange(assetPricesToAdd);
                    }

                    if (assetPricesToUpdate.Count > 0)
                    {
                        context.BybitAssetPrices.UpdateRange(assetPricesToUpdate);
                    }

                    // Find symbols to delete
                    var symbolsToDelete = markets.Except(updatedSymbols).ToList();

                    if (symbolsToDelete.Count > 0)
                    {
                        // Delete from BybitMarkets
                        var marketsToDelete = context.BybitMarkets.Where(m => symbolsToDelete.Contains(m.Symbol));
                        context.BybitMarkets.RemoveRange(marketsToDelete);

                        // Delete from BybitAssetPrices
                        var assetPricesToDelete = context.BybitAssetPrices.Where(ap => symbolsToDelete.Contains(ap.Symbol));
                        context.BybitAssetPrices.RemoveRange(assetPricesToDelete);

                        // Insert into BybitRemovedAssets
                        foreach (var symbol in symbolsToDelete)
                        {
                            var existingRemovedAsset = await context.BybitRemovedAssets.FirstOrDefaultAsync(ra => ra.Symbol == symbol);
                            if (existingRemovedAsset == null)
                            {
                                var removedAsset = new BybitRemovedAsset
                                {
                                    Symbol = symbol,
                                    Time = DateTime.UtcNow
                                };
                                context.BybitRemovedAssets.Add(removedAsset);
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
                finally
                {
                    _semaphore.Release();
                }
            }
            
        }



        public async Task DeleteDuplicates()
        {
            using (var scope = _scopeFactory.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<AutoSignalsDbContext>();

                var duplicateSymbols = context.BybitAssetPrices.GroupBy(s => s.Symbol)
                                                             .Where(g => g.Count() > 1)
                                                             .SelectMany(g => g.OrderBy(s => s.Time)
                                                                              .Skip(1));

                context.BybitAssetPrices.RemoveRange(duplicateSymbols);
                await context.SaveChangesAsync();
            }

             
        }


        public async Task<decimal> GetBalance(string apiKey, string apiSecret, string password)
        {
            // Placeholder for getting balance from Bybit
            return await Task.FromResult(1000.0m); // Example balance
        }
    } 
}
