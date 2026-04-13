using AutoSignals.Data;
using AutoSignals.Models;
using AutoSignals.Services;
using AutoSignals.Services.ExchangeAdapters;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Memory;
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
    private readonly AesEncryptionService _encryption_service;
    private readonly ExchangeOrderAdapterFactory _exchangeOrderAdapterFactory;
    private readonly IMemoryCache _cache;
    private static readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1); // Semaphore to limit concurrent access
    private const string ExchangesCacheKey = "EnabledExchanges";


    int savePrecision = 8;

    public OrderService(AutoSignalsDbContext context, ILogger<OrderService> logger, ErrorLogService errorLogService, IServiceScopeFactory scopeFactory, AesEncryptionService encryptionService, ExchangeOrderAdapterFactory exchangeOrderAdapterFactory, IMemoryCache cache)
    {
        _context = context;
        _logger = logger;
        _errorLogService = errorLogService;
        _scopeFactory = scopeFactory;
        _encryption_service = encryptionService;
        _exchangeOrderAdapterFactory = exchangeOrderAdapterFactory;
        _cache = cache;
    }

    public async Task CreateOrdersForActiveUsers(Signal signal)
    {
        _logger.LogInformation($"Starting order creation for signal: {signal.Symbol}");
        var startTime = DateTime.UtcNow;

        try
        {
            // Fetch all active users with a Pro or VIP subscription (active or on trial)
            var activeUsers = await _context.UsersData
                .Where(user => user.SubscriptionTier != SubscriptionTier.Freemium
                            && (user.SubscriptionStatus == SubscriptionStatus.Active
                             || user.SubscriptionStatus == SubscriptionStatus.Trial))
                .ToListAsync();

            if (!activeUsers.Any())
            {
                _logger.LogInformation("No active users found with an active subscription.");
                return;
            }

            // Validate symbol and fetch precisions
            var precisions = await GetPrecisionsAsync(signal.Symbol);
            if (precisions.Count == 0)
            {
                _logger.LogWarning($"No precision data found for symbol {signal.Symbol}. Check if exchanges are enabled and if the symbol is valid. Signal: {signal}");
                await _errorLogService.LogErrorAsync($"No precision data found for symbol {signal.Symbol}. Check if exchanges are enabled and if the symbol is valid.", null, "OrderService.CreateOrdersForActiveUsers");
                return;
            }

            foreach (var user in activeUsers)
            {
                try
                {
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

                    // Determine if any enabled provider setting for this user is in testing mode
                    bool anyTesting = providerSettings.Any(s => s.Testing);

                    // If user has no exchange selected and none of their provider settings are testing -> skip
                    if (!user.ExchangeId.HasValue && !anyTesting)
                    {
                        _logger.LogWarning($"Skipping user {user.Id} as they do not have an exchange selected.");
                        await _errorLogService.LogErrorAsync($"Skipping user {user.Id} as they do not have an exchange selected.", null, "OrderService.CreateOrdersForActiveUsers");
                        continue;
                    }

                    // If user has an exchange selected but precision for that exchange is missing and none of their provider settings are testing -> skip
                    if (user.ExchangeId.HasValue && !precisions.ContainsKey(user.ExchangeId.Value) && !anyTesting)
                    {
                        _logger.LogWarning($"Skipping user {user.Id} as their exchange ID {user.ExchangeId.Value} is not found in precisions.");
                        await _errorLogService.LogErrorAsync($"Skipping user {user.Id} as their exchange ID {user.ExchangeId.Value} is not found in precisions.", null, "OrderService.CreateOrdersForActiveUsers");
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

            // Resolve the exchange connection for this provider setting
            var connection = await ResolveConnectionAsync(scopedContext, user.Id, settings.ConnectionId);
            var effectiveExchangeId = connection != null ? (int?)connection.ExchangeId : user.ExchangeId;

            // Resolve precision data. If missing but this provider is in testing mode, create reasonable defaults for calculations.
            (string Name, decimal PricePrecision, decimal MinTradeUSDT, decimal AmountPrecision, int MinLeverage, int MaxLeverage) precision;
            if (effectiveExchangeId.HasValue && precisions.TryGetValue(effectiveExchangeId.Value, out var precisionData))
            {
                precision = precisionData;
            }
            else if (settings.Testing)
            {
                // Default precision suitable for test orders (uses user settings for min notional)
                precision = ("TEST", 0.0001m, (decimal)settings.MinTradeSizeUsd, 0.0001m, 1, 100);
                _logger.LogInformation($"Using default precision data for test order for user {user.Id}.");
            }
            else
            {
                _logger.LogWarning($"Precision data not found for user {user.Id} and exchange ID {effectiveExchangeId}");
                await _errorLogService.LogErrorAsync($"Precision data not found for user {user.Id} and exchange ID {effectiveExchangeId}", "OrderService.CreateOrderForUser");
                return;
            }

            // Get the user's balance from the resolved connection or fall back to UserData credentials
            var userBalance = connection != null
                ? await GetConnectionBalance(connection)
                : await GetUserBalance(user.ExchangeId, user.Id);
            if (userBalance <= 0 && !settings.Testing)
            {
                _logger.LogWarning($"User {user.Id} has insufficient balance. Exchange: {effectiveExchangeId}. Balance: {userBalance}.");
                await _errorLogService.LogErrorAsync($"User {user.Id} has insufficient balance. Exchange: {effectiveExchangeId}. Balance: {userBalance}",null, "OrderService.CreateOrderForUser");
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
            var entryOrders = CreateEntryOrders(signal, user, settings, (double)precision.MinTradeUSDT, tradeSizes["Entry"], leverage, stoploss, effectiveExchangeId);

            // Create stoploss order
            var stoplossOrder = CreateStoplossOrder(signal, user, settings, tradeSizes["StopLoss"], stoploss, leverage, effectiveExchangeId);

            // Create take profit orders
            var takeProfitOrders = CreateTakeProfitOrders(signal, user, settings, leverage, amountPrecision, effectiveExchangeId);
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

    private async Task<Dictionary<int, (string Name, decimal PricePrecision, decimal MinTradeUSDT, decimal AmountPrecision, int MinLeverage, int MaxLeverage)>> GetPrecisionsAsync(string symbol)
    {
        // Single query for all enabled exchanges, cached for 15 minutes (exchange list changes very rarely)
        if (!_cache.TryGetValue(ExchangesCacheKey, out Dictionary<string, Exchange>? exchangesByName))
        {
            var exchanges = await _context.Exchanges
                .Where(e => e.IsEnabled)
                .AsNoTracking()
                .ToListAsync();
            exchangesByName = exchanges.ToDictionary(e => e.Name);
            _cache.Set(ExchangesCacheKey, exchangesByName, TimeSpan.FromMinutes(15));
        }

        var precisions = new Dictionary<int, (string Name, decimal PricePrecision, decimal MinTradeUSDT, decimal AmountPrecision, int MinLeverage, int MaxLeverage)>();

        var bitgetMarket = await _context.BitgetMarkets.AsNoTracking().FirstOrDefaultAsync(m => m.Symbol == symbol);
        if (bitgetMarket != null && exchangesByName!.TryGetValue("Bitget", out var bitgetExchange))
            precisions[bitgetExchange.Id] = (bitgetExchange.Name, bitgetMarket.PricePrecision, bitgetMarket.MinTradeUSDT, bitgetMarket.AmountPrecision, bitgetMarket.MinLever, bitgetMarket.MaxLever);

        var bybitMarket = await _context.BybitMarkets.AsNoTracking().FirstOrDefaultAsync(m => m.Symbol == symbol);
        if (bybitMarket != null && exchangesByName!.TryGetValue("Bybit", out var bybitExchange))
            precisions[bybitExchange.Id] = (bybitExchange.Name, bybitMarket.PricePrecision, bybitMarket.MinTradeUSDT, bybitMarket.AmountPrecision, bybitMarket.MinLever, bybitMarket.MaxLever);

        var kuCoinMarket = await _context.KuCoinMarkets.AsNoTracking().FirstOrDefaultAsync(m => m.Symbol == symbol);
        if (kuCoinMarket != null && exchangesByName!.TryGetValue("KuCoin", out var kuCoinExchange))
            precisions[kuCoinExchange.Id] = (kuCoinExchange.Name, kuCoinMarket.PricePrecision, kuCoinMarket.MinTradeUSDT, kuCoinMarket.AmountPrecision, kuCoinMarket.MinLever, kuCoinMarket.MaxLever);

        var okxMarket = await _context.OkxMarkets.AsNoTracking().FirstOrDefaultAsync(m => m.Symbol == symbol);
        if (okxMarket != null && exchangesByName!.TryGetValue("Okx", out var okxExchange))
            precisions[okxExchange.Id] = (okxExchange.Name, okxMarket.PricePrecision, okxMarket.MinTradeUSDT, okxMarket.AmountPrecision, okxMarket.MinLever, okxMarket.MaxLever);

        var binanceMarket = await _context.BinanceMarkets.AsNoTracking().FirstOrDefaultAsync(m => m.Symbol == symbol);
        if (binanceMarket != null && exchangesByName!.TryGetValue("Binance", out var binanceExchange))
            precisions[binanceExchange.Id] = (binanceExchange.Name, binanceMarket.PricePrecision, binanceMarket.MinTradeUSDT, binanceMarket.AmountPrecision, binanceMarket.MinLever, binanceMarket.MaxLever);

        return precisions;
    }



    private async Task<UserExchangeConnection?> ResolveConnectionAsync(
        AutoSignalsDbContext ctx, string userId, int? connectionId)
    {
        if (connectionId.HasValue)
            return await ctx.UserExchangeConnections
                .FirstOrDefaultAsync(c => c.Id == connectionId && c.UserId == userId && c.IsActive);

        // No explicit assignment → use default active connection, fall back to any active
        var defaultConn = await ctx.UserExchangeConnections
            .Where(c => c.UserId == userId && c.IsActive && c.IsDefault)
            .FirstOrDefaultAsync();
        if (defaultConn != null) return defaultConn;

        return await ctx.UserExchangeConnections
            .Where(c => c.UserId == userId && c.IsActive)
            .FirstOrDefaultAsync();
    }

    private async Task<decimal> GetConnectionBalance(UserExchangeConnection connection)
    {
        var apiKey    = _encryption_service.Decrypt(connection.ApiKey ?? "");
        var apiSecret = _encryption_service.Decrypt(connection.ApiSecret ?? "");
        var apiPwd    = _encryption_service.Decrypt(connection.ApiPassword ?? "");

        if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(apiSecret))
        {
            _logger.LogWarning($"API credentials for connection {connection.Id} are missing.");
            return 0;
        }

        try
        {
            var adapter = await _exchangeOrderAdapterFactory.GetRequiredAdapterAsync(connection.ExchangeId);
            return await adapter.GetBalanceAsync(new ExchangeCredentials(apiKey, apiSecret, apiPwd));
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Failed to fetch balance for connection {connection.Id}. {ex.Message}");
            await _errorLogService.LogErrorAsync($"Failed to fetch balance for connection {connection.Id}. {ex.Message}", ex.StackTrace, "OrderService.GetConnectionBalance");
            return 0;
        }
    }

    private async Task<decimal> GetUserBalance(int? exchangeId, string userId)
    {
        var user = await _context.UsersData.FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null || !exchangeId.HasValue)
        {
            _logger.LogWarning($"User {userId} or exchange {exchangeId} not found.");
            return 0;
        }

        var apiKey = _encryption_service.Decrypt(user.ApiKey);
        var apiSecret = _encryption_service.Decrypt(user.ApiSecret);
        var apiPassword = _encryption_service.Decrypt(user.ApiPassword);

        if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(apiSecret))
        {
            _logger.LogWarning($"API credentials for user {userId} are missing.");
            return 0;
        }

        try
        {
            var adapter = await _exchangeOrderAdapterFactory.GetRequiredAdapterAsync(exchangeId.Value);
            var credentials = new ExchangeCredentials(apiKey, apiSecret, apiPassword);
            return await adapter.GetBalanceAsync(credentials);
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Unsupported or failing exchange ID {exchangeId} for user {userId}. {ex.Message}");
            await _errorLogService.LogErrorAsync($"Failed to fetch user balance for exchange {exchangeId}. {ex.Message}", ex.StackTrace, "OrderService.GetUserBalance");
            return 0;
        }
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
        if (settings.UseStoploss) 
        {
            var entry = signal.Entry;
            var stoplossPercentage = (float)settings.StoplossPercentage;
            double stoploss = signal.Side == "long" ? entry - (entry * stoplossPercentage / 100) : entry + (entry * stoplossPercentage / 100);

            // Format the stoploss to avoid scientific notation i.e. 1.2345E-5
            string formattedStoploss = stoploss.ToString("F8", CultureInfo.InvariantCulture);
            double parsedStoploss = double.Parse(formattedStoploss, CultureInfo.InvariantCulture);

            return Math.Round(parsedStoploss, savePrecision);
        }
        else
        {
            return signal.Stoploss > 0 ? signal.Stoploss : 0;
        }
    }

    private List<Order> CreateEntryOrders(Signal signal, UserData user, ProviderSettings settings, double minNotational, double tradeSize, int leverage, double stoploss, int? resolvedExchangeId = null)
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

        var exchangeIdString = resolvedExchangeId.HasValue ? resolvedExchangeId.Value.ToString() : (user.ExchangeId.HasValue ? user.ExchangeId.Value.ToString() : "TEST");

        // Create initial entry order
        entryOrders.Add(new Order
        {
            SignalId = signal.Id,
            UserId = user.Id,
            ExchangeId = exchangeIdString,
            TelegramId = user.TelegramId,
            PositionId = "",
            UserName = user.NickName,
            Symbol = signal.Symbol,
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
            ExchangeId = exchangeIdString,
            TelegramId = user.TelegramId,
            PositionId = "",
            UserName = user.NickName,
            Symbol = signal.Symbol,
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
            ExchangeId = exchangeIdString,
            TelegramId = user.TelegramId,
            PositionId = "",
            UserName = user.NickName,
            Symbol = signal.Symbol,
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

    private Order CreateStoplossOrder(Signal signal, UserData user, ProviderSettings settings, double tradeSize, double stoploss, int leverage, int? resolvedExchangeId = null)
    {
        var test = settings.Testing;
        var unifiedSymbol = signal.Symbol.Replace("USDT", "/USDT:USDT");

        var exchangeIdString = resolvedExchangeId.HasValue ? resolvedExchangeId.Value.ToString() : (user.ExchangeId.HasValue ? user.ExchangeId.Value.ToString() : "TEST");

        if (!settings.IgnoreStoploss)
        {
            
            return new Order
            {
                SignalId = signal.Id,
                UserId = user.Id,
                ExchangeId = exchangeIdString,
                TelegramId = user.TelegramId,
                PositionId = "",
                UserName = user.NickName,
                Symbol = signal.Symbol,
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
            ExchangeId = exchangeIdString,
            TelegramId = user.TelegramId,
            PositionId = "",
            UserName = user.NickName,
            Symbol = signal.Symbol,
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

    private List<Order> CreateTakeProfitOrders(Signal signal, UserData user, ProviderSettings settings, int leverage, int amountPrecision, int? resolvedExchangeId = null)
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

        var exchangeIdString = resolvedExchangeId.HasValue ? resolvedExchangeId.Value.ToString() : (user.ExchangeId.HasValue ? user.ExchangeId.Value.ToString() : "TEST");

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
                ExchangeId = exchangeIdString,
                TelegramId = user.TelegramId,
                PositionId = "",
                UserName = user.NickName,
                Symbol = signal.Symbol,
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
                ExchangeId = exchangeIdString,
                TelegramId = user.TelegramId,
                PositionId = "",
                UserName = user.NickName,
                Symbol = signal.Symbol,
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