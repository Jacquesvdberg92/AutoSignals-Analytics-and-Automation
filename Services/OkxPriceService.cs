namespace AutoSignals.Services
{
    using AutoSignals.Data;
    using AutoSignals.Models;
    using AutoSignals.ViewModels;
    using ccxt;
    using CryptoExchange.Net;
    using CryptoExchange.Net.Authentication;
    using CryptoExchange.Net.Converters;
    using CryptoExchange.Net.Converters.SystemTextJson;
    using CryptoExchange.Net.Interfaces;
    using CryptoExchange.Net.Objects;
    using Microsoft.EntityFrameworkCore;
    using Newtonsoft.Json;
    using OKX.Net.Clients;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;

    public class OkxPriceService : IOkxService
    {
        private readonly ccxt.okx _okx;
        private readonly AutoSignalsDbContext _context;
        private static readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        private readonly ErrorLogService _errorLogService;
        private readonly IServiceScopeFactory _scopeFactory;

        public OkxPriceService(string apiKey, string apiSecret, string password, ErrorLogService errorLogService, IServiceScopeFactory scopeFactory)
        {
            _okx = new ccxt.okx(new Dictionary<string, object>
            {
                { "apiKey", apiKey },
                { "secret", apiSecret },
                { "password", password }
            });
            _okx.options["defaultType"] = "swap";
            _okx.options["defaultSettle"] = "USDT";
            _errorLogService = errorLogService;
            _scopeFactory = scopeFactory;
        }

        public async Task<IEnumerable<object>> GetOkxMarketsAsync()
        {
            await _semaphore.WaitAsync();
            try
            {
                using (var scope = _scopeFactory.CreateScope())
                {
                    var context = scope.ServiceProvider.GetRequiredService<AutoSignalsDbContext>();

                    var markets = await _okx.fetchMarkets(new Dictionary<string, object> { { "type", "swap" } }) as List<object>;

                    if (markets == null)
                    {
                        Console.WriteLine("Failed to fetch futures markets.");
                        return Enumerable.Empty<object>();
                    }

                    var usdtSwapMarkets = new List<OkxMarket>();
                    var fetchedSymbols = new HashSet<string>();

                    foreach (var market in markets)
                    {
                        if (market is Dictionary<string, object> marketDict &&
                           marketDict.TryGetValue("quote", out var quote) && (quote?.ToString() == "USDT") && // Handle null quote value
                           marketDict.TryGetValue("type", out var type) && type.ToString() == "swap")
                        {
                            var limits = marketDict["limits"] as Dictionary<string, object>;
                            var cost = limits["cost"] as Dictionary<string, object>;
                            var leverage = limits["leverage"] as Dictionary<string, object>;
                            var precision = marketDict["precision"] as Dictionary<string, object>;

                            var symbol = marketDict["symbol"].ToString().Replace("/", "").Replace(":USDT", "");

                            var existingMarket = await context.OkxMarkets.FirstOrDefaultAsync(m => m.Symbol == symbol);

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
                                var okxMarket = new OkxMarket
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
                                usdtSwapMarkets.Add(okxMarket);
                            }

                            // Add symbol to fetched symbols set
                            fetchedSymbols.Add(symbol);
                        }
                    }

                    // Save new and updated markets to database
                    if (usdtSwapMarkets.Count > 0)
                    {
                        context.OkxMarkets.AddRange(usdtSwapMarkets);
                    }
                    await context.SaveChangesAsync();

                    // Find symbols to delete
                    var currentSymbols = await context.OkxMarkets.Select(m => m.Symbol).ToListAsync();
                    var symbolsToDelete = currentSymbols.Except(fetchedSymbols).ToList();

                    if (symbolsToDelete.Count > 0)
                    {
                        // Delete from OkxMarkets
                        var marketsToDelete = context.OkxMarkets.Where(m => symbolsToDelete.Contains(m.Symbol));
                        context.OkxMarkets.RemoveRange(marketsToDelete);

                        // Insert into OkxRemovedAssets
                        foreach (var symbol in symbolsToDelete)
                        {
                            var existingRemovedAsset = await context.OkxRemovedAssets.FirstOrDefaultAsync(ra => ra.Symbol == symbol);
                            if (existingRemovedAsset == null)
                            {
                                var removedAsset = new OkxRemovedAsset
                                {
                                    Symbol = symbol,
                                    Time = DateTime.UtcNow
                                };
                                context.OkxRemovedAssets.Add(removedAsset);
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


        public async Task FetchAllOkxAssetPricesAsync()
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AutoSignalsDbContext>();

            var markets = await context.OkxMarkets.AsNoTracking().ToListAsync();
            if (markets.Count == 0) return;

            var assetPricesToAdd = new List<OkxAssetPrice>();
            var assetPricesToUpdate = new List<OkxAssetPrice>();
            var fetchedSymbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Cache existing rows (case-insensitive)
            var existingAssetPrices = await context.OkxAssetPrices
                .AsNoTracking()
                .ToDictionaryAsync(ap => ap.Symbol, StringComparer.OrdinalIgnoreCase);

            // Build a fast lookup of DB symbols we care about
            var wantedSymbols = new HashSet<string>(
                markets.Select(m => m.Symbol).Where(s => !string.IsNullOrWhiteSpace(s)),
                StringComparer.OrdinalIgnoreCase
            );

            static decimal ToDec(object? val)
            {
                if (val == null) return 0m;
                if (val is decimal d) return d;
                if (val is double db) return Convert.ToDecimal(db, System.Globalization.CultureInfo.InvariantCulture);
                if (val is float f) return Convert.ToDecimal(f, System.Globalization.CultureInfo.InvariantCulture);
                if (val is long l) return l;
                if (val is int i) return i;
                if (val is string s && decimal.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var r)) return r;
                return 0m;
            }

            Dictionary<string, object>? raw = null;
            try
            {
                // OKX: GET /api/v5/market/tickers?instType=SWAP
                raw = await _okx.publicGetMarketTickers(new Dictionary<string, object> { { "instType", "SWAP" } }) as Dictionary<string, object>;
            }
            catch (Exception ex)
            {
                await _errorLogService.LogErrorAsync($"publicGetMarketTickers failed: {ex.Message}", ex.StackTrace, "OkxPriceService.FetchAllOkxAssetPricesAsync");
                return;
            }

            if (raw == null || !raw.TryGetValue("data", out var dataObj))
            {
                await _errorLogService.LogErrorAsync("publicGetMarketTickers returned null or no data", null, "OkxPriceService.FetchAllOkxAssetPricesAsync");
                return;
            }

            var rows = (dataObj as IEnumerable<object>)?.OfType<Dictionary<string, object>>()?.ToList() ?? new List<Dictionary<string, object>>();
            foreach (var it in rows)
            {
                // instId e.g. "BTC-USDT-SWAP"
                if (!it.TryGetValue("instId", out var idObj)) continue;
                var instId = idObj?.ToString();
                if (string.IsNullOrWhiteSpace(instId)) continue;

                var parts = instId.Split('-');
                if (parts.Length < 3) continue;

                var baseCoin = parts[0].ToUpperInvariant();
                var quote = parts[1].ToUpperInvariant();
                var type = parts[2].ToUpperInvariant();

                if (type != "SWAP" || quote != "USDT") continue;

                // Your DB symbol is "BTCUSDT"
                var dbSymbol = baseCoin + quote;
                if (!wantedSymbols.Contains(dbSymbol)) continue;

                // OKX fields (strings): last, open24h, high24h, low24h, vol24h
                var last = ToDec(it.TryGetValue("last", out var lastObj) ? lastObj : null);
                if (last == 0m) continue;

                var open = ToDec(it.TryGetValue("open24h", out var openObj) ? openObj : null);
                var high = ToDec(it.TryGetValue("high24h", out var highObj) ? highObj : null);
                var low = ToDec(it.TryGetValue("low24h", out var lowObj) ? lowObj : null);
                var close = last;
                var volume = ToDec(it.TryGetValue("vol24h", out var volObj) ? volObj : null); // contracts; use if acceptable

                if (existingAssetPrices.TryGetValue(dbSymbol, out var existing))
                {
                    // Update the existing (detached) entity and mark as Modified later
                    existing.Price = last;
                    existing.Open = open;
                    existing.High = high;
                    existing.Low = low;
                    existing.Close = close;
                    existing.Volume = volume;
                    existing.Time = DateTime.UtcNow;

                    assetPricesToUpdate.Add(existing);
                }
                else
                {
                    assetPricesToAdd.Add(new OkxAssetPrice
                    {
                        Symbol = dbSymbol,
                        Price = last,
                        Open = open,
                        High = high,
                        Low = low,
                        Close = close,
                        Volume = volume,
                        Time = DateTime.UtcNow
                    });
                }

                fetchedSymbols.Add(dbSymbol);
            }

            if (assetPricesToAdd.Count > 0)
                context.OkxAssetPrices.AddRange(assetPricesToAdd);

            if (assetPricesToUpdate.Count > 0)
                context.OkxAssetPrices.UpdateRange(assetPricesToUpdate);

            // Only log missing; do not delete here
            var missing = markets.Select(m => m.Symbol).Where(s => !string.IsNullOrEmpty(s) && !fetchedSymbols.Contains(s)).ToList();
            if (missing.Count > 0)
            {
                await _errorLogService.LogErrorAsync($"OKX tickers missing for {missing.Count} symbols in this run.", null, "OkxPriceService.FetchAllOkxAssetPricesAsync", string.Join(",", missing.Take(50)));
            }

            // Save with retry and dedupe fallback
            for (int retry = 0; retry < 3; retry++)
            {
                try
                {
                    await context.SaveChangesAsync();
                    break;
                }
                catch (DbUpdateException ex) when (retry < 2 && ex.InnerException?.Message?.IndexOf("duplicate key", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    // Fallback: if any pending Adds conflict (race), convert them to Updates
                    if (assetPricesToAdd.Count > 0)
                    {
                        var symbols = assetPricesToAdd.Select(a => a.Symbol).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                        var existingNow = await context.OkxAssetPrices.AsNoTracking()
                            .Where(x => symbols.Contains(x.Symbol))
                            .ToDictionaryAsync(x => x.Symbol, StringComparer.OrdinalIgnoreCase);

                        var toUpdate = new List<OkxAssetPrice>();
                        foreach (var add in assetPricesToAdd.ToList())
                        {
                            if (existingNow.TryGetValue(add.Symbol, out var exi))
                            {
                                exi.Price = add.Price;
                                exi.Open = add.Open;
                                exi.High = add.High;
                                exi.Low = add.Low;
                                exi.Close = add.Close;
                                exi.Volume = add.Volume;
                                exi.Time = add.Time;

                                toUpdate.Add(exi);
                                context.Entry(add).State = EntityState.Detached;
                                assetPricesToAdd.Remove(add);
                            }
                        }

                        if (toUpdate.Count > 0)
                            context.OkxAssetPrices.UpdateRange(toUpdate);
                    }
                    // retry
                }
            }
        }

        public async Task<decimal?> FetchOkxAssetPriceAsync(string symbol)
        {
            var ticker = await _okx.fetchTicker(symbol) as Dictionary<string, object>;
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

                var symbols = await context.OkxMarkets.Select(m => m.BaseCoin + "-USDT").ToListAsync();
                if (symbols == null || !symbols.Any())
                {
                    Console.WriteLine("No markets found.");
                    return;
                }

                var existingPrices = await context.OkxAssetPrices.ToDictionaryAsync(ap => ap.Symbol);
                var client = new OKXSocketClient();
                var assetPricesToAdd = new List<OkxAssetPrice>();
                var assetPricesToUpdate = new List<OkxAssetPrice>();
                var updatedSymbols = new HashSet<string>();

                foreach (var symbol in symbols)
                {
                    var result = await client.UnifiedApi.ExchangeData.SubscribeToTickerUpdatesAsync(symbol, async data =>
                    {
                        if (data != null && data.Data != null)
                        {
                            var lastPrice = data.Data.LastPrice;
                            var open = data.Data.OpenPrice ?? 0;
                            var high = data.Data.HighPrice ?? 0;
                            var low = data.Data.LowPrice ?? 0;
                            var close = data.Data.LastPrice ?? 0; 
                            var volume = data.Data.QuoteVolume; 

                            if (lastPrice != null)
                            {
                                await _semaphore.WaitAsync();
                                try
                                {
                                    var dbSymbol = symbol.Replace("-", "");
                                    if (existingPrices.TryGetValue(dbSymbol, out var existingAssetPrice))
                                    {
                                        // Update price and OHLCV if changed
                                        if (existingAssetPrice.Price != (decimal)lastPrice ||
                                            existingAssetPrice.Open != open ||
                                            existingAssetPrice.High != high ||
                                            existingAssetPrice.Low != low ||
                                            existingAssetPrice.Close != close ||
                                            existingAssetPrice.Volume != volume)
                                        {
                                            existingAssetPrice.Price = (decimal)lastPrice;
                                            existingAssetPrice.Open = open;
                                            existingAssetPrice.High = high;
                                            existingAssetPrice.Low = low;
                                            existingAssetPrice.Close = close;
                                            existingAssetPrice.Volume = volume;
                                            existingAssetPrice.Time = DateTime.Now;
                                            assetPricesToUpdate.Add(existingAssetPrice);
                                        }
                                    }
                                    else
                                    {
                                        if (!assetPricesToAdd.Any(ap => ap.Symbol == dbSymbol))
                                        {
                                            assetPricesToAdd.Add(new OkxAssetPrice
                                            {
                                                Symbol = dbSymbol,
                                                Price = (decimal)lastPrice,
                                                Open = open,
                                                High = high,
                                                Low = low,
                                                Close = close,
                                                Volume = volume,
                                                Time = DateTime.Now
                                            });
                                        }
                                    }

                                    updatedSymbols.Add(dbSymbol);
                                }
                                finally
                                {
                                    _semaphore.Release();
                                }
                            }
                        }
                    });

                    if (!result.Success)
                    {
                        Console.WriteLine($"Failed to subscribe to ticker updates for symbol: {symbol}");
                    }
                }

                await client.UnifiedApi.UnsubscribeAllAsync();

                // Perform database operations after iteration
                await _semaphore.WaitAsync();
                try
                {
                    if (assetPricesToAdd.Count > 0)
                    {
                        context.OkxAssetPrices.AddRange(assetPricesToAdd);
                    }

                    if (assetPricesToUpdate.Count > 0)
                    {
                        context.OkxAssetPrices.UpdateRange(assetPricesToUpdate);
                    }

                    // Find symbols to delete
                    var symbolsToDelete = existingPrices.Keys.Except(updatedSymbols).ToList();

                    if (symbolsToDelete.Count > 0)
                    {
                        var marketsToDelete = context.OkxMarkets.Where(m => symbolsToDelete.Contains(m.Symbol));
                        context.OkxMarkets.RemoveRange(marketsToDelete);

                        var assetPricesToDelete = context.OkxAssetPrices.Where(ap => symbolsToDelete.Contains(ap.Symbol));
                        context.OkxAssetPrices.RemoveRange(assetPricesToDelete);

                        foreach (var symbol in symbolsToDelete)
                        {
                            var existingRemovedAsset = await context.OkxRemovedAssets.FirstOrDefaultAsync(ra => ra.Symbol == symbol);
                            if (existingRemovedAsset == null)
                            {
                                var removedAsset = new OkxRemovedAsset
                                {
                                    Symbol = symbol,
                                    Time = DateTime.UtcNow
                                };
                                context.OkxRemovedAssets.Add(removedAsset);
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
                catch (Exception ex)
                {
                    Console.WriteLine($"Error: {ex.Message}");
                    if (ex.InnerException != null)
                    {
                        Console.WriteLine($"Inner Exception: {ex.InnerException.Message}");
                        Console.WriteLine($"Inner Exception Stack Trace: {ex.InnerException.StackTrace}");
                    }
                    Console.WriteLine($"Stack Trace: {ex.StackTrace}");
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

                var duplicateSymbols = context.OkxAssetPrices
                    .GroupBy(s => s.Symbol)
                    .Where(g => g.Count() > 1)
                    .SelectMany(g => g.OrderBy(s => s.Time).Skip(1));

                context.OkxAssetPrices.RemoveRange(duplicateSymbols);
                await context.SaveChangesAsync();
            }
        }


        public async Task<decimal> GetBalance(string apiKey, string apiSecret, string password)
        {
            var okxClient = new ccxt.okx(new Dictionary<string, object>
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
                    response = await okxClient.fetchBalance(new Dictionary<string, object>
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
                    throw new Exception("Service scope factory is not initialized.");

                using (var scope = _scopeFactory.CreateScope())
                {
                    var context = scope.ServiceProvider.GetRequiredService<AutoSignalsDbContext>();

                    var okxClient = new ccxt.okx(new Dictionary<string, object>
            {
                { "apiKey", apiKey },
                { "secret", apiSecret },
                { "password", password },
            });

                    // Set Futures mode
                    okxClient.options["defaultType"] = "swap";
                    okxClient.options["defaultSettle"] = "USDT";

                    // Ensure markets are loaded (symbol metadata)
                    try { await okxClient.loadMarkets(); } catch { /* non-fatal */ }

                    // Try to ensure hedge (long_short) mode so posSide is honored for SL/TP
                    try
                    {
                        var pmr = await okxClient.setPositionMode(true /* hedged */) as Dictionary<string, object>;
                        var ok = pmr != null && (!pmr.ContainsKey("code") || pmr["code"]?.ToString() == "0");
                        if (!ok)
                        {
                            await _errorLogService.LogErrorAsync(
                                $"setPositionMode(hedged) did not return success. Response: {JsonConvert.SerializeObject(pmr)}",
                                null,
                                nameof(SendEntryOrderAsync));
                        }
                    }
                    catch (Exception ex)
                    {
                        await _errorLogService.LogErrorAsync($"setPositionMode failed: {ex.Message}", ex.StackTrace, nameof(SendEntryOrderAsync));
                        // continue; not fatal
                    }

                    // Fetch market limits to clamp leverage to exchange constraints
                    var marketInfo = await context.OkxMarkets.AsNoTracking()
                                .FirstOrDefaultAsync(m => m.Symbol == order.Symbol.Trim());

                    // Determine margin mode and side
                    var tdMode = order.IsIsolated ? "isolated" : "cross"; // OKX expects 'tdMode'
                    var posSide = order.Side.Equals("buy", StringComparison.OrdinalIgnoreCase) ? "long" : "short";

                    // Compute safe leverage (prefer exchange min/max if available)
                    int requestedLev = Convert.ToInt32(order.Leverage);
                    int minLev = marketInfo?.MinLever > 0 ? marketInfo.MinLever : 1;
                    int maxLev = marketInfo?.MaxLever > 0 ? marketInfo.MaxLever : 125;
                    int safeLev = Math.Clamp(requestedLev, minLev, maxLev);

                    // Set margin mode (account-level + symbol) if needed
                    var marginModeParams = tdMode == "isolated"
                        ? new Dictionary<string, object> { { "lever", safeLev } }
                        : new Dictionary<string, object>();
                    try { await okxClient.setMarginMode(tdMode, order.Symbol, marginModeParams); } catch { /* tolerate */ }

                    // Set leverage per side
                    try
                    {
                        var leverageResult = await okxClient.setLeverage(safeLev, order.Symbol, new Dictionary<string, object>
                {
                    { "marginMode", tdMode },
                    { "posSide", posSide },
                    { "ccy", "USDT" }
                });
                        Console.WriteLine($"SetLeverage: {JsonConvert.SerializeObject(leverageResult)}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"SetLeverage failed: {ex.Message}");
                    }

                    // Amount must be in contracts (sz). Convert base size to contracts using ctVal and lotSz.
                    double ctVal = 1.0;
                    double lotSz = 1.0;
                    try
                    {
                        var markets = await okxClient.fetchMarkets(new Dictionary<string, object> { { "type", "swap" } }) as List<object>;
                        var market = markets?
                            .OfType<Dictionary<string, object>>()
                            .FirstOrDefault(m => m.TryGetValue("symbol", out var sym) && string.Equals(sym?.ToString(), order.Symbol, StringComparison.OrdinalIgnoreCase));

                        if (market != null)
                        {
                            if (market.TryGetValue("info", out var infoObj) && infoObj is Dictionary<string, object> info)
                            {
                                if (info.TryGetValue("ctVal", out var ctValObj) && ctValObj != null)
                                    ctVal = Convert.ToDouble(ctValObj, System.Globalization.CultureInfo.InvariantCulture);
                                if (info.TryGetValue("lotSz", out var lotSzObj) && lotSzObj != null)
                                    lotSz = Convert.ToDouble(lotSzObj, System.Globalization.CultureInfo.InvariantCulture);
                            }
                            if (market.TryGetValue("contractSize", out var csObj) && csObj != null)
                                ctVal = Convert.ToDouble(csObj, System.Globalization.CultureInfo.InvariantCulture);
                        }
                    }
                    catch { /* tolerate */ }

                    if (ctVal <= 0) ctVal = 1.0;
                    if (lotSz <= 0) lotSz = 1.0;

                    var baseQty = Convert.ToDouble(order.Size);
                    var contracts = baseQty / ctVal;
                    // Round down to lot size steps
                    var contractsRounded = Math.Floor(contracts / lotSz) * lotSz;
                    if (contractsRounded <= 0)
                        throw new Exception($"Computed contracts is zero. baseQty={baseQty}, ctVal={ctVal}, lotSz={lotSz}");

                    // Build OKX params (OKX: use tdMode, posSide). Do NOT send Bitget params.
                    var orderParams = new Dictionary<string, object>
            {
                { "tdMode", tdMode },
                { "posSide", posSide }
                // { "reduceOnly", false } // optional for opens
            };

                    // Pre-validate and configure Stop Loss so OKX interprets it correctly for shorts/longs
                    //if (order.Stoploss.HasValue && order.Stoploss > 0)
                    //{
                    //    decimal? last = null;
                    //    try
                    //    {
                    //        var ticker = await okxClient.fetchTicker(order.Symbol) as Dictionary<string, object>;
                    //        if (ticker != null && ticker.TryGetValue("last", out var lastObj) && lastObj != null)
                    //            last = Convert.ToDecimal(lastObj, System.Globalization.CultureInfo.InvariantCulture);
                    //    }
                    //    catch { /* tolerate */ }

                    //    if (last.HasValue)
                    //    {
                    //        if (order.Side.Equals("buy", StringComparison.OrdinalIgnoreCase) && (decimal)order.Stoploss.Value >= last.Value)
                    //        {
                    //            return new ExchangeOrderResult
                    //            {
                    //                Success = false,
                    //                ErrorCode = "SL_VALIDATION",
                    //                ErrorMessage = $"For long entries, Stop Loss must be below the last price ({last.Value}). Provided: {order.Stoploss.Value}"
                    //            };
                    //        }
                    //        if (order.Side.Equals("sell", StringComparison.OrdinalIgnoreCase) && (decimal)order.Stoploss.Value <= last.Value)
                    //        {
                    //            return new ExchangeOrderResult
                    //            {
                    //                Success = false,
                    //                ErrorCode = "SL_VALIDATION",
                    //                ErrorMessage = $"For short entries, Stop Loss must be above the last price ({last.Value}). Provided: {order.Stoploss.Value}"
                    //            };
                    //        }
                    //    }

                    //    if(order.Side.Equals("buy", StringComparison.OrdinalIgnoreCase))
                    //    {
                    //        // Long position SL
                    //        // Use OKX-native fields to ensure correct behavior
                    //        orderParams["slTriggerPx"] = order.Stoploss.Value;
                    //        orderParams["slTriggerPxType"] = "mark"; 
                    //        orderParams["slOrdPx"] = "-1";          // market order on trigger
                    //    }
                    //    else
                    //    {
                    //        // Short position SL
                    //        // Use OKX-native fields to ensure correct behavior
                    //        orderParams["tpTriggerPx"] = order.Stoploss.Value;
                    //        orderParams["tpTriggerPxType"] = "mark"; 
                    //        orderParams["tpOrdPx"] = "-1";          // market order on trigger
                    //    }
                    //}

                    Dictionary<string, object> response = null;
                    int retryCount = 0;
                    Exception lastException = null;

                    while (retryCount < 3)
                    {
                        try
                        {
                            // Market order => price must be null
                            response = await okxClient.createOrder(
                                order.Symbol,
                                "market",
                                order.Side.ToLowerInvariant(),
                                contractsRounded,
                                null,
                                orderParams
                            ) as Dictionary<string, object>;

                            // OKX success => code == "0" or no "message"
                            var ok = response != null &&
                                        (!response.ContainsKey("code") || response["code"]?.ToString() == "0") &&
                                        !response.ContainsKey("message");

                            if (ok) break;

                            var msg = response?.ContainsKey("msg") == true ? response["msg"]?.ToString() : response?.ContainsKey("message") == true ? response["message"]?.ToString() : null;
                            if (!string.IsNullOrEmpty(msg) && msg.Contains("Too many requests", StringComparison.OrdinalIgnoreCase))
                            {
                                await Task.Delay(5000);
                                retryCount++;
                                continue;
                            }
                            break;
                        }
                        catch (Exception ex)
                        {
                            lastException = ex;
                            await _errorLogService.LogErrorAsync(
                                $"An error occurred while sending entry order: {ex.Message}",
                                ex.StackTrace,
                                nameof(SendEntryOrderAsync),
                                JsonConvert.SerializeObject(order, Formatting.Indented) + Environment.NewLine + JsonConvert.SerializeObject(orderParams, Formatting.Indented)
                            );

                            if (ex.Message.Contains("Too many requests", StringComparison.OrdinalIgnoreCase))
                            {
                                await Task.Delay(5000);
                                retryCount++;
                                continue;
                            }
                            break;
                        }
                    }

                    // Specific OKX codes you care about
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
                    // Add explicit mapping for SL direction error
                    if (response != null && response.TryGetValue("data", out var dataObj) && dataObj is IEnumerable<object> dataArr)
                    {
                        var first = dataArr.OfType<Dictionary<string, object>>().FirstOrDefault();
                        if (first != null && first.TryGetValue("sCode", out var sCode) && sCode?.ToString() == "51280")
                        {
                            var lastMsg = first.TryGetValue("sMsg", out var sMsgObj) ? sMsgObj?.ToString() : "SL trigger price invalid relative to last price";
                            return new ExchangeOrderResult
                            {
                                Success = false,
                                ErrorCode = "51280",
                                ErrorMessage = lastMsg,
                                Response = response
                            };
                        }
                    }
                    if (response != null && (response.ContainsKey("message") || response.ContainsKey("msg")))
                    {
                        return new ExchangeOrderResult
                        {
                            Success = false,
                            ErrorMessage = response.ContainsKey("msg") ? response["msg"]?.ToString() : response["message"]?.ToString(),
                            Response = response
                        };
                    }
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
                        Success = response != null && (!response.ContainsKey("code") || response["code"]?.ToString() == "0"),
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
                    throw new Exception("Service scope factory is not initialized.");

                using (var scope = _scopeFactory.CreateScope())
                {
                    var context = scope.ServiceProvider.GetRequiredService<AutoSignalsDbContext>();

                    var existingPosition = await context.Positions
                        .FirstOrDefaultAsync(p => p.Id == int.Parse(order.PositionId));
                    if (existingPosition == null)
                        return "Position not found.";

                    var okxClient = new ccxt.okx(new Dictionary<string, object>
                    {
                        { "apiKey", apiKey },
                        { "secret", apiSecret },
                        { "password", password },
                    });

                    okxClient.options["defaultType"] = "swap";


                    // Determine which side to send to close
                    var isClosingLong = order.Side.Equals("sell", StringComparison.OrdinalIgnoreCase); // sell closes long, buy closes short
                    var closeSide = isClosingLong ? "sell" : "buy";
                    var posSide = isClosingLong ? "long" : "short"; // hedge mode

                    // Compute intended base-coin quantity to close (percentage of position)
                    var positionBaseQty = Convert.ToDouble(existingPosition.Size);
                    var baseQtyToClose = positionBaseQty * (order.Size / 100.0);
                    if (baseQtyToClose <= 0)
                        return "Computed close size is zero.";

                    // Fetch market info to get contract size (ctVal) and lot size (lotSz)
                    double ctVal = 1.0;  // contract size in base coin (e.g., 0.01 BTC)
                    double lotSz = 1.0;  // minimum contracts step
                    try
                    {
                        var markets = await okxClient.fetchMarkets(new Dictionary<string, object> { { "type", "swap" } }) as List<object>;
                        var market = markets?
                            .OfType<Dictionary<string, object>>()
                            .FirstOrDefault(m => m.TryGetValue("symbol", out var sym) && string.Equals(sym?.ToString(), order.Symbol, StringComparison.OrdinalIgnoreCase));

                        if (market != null)
                        {
                            // Prefer info.ctVal and info.lotSz
                            if (market.TryGetValue("info", out var infoObj) && infoObj is Dictionary<string, object> info)
                            {
                                if (info.TryGetValue("ctVal", out var ctValObj) && ctValObj != null)
                                    ctVal = Convert.ToDouble(ctValObj, System.Globalization.CultureInfo.InvariantCulture);
                                if (info.TryGetValue("lotSz", out var lotSzObj) && lotSzObj != null)
                                    lotSz = Convert.ToDouble(lotSzObj, System.Globalization.CultureInfo.InvariantCulture);
                            }
                            // Fallback to top-level contractSize if present
                            if (market.TryGetValue("contractSize", out var csObj) && csObj != null)
                                ctVal = Convert.ToDouble(csObj, System.Globalization.CultureInfo.InvariantCulture);
                        }
                    }
                    catch
                    {
                        // Keep defaults if market fetch fails
                    }

                    if (ctVal <= 0) ctVal = 1.0;
                    if (lotSz <= 0) lotSz = 1.0;

                    // Convert base-coin amount to contracts and round to lotSz
                    var contractsToClose = baseQtyToClose / ctVal;
                    var contractsRounded = Math.Floor(contractsToClose / lotSz) * lotSz;

                    if (contractsRounded <= 0)
                        return $"Computed contracts to close is zero after rounding. baseQty={baseQtyToClose}, ctVal={ctVal}, lotSz={lotSz}";

                    // Preferred params (hedge mode)
                    var orderParams = new Dictionary<string, object>
                    {
                        { "tdMode", order.IsIsolated ? "isolated" : "cross" },
                        { "reduceOnly", true },
                        { "posSide", posSide }
                    };

                    Dictionary<string, object> response = null;
                    int retryCount = 0;
                    Exception lastEx = null;

                    while (retryCount < 3)
                    {
                        try
                        {
                            // Market order: price must be null
                            response = await okxClient.createOrder(order.Symbol, "market", closeSide, contractsRounded, null, orderParams) as Dictionary<string, object>;
                            if (response != null && (!response.ContainsKey("code") || response["code"]?.ToString() == "0"))
                                break;

                            var msg = response?.ContainsKey("msg") == true ? response["msg"]?.ToString() : response?.ContainsKey("message") == true ? response["message"]?.ToString() : null;
                            if (!string.IsNullOrEmpty(msg) && msg.Contains("posSide", StringComparison.OrdinalIgnoreCase))
                            {
                                var orderParamsNet = new Dictionary<string, object>
                                {
                                    { "tdMode", order.IsIsolated ? "isolated" : "cross" },
                                    { "reduceOnly", true }
                                };
                                response = await okxClient.createOrder(order.Symbol, "market", closeSide, contractsRounded, null, orderParamsNet) as Dictionary<string, object>;
                                if (response != null && (!response.ContainsKey("code") || response["code"]?.ToString() == "0"))
                                    break;
                            }

                            if (!string.IsNullOrEmpty(msg) && msg.Contains("Too many requests", StringComparison.OrdinalIgnoreCase))
                            {
                                await Task.Delay(5000);
                                retryCount++;
                                continue;
                            }

                            break;
                        }
                        catch (Exception ex)
                        {
                            lastEx = ex;
                            if (ex.Message.Contains("Too many requests", StringComparison.OrdinalIgnoreCase))
                            {
                                await Task.Delay(5000);
                                retryCount++;
                                continue;
                            }
                            break;
                        }
                    }

                    if (response == null && lastEx != null)
                        return $"Error: {lastEx.Message}";

                    return JsonConvert.SerializeObject(response, Formatting.None);
                }
            }
            catch (Exception ex)
            {
                await _errorLogService.LogErrorAsync(
                    $"An error occurred while sending take profit order: {ex.Message}",
                    ex.StackTrace,
                    nameof(SendTakeProfitOrderAsync),
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
                    throw new Exception("Service scope factory is not initialized.");

                using (var scope = _scopeFactory.CreateScope())
                {
                    var context = scope.ServiceProvider.GetRequiredService<AutoSignalsDbContext>();

                    var okxClient = new ccxt.okx(new Dictionary<string, object>
                    {
                        { "apiKey", apiKey },
                        { "secret", apiSecret },
                        { "password", password },
                    });

                    // Futures (swap) mode
                    okxClient.options["defaultType"] = "swap";

                    // tdMode and posSide are required for close-position
                    var tdMode = order.IsIsolated ? "isolated" : "cross";

                    // If order.Side is sell => close long, else close short
                    var posSide = order.Side.Equals("sell", StringComparison.OrdinalIgnoreCase) ? "long" : "short";

                    // Ensure hedged (long/short) mode. Passing only the bool is sufficient for OKX in ccxt.
                    try
                    {
                        await okxClient.setPositionMode(true);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"setPositionMode failed: {ex.Message}");
                        // Non-fatal for closePosition; continue
                    }

                    // Required params for /trade/close-position
                    var closeParams = new Dictionary<string, object>
                    {
                        { "mgnMode", tdMode },
                        { "posSide", posSide },
                        { "ccy", "USDT" } // margin currency for USDT-margined swaps
                    };

                    Dictionary<string, object> response = null;
                    int retryCount = 0;
                    Exception lastEx = null;

                    while (retryCount < 3)
                    {
                        try
                        {
                            // Use posSide in params; do not pass it as the second argument
                            response = await okxClient.closePosition(order.Symbol, null, closeParams) as Dictionary<string, object>;

                            // OKX success code is "0"
                            if (response != null && (!response.ContainsKey("code") || response["code"]?.ToString() == "0"))
                                break;

                            // Handle rate limit retry
                            var msg = response?.ContainsKey("msg") == true ? response["msg"]?.ToString() : response?.ContainsKey("message") == true ? response["message"]?.ToString() : null;
                            if (!string.IsNullOrEmpty(msg) && msg.Contains("Too many requests", StringComparison.OrdinalIgnoreCase))
                            {
                                await Task.Delay(5000);
                                retryCount++;
                                continue;
                            }

                            break;
                        }
                        catch (ccxt.ExchangeError ex)
                        {
                            lastEx = ex;

                            // OKX: 22002 = No position to close
                            if (ex.Message.Contains("\"code\":\"22002\""))
                            {
                                var msg = ex.Message.Contains("\"msg\":\"")
                                    ? ex.Message.Split("\"msg\":\"")[1].Split("\"")[0]
                                    : "No position to close";
                                return $"Error: {msg} (code 22002)";
                            }

                            if (ex.Message.Contains("Too many requests", StringComparison.OrdinalIgnoreCase))
                            {
                                await Task.Delay(5000);
                                retryCount++;
                                continue;
                            }

                            break;
                        }
                        catch (Exception ex)
                        {
                            lastEx = ex;
                            if (ex.Message.Contains("Too many requests", StringComparison.OrdinalIgnoreCase))
                            {
                                await Task.Delay(5000);
                                retryCount++;
                                continue;
                            }
                            break;
                        }
                    }

                    // Handle response error 22002 if returned as a normal payload
                    if (response != null && response.TryGetValue("code", out var codeObj) && codeObj?.ToString() == "22002")
                    {
                        var msg = response.ContainsKey("msg") ? response["msg"].ToString() : "No position to close";
                        return $"Error: {msg} (code 22002)";
                    }

                    if (response == null && lastEx != null)
                        return $"Error: {lastEx.Message}";

                    return response != null ? JsonConvert.SerializeObject(response) : "No response from okx";
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred while sending Stoploss Order: {ex.Message}");
                await _errorLogService.LogErrorAsync(
                    $"An error occurred while sending Stoploss Order: {ex.Message}",
                    ex.StackTrace,
                    nameof(SendStoplossOrderAsync),
                    JsonConvert.SerializeObject(order, Formatting.Indented)
                );
                return $"Error: {ex.Message}";
            }
        }
    }
}
