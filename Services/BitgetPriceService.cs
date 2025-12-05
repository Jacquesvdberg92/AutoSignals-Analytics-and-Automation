namespace AutoSignals.Services
{
    using AutoSignals.Data;
    using AutoSignals.Models;
    using AutoSignals.ViewModels;
    using Bitget.Net.Clients;
    using Microsoft.EntityFrameworkCore;
    using Newtonsoft.Json;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;

    public class BitgetPriceService : IBitgetService
    {
        private readonly ccxt.bitget _bitget;
        private readonly AutoSignalsDbContext _context;
        private static readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        private readonly ErrorLogService _errorLogService;
        private readonly IServiceScopeFactory _scopeFactory;

        public BitgetPriceService(string apiKey, string apiSecret, string password, ErrorLogService errorLogService, IServiceScopeFactory scopeFactory)
        {
            _bitget = new ccxt.bitget(new Dictionary<string, object>
            {
                { "apiKey", apiKey },
                { "secret", apiSecret },
                { "password", password }
            });
            // Ensure all subsequent calls target derivatives endpoints
            _bitget.options["defaultType"] = "swap";
            _errorLogService = errorLogService;
            _scopeFactory = scopeFactory;
        }

        public async Task<IEnumerable<object>> GetBitgetMarketsAsync()
        {
            await _semaphore.WaitAsync();
            try
            {

                using (var scope = _scopeFactory.CreateScope())
                {
                    var context = scope.ServiceProvider.GetRequiredService<AutoSignalsDbContext>();

                    var markets = await _bitget.fetchMarkets(new Dictionary<string, object> { { "type", "swap" } }) as List<object>;

                    if (markets == null)
                    {
                        Console.WriteLine("Failed to fetch futures markets.");
                        return Enumerable.Empty<object>();
                    }

                    var usdtSwapMarkets = new List<BitgetMarket>();
                    var fetchedSymbols = new HashSet<string>();

                    foreach (var market in markets)
                    {
                        if (market is Dictionary<string, object> marketDict &&
                            marketDict.TryGetValue("quote", out var quote) && quote.ToString() == "USDT" &&
                            marketDict.TryGetValue("type", out var type) && type.ToString() == "swap")
                        {
                            var limits = marketDict["limits"] as Dictionary<string, object>;
                            var cost = limits["cost"] as Dictionary<string, object>;
                            var leverage = limits["leverage"] as Dictionary<string, object>;
                            var precision = marketDict["precision"] as Dictionary<string, object>;

                            var symbol = marketDict["id"].ToString();
                            var existingMarket = await context.BitgetMarkets.FirstOrDefaultAsync(m => m.Symbol == symbol);

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
                                var bitgetMarket = new BitgetMarket
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
                                usdtSwapMarkets.Add(bitgetMarket);
                            }

                            // Add symbol to fetched symbols set
                            fetchedSymbols.Add(symbol);
                        }
                    }

                    // Save new and updated markets to database
                    if (usdtSwapMarkets.Count > 0)
                    {
                        context.BitgetMarkets.AddRange(usdtSwapMarkets);
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

                    return usdtSwapMarkets.Cast<object>();
                }
                
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task FetchAllBitgetAssetPricesAsync()
        {
            using (var scope = _scopeFactory.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<AutoSignalsDbContext>();

                var markets = await context.BitgetMarkets.ToListAsync();
                var assetPricesToAdd = new List<BitgetAssetPrice>();
                var assetPricesToUpdate = new List<BitgetAssetPrice>();
                var fetchedSymbols = new HashSet<string>();

                // Cache existing asset prices to minimize DB reads
                var existingAssetPrices = await context.BitgetAssetPrices.ToDictionaryAsync(ap => ap.Symbol);
                var existingRemovedAssets = await context.BitgetRemovedAssets.ToDictionaryAsync(ra => ra.Symbol);

                foreach (var market in markets)
                {
                    var ticker = await _bitget.fetchTicker(market.Symbol) as Dictionary<string, object>;
                    //Console.WriteLine($"Fetched ticker for {market.Symbol}: {JsonConvert.SerializeObject(ticker, Formatting.Indented)}");
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
                                var assetPrice = new BitgetAssetPrice
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
                    context.BitgetAssetPrices.AddRange(assetPricesToAdd);
                }

                // Find symbols to delete
                var symbolsToDelete = markets.Where(m => !fetchedSymbols.Contains(m.Symbol)).ToList();

                if (symbolsToDelete.Count > 0)
                {
                    // Prepare to delete asset prices and markets
                    var assetPricesToDelete = context.BitgetAssetPrices.Where(ap => symbolsToDelete.Any(m => m.Symbol == ap.Symbol));
                    context.BitgetAssetPrices.RemoveRange(assetPricesToDelete);

                    // Remove symbols from BitgetMarkets
                    context.BitgetMarkets.RemoveRange(symbolsToDelete);

                    // Prepare removed assets for insertion without duplicates
                    foreach (var symbol in symbolsToDelete.Select(m => m.Symbol))
                    {
                        if (!existingRemovedAssets.ContainsKey(symbol))
                        {
                            var removedAsset = new BitgetRemovedAsset
                            {
                                Symbol = symbol,
                                Time = DateTime.UtcNow
                            };
                            context.BitgetRemovedAssets.Add(removedAsset);
                        }
                    }
                }

                if (assetPricesToUpdate.Count > 0)
                {
                    context.BitgetAssetPrices.UpdateRange(assetPricesToUpdate);
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
                            // Optionally log or rethrow after final attempt
                            await _errorLogService.LogErrorAsync(
                                $"Failed to save Bitget asset prices after 3 attempts: {ex.Message}",
                                ex.StackTrace,
                                nameof(FetchAllBitgetAssetPricesAsync)
                            );
                            Console.WriteLine($"Final error saving changes to the database: {ex.Message}");
                            Console.WriteLine($"Stack Trace: {ex.StackTrace}");
                            Console.WriteLine($"Inner Exception: {ex.InnerException?.Message}");
                            throw;
                        }
                        await Task.Delay(1000); // Wait a seconds before retrying
                    }
                }
            }
        }


        public async Task<decimal?> FetchBitgetAssetPriceAsync(string symbol)
        {
            Dictionary<string, object> ticker = null;
            int retryCount = 0;
            while (retryCount < 3)
            {
                ticker = await _bitget.fetchTicker(symbol) as Dictionary<string, object>;
                if (ticker != null && ticker.ContainsKey("last"))
                {
                    break;
                }
                else if (ticker != null && ticker.ContainsKey("message") && ticker["message"].ToString().Contains("Too many requests"))
                {
                    Console.WriteLine("Too many requests. Retrying in 5 seconds...");
                    await Task.Delay(5000);
                    retryCount++;
                }
                else
                {
                    break;
                }
            }

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

                var markets = await _context.BitgetMarkets.Select(m => m.Symbol).ToListAsync();
                if (markets == null || !markets.Any())
                {
                    Console.WriteLine("No markets found.");
                    return;
                }

                var existingPrices = await _context.BitgetAssetPrices.ToDictionaryAsync(ap => ap.Symbol);
                var client = new BitgetSocketClient();
                var assetPricesToAdd = new List<BitgetAssetPrice>();
                var assetPricesToUpdate = new List<BitgetAssetPrice>();
                var updatedSymbols = new HashSet<string>();

                foreach (var symbol in markets)
                {
                    var result = await client.FuturesApi.SubscribeToTickerUpdatesAsync(symbol, data =>
                    {
                        if (data?.Data != null)
                        {
                            var price = data.Data.LastTradePrice;
                            var open = data.Data.OpenPriceUtc0;
                            var high = data.Data.HighPrice24h;
                            var low = data.Data.LowPrice24h;
                            var close = data.Data.LastTradePrice;
                            var volume = data.Data.QuoteVolume;

                            if (price != null)
                            {
                                if (existingPrices.TryGetValue(symbol, out var existingAssetPrice))
                                {
                                    if (existingAssetPrice.Price != price)
                                    {
                                        existingAssetPrice.Price = (decimal)price;
                                        existingAssetPrice.Open = (decimal)open;
                                        existingAssetPrice.High = (decimal)high;
                                        existingAssetPrice.Low = (decimal)low;
                                        existingAssetPrice.Close = (decimal)close;
                                        existingAssetPrice.Volume = (decimal)volume;
                                        existingAssetPrice.Time = DateTime.UtcNow;
                                        assetPricesToUpdate.Add(existingAssetPrice);
                                    }
                                }
                                else
                                {
                                    if (!assetPricesToAdd.Any(ap => ap.Symbol == symbol))
                                    {
                                        assetPricesToAdd.Add(new BitgetAssetPrice
                                        {
                                            Symbol = symbol,
                                            Price = (decimal)price,
                                            Open = (decimal)open,
                                            High = (decimal)high,
                                            Low = (decimal)low,
                                            Close = (decimal)close,
                                            Volume = (decimal)volume,
                                            Time = DateTime.UtcNow
                                        });
                                    }
                                }

                                updatedSymbols.Add(symbol);
                            }
                        }
                    });

                    if (!result.Success)
                        Console.WriteLine($"Failed to subscribe to ticker updates for symbol: {symbol}");
                }

                // Wait for updates to be processed (optionally keep alive)
                // await Task.Delay(-1);

                await client.FuturesApi.UnsubscribeAllAsync();

                // After all updates, perform DB operations
                if (assetPricesToAdd.Count > 0)
                {
                    _context.BitgetAssetPrices.AddRange(assetPricesToAdd);
                }

                if (assetPricesToUpdate.Count > 0)
                {
                    _context.BitgetAssetPrices.UpdateRange(assetPricesToUpdate);
                }

                // Find symbols to delete
                var symbolsToDelete = markets.Except(updatedSymbols).ToList();

                if (symbolsToDelete.Count > 0)
                {
                    
                    var marketsToDelete = _context.BitgetMarkets.Where(m => symbolsToDelete.Contains(m.Symbol));
                    _context.BitgetMarkets.RemoveRange(marketsToDelete);

                    // Delete from BitgetAssetPrices
                    var assetPricesToDelete = _context.BitgetAssetPrices.Where(ap => symbolsToDelete.Contains(ap.Symbol));
                    _context.BitgetAssetPrices.RemoveRange(assetPricesToDelete);

                    // Insert into BitgetRemovedAssets
                    foreach (var symbol in symbolsToDelete)
                    {
                        var existingRemovedAsset = await _context.BitgetRemovedAssets.FirstOrDefaultAsync(ra => ra.Symbol == symbol);
                        if (existingRemovedAsset == null)
                        {
                            var removedAsset = new BitgetRemovedAsset
                            {
                                Symbol = symbol,
                                Time = DateTime.UtcNow
                            };
                            _context.BitgetRemovedAssets.Add(removedAsset);
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
            using (var scope = _scopeFactory.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<AutoSignalsDbContext>();

                var duplicateSymbols = context.BitgetAssetPrices.GroupBy(s => s.Symbol)
                    .Where(g => g.Count() > 1)
                    .SelectMany(g => g.OrderBy(s => s.Time).Skip(1));

                context.BitgetAssetPrices.RemoveRange(duplicateSymbols);
                await context.SaveChangesAsync();
            }
        }

        public async Task<decimal> GetBalance(string apiKey, string apiSecret, string password)
        {
            var bitgetClient = new ccxt.bitget(new Dictionary<string, object>
                {
                    { "apiKey", apiKey },
                    { "secret", apiSecret },
                    { "password", password },
                });

            try
            {
                Dictionary<string, object>? response = null;
                int retryCount = 0;
                while (retryCount < 3)
                {
                    response = await bitgetClient.fetchBalance(new Dictionary<string, object>
                    {
                        //{ "apiKey", apiKey },
                        //{ "secret", apiSecret },
                        //{ "password", password },
                        { "type", "swap" }
                    }) as Dictionary<string, object>;

                    if (response != null && !response.ContainsKey("message"))
                    {
                        break;
                    }
                    else if (response != null && response.ContainsKey("message") && response["message"].ToString().Contains("Too many requests"))
                    {
                        Console.WriteLine("Too many requests. Retrying in 5 seconds...");
                        await Task.Delay(5000);
                        retryCount++;
                    }
                    else
                    {
                        break;
                    }
                }

                Console.WriteLine(response);

                if (response != null)
                {
                    Console.WriteLine("Balance fetched successfully.");
                    Console.WriteLine(JsonConvert.SerializeObject(response, Formatting.Indented));

                    if (response.TryGetValue("free", out var totalBalances) && totalBalances is Dictionary<string, object> totalDict)
                    {
                        if (totalDict.TryGetValue("USDT", out var usdtBalance))
                        {
                            Console.WriteLine($"Free USDT Balance: {usdtBalance}");
                            return Convert.ToDecimal(usdtBalance);
                        }
                        else
                        {
                            Console.WriteLine("USDT balance not found in the total balances.");
                        }
                    }
                    else
                    {
                        Console.WriteLine("Free balances not found in the balance response.");
                    }
                }
                else
                {
                    Console.WriteLine("Failed to fetch balance.");
                }
            }
            catch (Exception ex)
            {            
                Console.WriteLine($"An error occurred while fetching the balance: {ex.Message}");
            }

            return 0.0m;
        }

        public async Task<ExchangeOrderResult> SendEntryOrderAsync(Models.Order order, string apiKey, string apiSecret, string password)
        {
            try
            {
                if (_scopeFactory == null)
                {
                    throw new Exception("Service scope factory is not initialized.");
                }

                using (var scope = _scopeFactory.CreateScope())
                {
                    var context = scope.ServiceProvider.GetRequiredService<AutoSignalsDbContext>();

                    var bitgetClient = new ccxt.bitget(new Dictionary<string, object>
                   {
                       { "apiKey", apiKey },
                       { "secret", apiSecret },
                       { "password", password },
                   });

                    //simulate trade  
                    //if (order.IsTest)  
                    //{  
                    //    bitgetClient.setSandboxMode(true);  
                    //}  

                    // Set Futures mode  
                    bitgetClient.options["defaultType"] = "swap";

                    // **Ensure Hedged Mode is Enabled**  
                    var positionModeResult = await bitgetClient.setPositionMode(true, order.Symbol);
                    Console.WriteLine($"Position Mode Set: {positionModeResult}");

                    // **Check if the mode was successfully changed**  
                    if (positionModeResult == null || positionModeResult.ToString().Contains("error"))
                    {
                        throw new Exception("Failed to set position mode to hedge.");
                    }

                    // **Set Margin Mode**  
                    var marginMode = order.IsIsolated ? "isolated" : "cross";
                    var marginModeResult = await bitgetClient.setMarginMode(marginMode, order.Symbol);

                    // **Set Leverage**  
                    var holdSide = order.Side.ToLower() == "buy" ? "long" : "short";
                    var leverageResult = await bitgetClient.setLeverage(order.Leverage, order.Symbol, new Dictionary<string, object>
                   {
                       { "marginMode", marginMode },
                       { "marginCoin", "USDT" },
                       { "holdSide", holdSide }
                   });

                    var orderParams = new Dictionary<string, object>();
                    // **Create Order**  
                    if (order.Stoploss <= 0 || !order.Stoploss.HasValue)
                    {
                        orderParams = new Dictionary<string, object>
                       {
                           { "productType", "USDT-FUTURES" },
                           { "marginCoin", "USDT" },
                           { "tradeSide", "open" },
                           { "posSide", holdSide },
                           { "marginMode", marginMode }
                       };
                    }
                    else
                    {
                        orderParams = new Dictionary<string, object>
                       {
                           { "productType", "USDT-FUTURES" },
                           { "marginCoin", "USDT" },
                           { "tradeSide", "open" },
                           { "posSide", holdSide },
                           { "marginMode", marginMode },
                           { "presetStopLossPrice", order.Stoploss }
                       };
                    }

                    Dictionary<string, object> response = null;
                    int retryCount = 0;
                    Exception lastException = null;
                    while (retryCount < 3)
                    {
                        try
                        {
                            response = await bitgetClient.createOrder(order.Symbol, "market", order.Side, order.Size, order.Price, orderParams) as Dictionary<string, object>;
                            if (response != null && !response.ContainsKey("message"))
                            {
                                break;
                            }
                            else if (response != null && response.ContainsKey("message") && response["message"].ToString().Contains("Too many requests"))
                            {
                                Console.WriteLine("Too many requests. Retrying in 5 seconds...");
                                await Task.Delay(5000);
                                retryCount++;
                            }
                            else
                            {
                                break;
                            }
                        }
                        catch (Exception ex)
                        {
                            // Save the last exception for reporting
                            lastException = ex;

                            // Check for "Too many requests" in the exception message
                            if (ex.Message.Contains("Too many requests"))
                            {
                                Console.WriteLine("Too many requests (exception). Retrying in 5 seconds...");
                                await Task.Delay(5000);
                                retryCount++;
                            }
                            else
                            {
                                // For all other exceptions, break and handle below
                                break;
                            }
                        }
                    }

                    // Check for error code 45110  
                    if (response != null && response.TryGetValue("code", out var codeObj) && codeObj?.ToString() == "45110")
                    {
                        return new ExchangeOrderResult
                        {
                            Success = false,
                            ErrorCode = "45110",
                            ErrorMessage = response.ContainsKey("msg") ? response["msg"].ToString() : "Minimum order value not met",
                            Response = response
                        };
                    }

                    // Check for error code 40762 (order amount exceeds balance)
                    if (response != null && response.TryGetValue("code", out var codeObj2) && codeObj2?.ToString() == "40762")
                    {
                        return new ExchangeOrderResult
                        {
                            Success = false,
                            ErrorCode = "40762",
                            ErrorMessage = response.ContainsKey("msg") ? response["msg"].ToString() : "The order amount exceeds the balance",
                            Response = response
                        };
                    }

                    // Generic Bitget error handling for message/msg property
                    if (response != null && (response.ContainsKey("message") || response.ContainsKey("msg")))
                    {
                        return new ExchangeOrderResult
                        {
                            Success = false,
                            ErrorMessage = response.ContainsKey("msg") ? response["msg"].ToString() : response["message"]?.ToString(),
                            Response = response
                        };
                    }

                    // If an exception was thrown and no response was received, return the exception message
                    if (response == null && lastException != null)
                    {
                        return new ExchangeOrderResult
                        {
                            Success = false,
                            ErrorMessage = lastException.Message,
                            Response = null
                        };
                    }

                    return new ExchangeOrderResult
                    {
                        Success = response != null && !response.ContainsKey("message"),
                        Response = response
                    };
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred while sending entry order: {ex.Message}");
                await _errorLogService.LogErrorAsync(
                    $"An error occurred while sending entry order: {ex.Message}",
                    ex.StackTrace,
                    nameof(SendEntryOrderAsync),
                    JsonConvert.SerializeObject(order, Formatting.Indented)
                ); 
                return new ExchangeOrderResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        public async Task<string> SendTakeProfitOrderAsync(Models.Order order, string apiKey, string apiSecret, string password)
        {
            try
            {
                if (_scopeFactory == null)
                {
                    throw new Exception("Service scope factory is not initialized.");
                }

                using (var scope = _scopeFactory.CreateScope())
                {
                    var context = scope.ServiceProvider.GetRequiredService<AutoSignalsDbContext>();

                    var existingPosition = await context.Positions
                        .FirstOrDefaultAsync(p => p.Id == int.Parse(order.PositionId));
                    if (existingPosition == null)
                    {
                        Console.WriteLine("Position not found.");
                        return "Position not found.";
                    }

                    var bitgetClient = new ccxt.bitget(new Dictionary<string, object>
                    {
                        { "apiKey", apiKey },
                        { "secret", apiSecret },
                        { "password", password },
                    });

                    // Set Futures mode
                    bitgetClient.options["defaultType"] = "swap";

                    // Determine position side (long or short)
                    var holdSide = order.Side.ToLower() == "sell" ? "long" : "short";
                    var posSide = order.Side.ToLower() == "sell" ? "long" : "short";

                    // Needed for hedgemode, closing is set in the parms, this is used to trach the position, buy to close a long, sell to close a short
                    var side = order.Side.ToLower() == "sell" ? "buy" : "sell";

                    // Ensure Hedged Mode is Enabled
                    var positionModeResult = await bitgetClient.setPositionMode(true, order.Symbol);

                    var size = Convert.ToDouble(existingPosition.Size) * (order.Size / 100);

                    var orderParams = new Dictionary<string, object>
                    {
                        { "productType", "USDT-FUTURES" },
                        { "reduceOnly", "true" },
                        { "tradeSide", "close" },
                        { "posSide", posSide },
                        { "tpMode", order.IsIsolated ? "isolated" : "cross" }
                    };

                    // Create the Take Profit order with "sell" to close the long
                    Dictionary<string, object> response = null;
                    int retryCount = 0;
                    while (retryCount < 3)
                    {
                        response = await bitgetClient.createOrder(order.Symbol, "market", side, size, order.Price, orderParams) as Dictionary<string, object>;
                        if (response != null && !response.ContainsKey("message"))
                        {
                            //await _telegramBotService.LoggError($"Take Profit order sent successfully. Order: {JsonConvert.SerializeObject(order, Formatting.Indented)}");
                            //await _telegramBotService.LoggError($"Take Profit order response: {JsonConvert.SerializeObject(response, Formatting.Indented)}");
                            break;
                        }
                        else if (response != null && response.ContainsKey("message") && response["message"].ToString().Contains("Too many requests"))
                        {
                            Console.WriteLine("Too many requests. Retrying in 5 seconds...");
                            await Task.Delay(5000);
                            retryCount++;
                        }
                        else
                        {
                            break;
                        }
                    }
                    return response.ToString();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred while sending take profit order: {ex.Message}");
                await _errorLogService.LogErrorAsync(
                    $"An error occurred while sending take profit order: {ex.Message}",
                    ex.StackTrace,
                    nameof(SendEntryOrderAsync),
                    JsonConvert.SerializeObject(order, Formatting.Indented)
                ); 
                return $"Error: {ex.Message}";
            }
        }

        public async Task<string> SendStoplossOrderAsync(Models.Order order, string apiKey, string apiSecret, string password)
        {
            try
            {
                if (_scopeFactory == null)
                {
                    throw new Exception("Service scope factory is not initialized.");
                }

                using (var scope = _scopeFactory.CreateScope())
                {
                    var context = scope.ServiceProvider.GetRequiredService<AutoSignalsDbContext>();

                    var bitgetClient = new ccxt.bitget(new Dictionary<string, object>
                    {
                        { "apiKey", apiKey },
                        { "secret", apiSecret },
                        { "password", password },
                    });

                    // Set Futures mode
                    bitgetClient.options["defaultType"] = "swap";

                    // Determine position side (long or short)
                    var holdSide = order.Side.ToLower() == "sell" ? "long" : "short";
                    var posSide = order.Side.ToLower() == "sell" ? "short" : "long";

                    // Needed for hedgemode, closing is set in the parms, this is used to trach the position, buy to close a long, sell to close a short
                    var side = order.Side.ToLower() == "sell" ? "buy" : "sell";

                    // Ensure Hedged Mode is Enabled
                    var positionModeResult = await bitgetClient.setPositionMode(true, order.Symbol);
                    
                    var orderParams = new Dictionary<string, object>
                    {
                        { "productType", "USDT-FUTURES" },
                        { "reduceOnly", "true" },
                        { "tradeSide", "close" },
                        { "posSide", posSide },
                        { "tpMode", order.IsIsolated ? "isolated" : "cross" },
                        { "holdSide", holdSide }
                    };

                    Dictionary<string, object> response = null;
                    int retryCount = 0;
                    while (retryCount < 3)
                    {
                        try
                        {
                            response = await bitgetClient.closePosition(order.Symbol, side, orderParams) as Dictionary<string, object>;
                            if (response != null && !response.ContainsKey("message"))
                            {
                                break;
                            }
                            else if (response != null && response.ContainsKey("message") && response["message"].ToString().Contains("Too many requests"))
                            {
                                Console.WriteLine("Too many requests. Retrying in 5 seconds...");
                                await Task.Delay(5000);
                                retryCount++;
                            }
                            else
                            {
                                break;
                            }
                        }
                        catch (ccxt.ExchangeError ex)
                        {
                            // Check for Bitget error code 22002 in the exception message
                            if (ex.Message.Contains("\"code\":\"22002\""))
                            {
                                var msg = ex.Message.Contains("\"msg\":\"")
                                    ? ex.Message.Split("\"msg\":\"")[1].Split("\"")[0]
                                    : "No position to close";
                                return $"Error: {msg} (code 22002)";
                            }
                            else
                            {
                                // Log and rethrow for other errors
                                Console.WriteLine($"ExchangeError: {ex.Message}");
                                throw;
                            }
                        }
                    }

                    // Handle Bitget error code 22002: No position to close
                    if (response != null && response.TryGetValue("code", out var codeObj) && codeObj?.ToString() == "22002")
                    {
                        var msg = response.ContainsKey("msg") ? response["msg"].ToString() : "No position to close";
                        return $"Error: {msg} (code 22002)";
                    }

                    return response != null ? JsonConvert.SerializeObject(response) : "No response from Bitget";
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred while sending Stoploss Order: {ex.Message}");
                await _errorLogService.LogErrorAsync(
                    $"An error occurred while sending Stoploss Order: {ex.Message}",
                    ex.StackTrace,
                    nameof(SendEntryOrderAsync),
                    JsonConvert.SerializeObject(order, Formatting.Indented)
                ); 
                return $"Error: {ex.Message}";
            }
        }

    }
}
