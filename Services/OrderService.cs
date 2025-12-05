using AutoSignals.Data;
using AutoSignals.Models;
using AutoSignals.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nethereum.ABI.CompilationMetadata;
using NuGet.Protocol.Plugins;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Telegram.Bot.Types;
using static System.Net.Mime.MediaTypeNames;

public class OrderService
{
    private readonly AutoSignalsDbContext _context;
    private readonly ILogger<OrderService> _logger;
    private readonly ErrorLogService _errorLogService;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AesEncryptionService _encryptionService;
    private static readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1); // Semaphore to limit concurrent access


    int savePrecision = 8;

    public OrderService(AutoSignalsDbContext context, ILogger<OrderService> logger, ErrorLogService errorLogService, IServiceScopeFactory scopeFactory, AesEncryptionService encryptionService)
    {
        _context = context;
        _logger = logger;
        _errorLogService = errorLogService;
        _scopeFactory = scopeFactory;
        _encryptionService = encryptionService;
        _encryptionService = encryptionService;
    }

    public async Task CreateOrdersForActiveUsers(Signal signal)
    {
        _logger.LogInformation($"Starting order creation for signal: {signal.Symbol}");
        var startTime = DateTime.UtcNow;

        try
        {
            // Fetch all active users with an active subscription
            var activeUsers = await _context.UsersData
                .Where(user => user.SubscriptionActive == "1")
                .ToListAsync();

            if (!activeUsers.Any())
            {
                _logger.LogInformation("No active users found with an active subscription.");
                return;
            }

            // Validate symbol and fetch precisions
            var presisionSymbol = signal.Symbol.Replace("/USDT:USDT", "USDT");
            var precisions = GetPrecisions(presisionSymbol);
            if (precisions.Count == 0)
            {
                _logger.LogWarning($"No precision data found for symbol {presisionSymbol}. Check if exchanges are enabled and if the symbol is valid.");
                await _errorLogService.LogErrorAsync($"No precision data found for symbol {presisionSymbol}. Check if exchanges are enabled and if the symbol is valid.", null, "OrderService.CreateOrdersForActiveUsers");
                return;
            }

            foreach (var user in activeUsers)
            {
                try
                {
                    // Check if the user's exchange ID is in the precisions dictionary
                    if(!user.ExchangeId.HasValue)
                    {
                        _logger.LogWarning($"Skipping user {user.Id} as they do not have an exchange selected.");
                        await _errorLogService.LogErrorAsync($"Skipping user {user.Id} as they do not have an exchange selected.",null, "OrderService.CreateOrdersForActiveUsers");
                        continue;
                    }
                    if(!precisions.ContainsKey(user.ExchangeId.Value))
                    {
                        _logger.LogWarning($"Skipping user {user.Id} as their exchange ID {user.ExchangeId.Value} is not found in precisions.");
                        await _errorLogService.LogErrorAsync($"Skipping user {user.Id} as their exchange ID {user.ExchangeId.Value} is not found in precisions.",null, "OrderService.CreateOrdersForActiveUsers");
                        continue;
                    }

                    using var scope = _scopeFactory.CreateScope();
                    var scopedContext = scope.ServiceProvider.GetRequiredService<AutoSignalsDbContext>();

                    // Fetch the provider settings for the user
                    var providerSettings = await scopedContext.ProvidersSettings
                        .Where(settings => settings.UserId == user.Id && settings.IsEnabled)
                        .ToListAsync();

                    if (!providerSettings.Any())
                    {
                        _logger.LogInformation($"No enabled provider settings found for user {user.Id}. Skipping.");
                        continue;
                    }

                    foreach (var settings in providerSettings)
                    {
                        await CreateOrderForUser(signal, user, settings, precisions);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error processing user {user.Id}: {ex.Message}");
                    await _errorLogService.LogErrorAsync($"Error processing user {user.Id}: {ex.Message}", ex.StackTrace, "OrderService.CreateOrdersForActiveUsers");
                }
            }

            _logger.LogInformation($"Order creation completed for signal: {signal.Symbol}");
            var endTime = DateTime.UtcNow;
            var duration = endTime - startTime;
            _logger.LogInformation($"Order creation took {duration.TotalSeconds} seconds.");
            //await _telegramBotService.LoggError($"Order creation completed and took {duration.TotalSeconds} seconds.");
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error creating orders for active users: {ex.Message}");
            await _errorLogService.LogErrorAsync($"Error creating orders for active users: {ex.Message}", ex.StackTrace, "OrderService.CreateOrdersForActiveUsers");
        }
    }

    private async Task CreateOrderForUser(Signal signal, UserData user, ProviderSettings settings, Dictionary<int, (string Name, decimal PricePrecision, decimal MinTradeUSDT, decimal AmountPrecision, int MinLeverage, int MaxLeverage)> precisions)
    {
        // Ignore long/short signals based on user settings
        if ((signal.Side == "long" && settings.IgnorLong) ||
            (signal.Side == "short" && settings.IgnorShort))
        {
            _logger.LogInformation($"User {user.Id} is set to ignore {(signal.Side == "long" ? "long" : "short")} signals. Skipping order creation.");
            return;
        }
        using var scope = _scopeFactory.CreateScope();
        var scopedContext = scope.ServiceProvider.GetRequiredService<AutoSignalsDbContext>();

        using var transaction = await scopedContext.Database.BeginTransactionAsync();
        try
        {
            _logger.LogInformation($"Creating order for user {user.Id} with signal {signal.Symbol}");

            // Check if the signal provider matches the user's provider settings
            if (signal.Provider != settings.ProviderId)
            {
                _logger.LogInformation($"Signal provider {signal.Provider} does not match user's provider settings {settings.ProviderId}. Skipping user {user.Id}.");
                return;
            }

            // Get the precision data for the user's exchange ID
            if (!user.ExchangeId.HasValue || !precisions.TryGetValue(user.ExchangeId.Value, out var precisionData))
            {
                _logger.LogWarning($"Precision data not found for user {user.Id} and exchange ID {user.ExchangeId}");
                await _errorLogService.LogErrorAsync($"Precision data not found for user {user.Id} and exchange ID {user.ExchangeId}", "OrderService.CreateOrderForUser");
                return;
            }

            var precision = precisions[user.ExchangeId.Value];

            // Get the user's balance from the exchange
            var userBalance = await GetUserBalance(user.ExchangeId, user.Id);
            if (userBalance <= 0 && !settings.Testing)
            {
                _logger.LogWarning($"User {user.Id} has insufficient balance. Exchange: {user.ExchangeId}. Balance: {userBalance}.");
                await _errorLogService.LogErrorAsync($"User {user.Id} has insufficient balance. Exchange: {user.ExchangeId}. Balance: {userBalance}",null, "OrderService.CreateOrderForUser");
                return;
            }

            // Calculate the size of the trade
            var tradeSizes = settings.Testing
                ? new Dictionary<string, double> { { "Entry", settings.MinTradeSizeUsd }, { "StopLoss", 0 } }
                : CalculateTradeSize((double)userBalance, settings, signal, precision);

            if (tradeSizes["Entry"] <= 0)
            {
                _logger.LogWarning($"User {user.Id}. Error calculating trade size.");
                await _errorLogService.LogErrorAsync($"Error calculating trade size for user {user.Id}. User Balance: {userBalance}. Signal: {signal}. Exchange: {user.ExchangeId}",null, "OrderService.CreateOrderForUser");
                return;
            }

            // Calculate stoploss and leverage
            var stoploss = CalculateStoploss(signal, settings);
            int pricePrecision = Math.Clamp((int)Math.Log10((double)1 / (double)precision.PricePrecision), 0, 15);
            int amountPrecision = Math.Clamp((int)Math.Log10((double)1 / (double)precision.AmountPrecision), 0, 15);
            stoploss = Math.Round(stoploss, pricePrecision);
            var leverage = settings.OverideLeverage ? settings.Leverage : signal.Leverage;

            // Create entry orders
            var entryOrders = CreateEntryOrders(signal, user, settings, (double)precision.MinTradeUSDT, tradeSizes["Entry"], leverage, stoploss);

            // Create stoploss order
            var stoplossOrder = CreateStoplossOrder(signal, user, settings, tradeSizes["StopLoss"], stoploss, leverage);

            // Create take profit orders
            var takeProfitOrders = CreateTakeProfitOrders(signal, user, settings, leverage, amountPrecision);
            if (takeProfitOrders == null)
            {
                _logger.LogWarning($"Error creating take profit orders for user {user.Id}");
                await _errorLogService.LogErrorAsync($"Error creating take profit orders for user {user.Id}", "OrderService.CreateOrderForUser");
                return;
            }

            // Save all orders to the database
            scopedContext.Orders.AddRange(entryOrders);
            scopedContext.Orders.Add(stoplossOrder);
            scopedContext.Orders.AddRange(takeProfitOrders);
            await SaveChangesWithRetryAsync(scopedContext);

            // Commit the transaction
            await transaction.CommitAsync();
            _logger.LogInformation($"Successfully created orders for user {user.Id} with signal {signal.Symbol}");
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error creating order for user {user.Id}: {ex.Message}");
            await _errorLogService.LogErrorAsync($"Error creating order for user {user.Id}: {ex.Message}", ex.StackTrace, "OrderService.CreateOrderForUser");

            // Rollback the transaction
            await transaction.RollbackAsync();
        }
    }

    private async Task SaveChangesWithRetryAsync(AutoSignalsDbContext context, int maxRetries = 3, int delayMilliseconds = 500)
    {
        int retryCount = 0;
        while (true)
        {
            try
            {
                await context.SaveChangesAsync();
                break; // Success!
            }
            catch (DbUpdateException ex) when (retryCount < maxRetries)
            {
                _logger.LogWarning($"SaveChangesAsync failed (attempt {retryCount + 1}). Retrying... Error: {ex.Message}");
                await _errorLogService.LogErrorAsync($"SaveChangesAsync failed (attempt {retryCount + 1}). Retrying... Error: {ex.Message}", ex.StackTrace, "OrderService.SaveChangesWithRetryAsync");
                retryCount++;
                await Task.Delay(delayMilliseconds);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Fatal error during SaveChangesWithRetry: {ex.Message}");
                await _errorLogService.LogErrorAsync($"Fatal error during SaveChangesWithRetry: {ex.Message}", ex.StackTrace, "OrderService.SaveChangesWithRetryAsync");
                throw; // Don't hide unexpected errors
            }
        }
    }

    private Dictionary<int, (string Name, decimal PricePrecision, decimal MinTradeUSDT, decimal AmountPrecision, int MinLeverage, int MaxLeverage)> GetPrecisions(string symbol)
    {
        var precisions = new Dictionary<int, (string Name, decimal PricePrecision, decimal MinTradeUSDT, decimal AmountPrecision, int MinLeverage, int MaxLeverage)>();

        // Fetch precisions from BitgetMarket
        var bitgetMarket = _context.BitgetMarkets.FirstOrDefault(m => m.Symbol == symbol);
        if (bitgetMarket != null)
        {
            var exchange = _context.Exchanges.FirstOrDefault(e => e.Name == "Bitget" && e.IsEnabled == true);
            if (exchange != null)
            {
                precisions[exchange.Id] = (exchange.Name, bitgetMarket.PricePrecision, bitgetMarket.MinTradeUSDT, bitgetMarket.AmountPrecision, bitgetMarket.MinLever, bitgetMarket.MaxLever);
            }
        }

        // Fetch precisions from BybitMarket
        var bybitMarket = _context.BybitMarkets.FirstOrDefault(m => m.Symbol == symbol);
        if (bybitMarket != null)
        {
            var exchange = _context.Exchanges.FirstOrDefault(e => e.Name == "Bybit" && e.IsEnabled == true);
            if (exchange != null)
            {
                precisions[exchange.Id] = (exchange.Name, bybitMarket.PricePrecision, bybitMarket.MinTradeUSDT, bybitMarket.AmountPrecision, bybitMarket.MinLever, bybitMarket.MaxLever);
            }
        }

        // Fetch precisions from KuCoinMarket
        var kuCoinMarket = _context.KuCoinMarkets.FirstOrDefault(m => m.Symbol == symbol);
        if (kuCoinMarket != null)
        {
            var exchange = _context.Exchanges.FirstOrDefault(e => e.Name == "KuCoin" && e.IsEnabled == true);
            if (exchange != null)
            {
                precisions[exchange.Id] = (exchange.Name, kuCoinMarket.PricePrecision, kuCoinMarket.MinTradeUSDT, kuCoinMarket.AmountPrecision, kuCoinMarket.MinLever, kuCoinMarket.MaxLever);
            }
        }

        // Fetch precisions from OkxMarket
        var okxMarket = _context.OkxMarkets.FirstOrDefault(m => m.Symbol == symbol);
        if (okxMarket != null)
        {
            var exchange = _context.Exchanges.FirstOrDefault(e => e.Name == "Okx" && e.IsEnabled == true);
            if (exchange != null)
            {
                precisions[exchange.Id] = (exchange.Name, okxMarket.PricePrecision, okxMarket.MinTradeUSDT, okxMarket.AmountPrecision, okxMarket.MinLever, okxMarket.MaxLever);
            }
        }

        // Fetch precisions from BinanceMarket
        var binanceMarket = _context.BinanceMarkets.FirstOrDefault(m => m.Symbol == symbol);
        if (binanceMarket != null)
        {
            var exchange = _context.Exchanges.FirstOrDefault(e => e.Name == "Binance" && e.IsEnabled == true);
            if (exchange != null)
            {
                precisions[exchange.Id] = (exchange.Name, binanceMarket.PricePrecision, binanceMarket.MinTradeUSDT, binanceMarket.AmountPrecision, binanceMarket.MinLever, binanceMarket.MaxLever);
            }
        }

        return precisions;
    }




    private async Task<decimal> GetUserBalance(int? exchangeId, string userId)
    {
        // Fetch user data
        var user = await _context.UsersData.FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null || !exchangeId.HasValue)
        {
            _logger.LogWarning($"User {userId} or exchange {exchangeId} not found.");
            return 0;
        }

        // Get API credentials
        var apiKey = _encryptionService.Decrypt(user.ApiKey);
        var apiSecret = _encryptionService.Decrypt(user.ApiSecret);
        var apiPassword = _encryptionService.Decrypt(user.ApiPassword);

        if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(apiSecret))
        {
            _logger.LogWarning($"API credentials for user {userId} are missing.");
            return 0;
        }

        // Call the appropriate GetBalance function based on the exchange
        decimal balance = 0;
        switch (exchangeId)
        {
            case 1: // Bitget
                var bitgetService = new BitgetPriceService(apiKey, apiSecret, apiPassword, _errorLogService, _scopeFactory);
                balance = await bitgetService.GetBalance(apiKey, apiSecret, apiPassword);
                break;
            case 2: // Binance
                var binanceService = new BinancePriceService(apiKey, apiSecret, _context);
                balance = await binanceService.GetBalance(apiKey, apiSecret, apiPassword);
                break;
            case 3: // Bybit
                var bybitService = new BybitPriceService(apiKey, apiSecret, _context);
                balance = await bybitService.GetBalance(apiKey, apiSecret, apiPassword);
                break;
            case 4: // Okx
                var okxService = new OkxPriceService(apiKey, apiSecret, apiPassword, _errorLogService, _scopeFactory);
                balance = await okxService.GetBalance(apiKey, apiSecret, apiPassword);
                break;
            case 5: // KuCoin
                var kuCoinService = new KuCoinPriceService(apiKey, apiSecret, apiPassword, _context);
                balance = await kuCoinService.GetBalance(apiKey, apiSecret, apiPassword);
                break;
            default:
                _logger.LogWarning($"Unsupported exchange ID {exchangeId} for user {userId}.");
                break;
        }

        return balance;
    }

    private Dictionary<string, double> CalculateTradeSize(
    double userBalance,
    ProviderSettings settings,
    Signal signal,
    (string Name, decimal PricePrecision, decimal MinTradeUSDT, decimal AmountPrecision, int MinLeverage, int MaxLeverage) precision)
    {
        using var _ = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["Symbol"] = signal?.Symbol,
            ["Exchange"] = precision.Name,
            ["Side"] = signal?.Side
        });

        try
        {
            var entryPrice = signal.Entry;
            var costBeforeClamp = userBalance * (settings.RiskPercentage / 100);
            var exchangeMinNotionalCost = (double)precision.MinTradeUSDT;

            // Calculate amount precision correctly
            var amountPrecision = precision.AmountPrecision >= 1
                ? 0
                : (int)Math.Log10((double)1 / (double)precision.AmountPrecision);

            _logger.LogDebug(
                "Starting trade size calculation. Entry: {Entry}, UserBalance: {UserBalance}, RiskPercent: {RiskPercent}, AmountPrecision: {AmountPrecision}, ExchangeMinNotionalUSD: {ExchangeMinNotionalUSD}",
                entryPrice, userBalance, settings.RiskPercentage, amountPrecision, exchangeMinNotionalCost);

            // Apply user-defined max/min trade size limits
            var cost = Math.Clamp(costBeforeClamp, settings.MinTradeSizeUsd, settings.MaxTradeSizeUsd);
            if (!cost.Equals(costBeforeClamp))
            {
                _logger.LogInformation(
                    "Risk-based cost clamped. Before: {Before}, After: {After}, Min: {Min}, Max: {Max}",
                    costBeforeClamp, cost, settings.MinTradeSizeUsd, settings.MaxTradeSizeUsd);
            }

            // Determine leverage within allowed limits
            var leverageRequested = settings.OverideLeverage ? settings.Leverage : signal.Leverage;
            var leverage = Math.Clamp(leverageRequested, precision.MinLeverage, precision.MaxLeverage);
            if (leverage != leverageRequested)
            {
                _logger.LogInformation(
                    "Leverage clamped. Requested: {Requested}, Applied: {Applied}, AllowedRange: {Min}-{Max}",
                    leverageRequested, leverage, precision.MinLeverage, precision.MaxLeverage);
            }
            else
            {
                _logger.LogDebug("Leverage applied. Value: {Leverage}", leverage);
            }

            var notional = cost * leverage;
            var totalSize = notional / entryPrice;

            // Ensure trade size meets the exchange's min notional requirement
            if (notional < exchangeMinNotionalCost)
            {
                _logger.LogWarning(
                    "Trade notional below exchange minimum. NotionalUSD: {NotionalUSD}, MinRequiredUSD: {MinRequiredUSD}",
                    notional, exchangeMinNotionalCost);

                // Keep return keys consistent with consumers
                return new Dictionary<string, double> { { "Entry", 0 }, { "StopLoss", 0 } };
            }

            // Ensure SL size is always slightly greater than total size
            var stopLossSize = totalSize * 1.01;

            _logger.LogDebug(
                "Calculated sizes. TotalSize: {TotalSize}, StopLossSize: {StopLossSize}, EntryPrice: {EntryPrice}",
                totalSize, stopLossSize, entryPrice);

            return new Dictionary<string, double>
            {
                { "Entry", Math.Round(totalSize, amountPrecision) },
                { "StopLoss", Math.Round(stopLossSize, amountPrecision) }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error calculating trade size. UserBalance: {UserBalance}, Entry: {Entry}, Side: {Side}, RiskPercent: {RiskPercent}, MinUSD: {MinUSD}, MaxUSD: {MaxUSD}, Exchange: {Exchange}",
                userBalance,
                signal?.Entry,
                signal?.Side,
                settings.RiskPercentage,
                settings.MinTradeSizeUsd,
                settings.MaxTradeSizeUsd,
                precision.Name);

            return new Dictionary<string, double> { { "Entry", 0 }, { "StopLoss", 0 } };
        }
    }


    private double CalculateStoploss(Signal signal, ProviderSettings settings)
    {
        var entry = signal.Entry;
        var stoplossPercentage = (float)settings.StoplossPercentage;
        double stoploss = signal.Side == "long" ? entry - (entry * stoplossPercentage / 100) : entry + (entry * stoplossPercentage / 100);

        // Format the stoploss to avoid scientific notation i.e. 1.2345E-5
        string formattedStoploss = stoploss.ToString("F8", CultureInfo.InvariantCulture);
        double parsedStoploss = double.Parse(formattedStoploss, CultureInfo.InvariantCulture);

        return Math.Round(parsedStoploss, savePrecision);
    }

    private List<Order> CreateEntryOrders(Signal signal, UserData user, ProviderSettings settings, double minNotational, double tradeSize, int leverage, double stoploss)
    {
        var entryOrders = new List<Order>();

        // Split into 50% (Initial), 20% (DCA1), 30% (DCA2)
        double initialSize = tradeSize * 0.50;
        double dca1Size = tradeSize * 0.20;
        double dca2Size = tradeSize * 0.30;

        // Ensure each order meets the exchange's min notational requirement
        if (initialSize * signal.Entry < minNotational)
        {
            initialSize = minNotational / signal.Entry;
        }
        if (dca1Size * signal.Entry < minNotational)
        {
            dca1Size = minNotational / signal.Entry;
        }
        if (dca2Size * signal.Entry < minNotational)
        {
            dca2Size = minNotational / signal.Entry;
        }

        // Adjust sizes to maintain total trade size
        double adjustedTotalSize = initialSize + dca1Size + dca2Size;
        if (adjustedTotalSize > tradeSize)
        {
            double scale = tradeSize / adjustedTotalSize;
            initialSize *= scale;
            dca1Size *= scale;
            dca2Size *= scale;
        }

        // Calculate DCA prices
        double dca1Price, dca2Price;
        if (signal.Side == "long")
        {
            dca1Price = signal.Entry + (stoploss - signal.Entry) / 3;
            dca2Price = signal.Entry + 2 * (stoploss - signal.Entry) / 3;
        }
        else // SELL
        {
            dca1Price = signal.Entry - (signal.Entry - stoploss) / 3;
            dca2Price = signal.Entry - 2 * (signal.Entry - stoploss) / 3;
        }

        // The exchange wants Buy or Sell for Futures trading
        var side = signal.Side == "long" ? "buy" : "sell";

        var stoplossValue = 0.0;
        if (!settings.IgnoreStoploss)
        {
            stoplossValue = signal.Stoploss > 0 ? signal.Stoploss : stoploss;
        }
        

        var test = settings.Testing;

        var unifiedSymbol = signal.Symbol.Replace("USDT", "/USDT:USDT");

        // Create initial entry order
        entryOrders.Add(new Order
        {
            SignalId = signal.Id,
            UserId = user.Id,
            ExchangeId = user.ExchangeId.ToString(),
            TelegramId = user.TelegramId,
            PositionId = "",
            UserName = user.NickName,
            Symbol = unifiedSymbol,
            Side = side,
            Price = Math.Round(signal.Entry, savePrecision),
            Stoploss = (double)stoplossValue,
            Size = initialSize,
            Leverage = leverage,
            Status = "OPEN",
            IsIsolated = settings.IsIsolated,
            IsTest = test,
            Description = "Initial Entry Order",
            Time = DateTime.UtcNow
        });

        // Create DCA1 entry order
        entryOrders.Add(new Order
        {
            SignalId = signal.Id,
            UserId = user.Id,
            ExchangeId = user.ExchangeId.ToString(),
            TelegramId = user.TelegramId,
            PositionId = "",
            UserName = user.NickName,
            Symbol = unifiedSymbol,
            Side = side,
            Price = Math.Round(dca1Price, savePrecision),
            Stoploss = (double)stoplossValue,
            Size = dca1Size,
            Leverage = leverage,
            Status = "PENDING",
            IsIsolated = settings.IsIsolated,
            IsTest = test,
            Description = "DCA1 Entry Order",
            Time = DateTime.UtcNow
        });

        // Create DCA2 entry order
        entryOrders.Add(new Order
        {
            SignalId = signal.Id,
            UserId = user.Id,
            ExchangeId = user.ExchangeId.ToString(),
            TelegramId = user.TelegramId,
            PositionId = "",
            UserName = user.NickName,
            Symbol = unifiedSymbol,
            Side = side,
            Price = Math.Round(dca2Price, savePrecision),
            Stoploss = (double)stoplossValue,
            Size = dca2Size,
            Leverage = leverage,
            Status = "PENDING",
            IsIsolated = settings.IsIsolated,
            IsTest = test,
            Description = "DCA2 Entry Order",
            Time = DateTime.UtcNow
        });

        return entryOrders;
    }

    private Order CreateStoplossOrder(Signal signal, UserData user, ProviderSettings settings, double tradeSize, double stoploss, int leverage)
    {
        var test = settings.Testing;
        var unifiedSymbol = signal.Symbol.Replace("USDT", "/USDT:USDT");

        if (!settings.IgnoreStoploss)
        {
            
            return new Order
            {
                SignalId = signal.Id,
                UserId = user.Id,
                ExchangeId = user.ExchangeId.ToString(),
                TelegramId = user.TelegramId,
                PositionId = "",
                UserName = user.NickName,
                Symbol = unifiedSymbol,
                Side = signal.Side == "long" ? "sell" : "buy",
                Price = Math.Round(stoploss, savePrecision),
                Stoploss = Math.Round(stoploss, savePrecision),
                Size = tradeSize,
                Leverage = leverage,
                Status = "PENDING",
                IsIsolated = settings.IsIsolated,
                IsTest = test,
                Description = "Stoploss Order",
                Time = DateTime.UtcNow
            };
        }
        return new Order
        {
            SignalId = signal.Id,
            UserId = user.Id,
            ExchangeId = user.ExchangeId.ToString(),
            TelegramId = user.TelegramId,
            PositionId = "",
            UserName = user.NickName,
            Symbol = unifiedSymbol,
            Side = signal.Side == "long" ? "sell" : "buy",
            Price = 0,
            Stoploss = 0,
            Size = tradeSize,
            Leverage = leverage,
            Status = "CLOSED",
            IsIsolated = settings.IsIsolated,
            IsTest = test,
            Description = "Stoploss Order",
            Time = DateTime.UtcNow
        };
        
    }

    private List<Order> CreateTakeProfitOrders(Signal signal, UserData user, ProviderSettings settings, int leverage, int amountPrecision)
    {
        var test = settings.Testing;
        var takeProfitOrders = new List<Order>();
        var takeProfitTargets = signal.TakeProfits
            .Split(',')
            .Select(s => double.Parse(s, CultureInfo.InvariantCulture))
            .ToList();
        var takeProfitCount = takeProfitTargets.Count; 

        var takeProfitPercentages = settings.TpPercentages ?? new List<double>();
        var unifiedSymbol = signal.Symbol.Replace("USDT", "/USDT:USDT");

        // Ensure the percentages list matches the number of take profit targets
        while (takeProfitPercentages.Count < takeProfitCount)
        {
            takeProfitPercentages.Add(0); // Default to 0 if not enough percentages are provided
        }

        double totalPercentage = takeProfitPercentages.Sum();
        if (totalPercentage <= 0)
        {
            _logger.LogWarning("Total take profit percentage is zero or invalid.");
            return null;
        }

        double moonbagSize = 0;
        if (settings.UseMoonbag)
        {
            moonbagSize = settings.MoonbagPercentage;
            totalPercentage -= moonbagSize;
        }

        for (int i = 0; i < takeProfitCount; i++)
        {
            double takeProfitSize = takeProfitPercentages[i];
            if (takeProfitSize <= 0)
            {
                _logger.LogWarning($"Take profit size for TP{i + 1} is zero or invalid.");
                continue;
            }

            var description = $"Take Profit Order {i + 1}";
            if (settings.MoveStoploss && i + 1 == settings.MoveStoplossOn)
            {
                description += " + MSL";
            }

            takeProfitOrders.Add(new Order
            {
                SignalId = signal.Id,
                UserId = user.Id,
                ExchangeId = user.ExchangeId.ToString(),
                TelegramId = user.TelegramId,
                PositionId = "",
                UserName = user.NickName,
                Symbol = unifiedSymbol,
                Side = signal.Side == "long" ? "sell" : "buy",
                Price = (double)Math.Round(takeProfitTargets[i], savePrecision),
                Stoploss = 0,
                Size = Math.Round(takeProfitSize, 2),
                Leverage = leverage,
                Status = "PENDING",
                IsIsolated = settings.IsIsolated,
                IsTest = test,
                Description = description,
                Time = DateTime.UtcNow
            });

            // If the current take profit size is 100, stop creating further orders
            if (takeProfitSize == 100)
            {
                _logger.LogInformation($"Take profit size for TP{i + 1} is 100%. No further orders will be created.");
                break;
            }
        }



        if (settings.UseMoonbag && moonbagSize > 0)
        {
            double moonbagPrice = 0;
            if (signal.Side == "long")
            {
                moonbagPrice = signal.Entry * (1 + settings.MoonbagPercentage / 100.0);
            }
            else
            {
                moonbagPrice = signal.Entry * (1 - settings.MoonbagPercentage / 100.0);
            }

            takeProfitOrders.Add(new Order
            {
                SignalId = signal.Id,
                UserId = user.Id,
                ExchangeId = user.ExchangeId.ToString(),
                TelegramId = user.TelegramId,
                PositionId = "",
                UserName = user.NickName,
                Symbol = unifiedSymbol,
                Side = signal.Side == "long" ? "sell" : "buy",
                Price = Math.Round(moonbagPrice, savePrecision),
                Stoploss = 0,
                Size = 100, // This needs to be 100% to close the full remaining position, **safeguard MB is handled as SL and closes full position
                Leverage = leverage,
                Status = "PENDING",
                IsIsolated = settings.IsIsolated,
                IsTest = test,
                Description = "Moonbag Order",
                Time = DateTime.UtcNow
            });
        }

        return takeProfitOrders;
    }
}