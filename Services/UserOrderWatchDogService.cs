using AutoSignals.Data;
using AutoSignals.Models;
using AutoSignals.Services.ExchangeAdapters;
using AutoSignals.ViewModels;
using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;
using System.Text.Json;

namespace AutoSignals.Services
{
    public class UserOrderWatchDogService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<UserOrderWatchDogService> _logger;
        private readonly AesEncryptionService _encryptionService;
        private readonly ErrorLogService _errorLogService;


        public UserOrderWatchDogService(
        IServiceScopeFactory scopeFactory,
        ILogger<UserOrderWatchDogService> logger,
        AesEncryptionService encryptionService,
        ErrorLogService errorLogService)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _encryptionService = encryptionService;
            _errorLogService = errorLogService;
        }

        public async Task TriggerOrderProcessing()
        {
            var startTime = DateTime.UtcNow;
            Console.WriteLine("Triggering order processing...");
            await ProcessOrdersAsync();
            var endTime = DateTime.UtcNow;
            Console.WriteLine($"Order processing completed in {(endTime - startTime).TotalSeconds} seconds");
        }

        private async Task ProcessOrdersAsync()
        {
            using (var scope = _scopeFactory.CreateScope())
            {
                var _context = scope.ServiceProvider.GetRequiredService<AutoSignalsDbContext>();

                // Fetch all necessary data in a single query
                var openOrders = await _context.Orders
                    .Where(o => o.Status == "OPEN")
                    .ToListAsync();
                var openPositions = await _context.Positions
                    .Where(p => p.Status == "OPEN")
                    .ToListAsync();

                // Pre-load user data and related order lookup to avoid per-order DB queries (S3)
                var distinctUserIds = openOrders.Select(o => o.UserId).Distinct().ToList();
                var userDataMap = await _context.UsersData
                    .Where(u => distinctUserIds.Contains(u.Id))
                    .ToDictionaryAsync(u => u.Id);

                var ordersByKey = openOrders
                    .GroupBy(o => (o.UserId, o.SignalId, o.Symbol))
                    .ToDictionary(g => g.Key, g => g.ToList());

                var symbols = openOrders.Select(o => o.Symbol).Distinct().ToList();
                Dictionary<string, decimal> priceData = new Dictionary<string, decimal>();

                // Fetch latest prices
                try
                {
                    priceData = await FetchLatestPricesAsync(symbols);
                }
                catch (Exception ex)
                {
                    var symbolList = string.Join(", ", symbols);
                    _logger.LogError(ex, "Failed to fetch latest prices from API for symbols: {Symbols}. Exception: {Message}, StackTrace: {StackTrace}",
                        symbolList, ex.Message, ex.StackTrace);

                    await _errorLogService.LogErrorAsync(
                        $"Failed to fetch latest prices from API for symbols: {symbolList}",
                        ex.StackTrace, "UserOrderWatchDogService.ProcessOrdersAsync", $"Symbols: {symbolList}");

                    // Fetch prices from the database as a backup
                    priceData = _context.GeneralAssetPrices
                        .Where(p => symbols.Contains(p.Symbol))
                        .ToDictionary(p => p.Symbol, p => p.Price);
                }

                // Update positions and close if necessary
                await HandleOpenPositionsAsync(priceData, _context);

                // Update position ROIs
                foreach (var symbol in priceData.Keys)
                {
                    var currentPrice = priceData[symbol];
                    var matchingPositions = openPositions.Where(p => p.Symbol == symbol).ToList();

                    foreach (var position in matchingPositions)
                    {
                        var newROI = CalculateUnrealizedROI(position, (double)currentPrice);
                        if (Math.Abs(newROI - position.ROI) > 0.0001)
                        {
                            position.ROI = newROI;
                            _context.Positions.Update(position);
                        }
                    }
                }

                // Filter and cancel orders older than 24 hours
                var ordersToCancel = openOrders
                    .Where(o => (o.Description == "Initial Entry Order" || o.Description.Contains("DCA Entry Order")) && o.Time < DateTime.UtcNow.AddHours(-24))
                    .ToList();

                foreach (var order in ordersToCancel)
                {
                    await CancelOrderAsync(order, _context);
                }

                // Process remaining orders sequentially
                var remainingOrders = openOrders.Except(ordersToCancel).ToList();
                foreach (var order in remainingOrders)
                {
                    await ProcessOrderAsync(order, priceData, _context, userDataMap, ordersByKey);
                }

                // Save all changes at the end
                try
                {
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException ex)
                {
                    _logger.LogWarning("Concurrency conflict detected. The position/order was modified by another process.");
                    // Optionally reload the entity and skip updating if it's now closed
                    foreach (var entry in ex.Entries)
                    {
                        if (entry.Entity is Position)
                        {
                            await entry.ReloadAsync();
                            var position = (Position)entry.Entity;
                            if (position.Status == "CLOSED")
                            {
                                _logger.LogInformation($"Position {position.Id} was closed by another process. Skipping update.");
                                continue;
                            }
                        }
                        // Handle other entity types as needed
                    }
                    // Optionally retry or just abort
                }
            }
        }

        private async Task ProcessOrderAsync(Order order, Dictionary<string, decimal> priceData, AutoSignalsDbContext _context, Dictionary<string, UserData> userDataMap, Dictionary<(string, int, string), List<Order>> ordersByKey)
        {
            // Check if the current price data contains the symbol for the order
            if (!priceData.TryGetValue(order.Symbol, out var currentPrice))
            {
                try
                {
                    // Try to fetch the latest known price for this symbol from the general prices table
                    var generalPrice = await _context.GeneralAssetPrices
                        .AsNoTracking()
                        .FirstOrDefaultAsync(p => p.Symbol == order.Symbol);

                    if (generalPrice != null)
                    {
                        currentPrice = generalPrice.Price;

                        // Optionally add it into the in-memory cache so subsequent lookups can use it
                        priceData[order.Symbol] = currentPrice;

                        _logger.LogInformation(
                            "Fallback price for symbol {Symbol} loaded from GeneralAssetPrices for order {OrderId}: {Price}",
                            order.Symbol,
                            order.Id,
                            currentPrice);
                    }
                    else
                    {
                        await _errorLogService.LogErrorAsync(
                            $"Current price data for symbol {order.Symbol} not found for order {order.Id}, and no entry found in GeneralAssetPrices.",
                            null,
                            "UserOrderWatchDogService.ProcessOrderAsync",
                            $"Order ID: {order.Id}, Symbol: {order.Symbol}");

                        // Cannot proceed without a price
                        return;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Error fetching fallback price from GeneralAssetPrices for order {OrderId}, symbol {Symbol}. Exception: {Message}",
                        order.Id,
                        order.Symbol,
                        ex.Message);

                    await _errorLogService.LogErrorAsync(
                        $"Error fetching fallback price from GeneralAssetPrices for order {order.Id}, symbol {order.Symbol}.",
                        ex.StackTrace,
                        "UserOrderWatchDogService.ProcessOrderAsync",
                        $"Order ID: {order.Id}, Symbol: {order.Symbol}");

                    // On any error, we cannot safely execute the order without a price
                    return;
                }
            }

            // For testing only - allows to execute order regardless of price
            //await ExecuteOrderAsync(order, currentPrice, _context);
            //

            // Define slippage bounds (0.5% slippage)
            var slippage = 0.005m;
            var lowerBound = (decimal)order.Price * (1 - slippage);
            var upperBound = (decimal)order.Price * (1 + slippage); 

            bool shouldExecute = order.Description switch
            {
                "Initial Entry Order" =>
                    order.Side.ToLower() switch
                    {
                        "buy" => currentPrice <= upperBound,
                        "sell" => currentPrice >= lowerBound,
                        _ => false
                    },
                _ when order.Description.Contains("DCA Entry Order") =>
                    order.Side.ToLower() switch
                    {
                        "buy" => currentPrice <= upperBound,
                        "sell" => currentPrice >= lowerBound,
                        _ => false
                    },
                "Stoploss Order" =>
                    (order.Side == "sell" && currentPrice < (decimal)order.Price) ||
                    (order.Side == "buy" && currentPrice > (decimal)order.Price),
                _ when order.Description.Contains("Stoploss On Entry Order") =>
                    (order.Side == "sell" && currentPrice < (decimal)order.Price) ||
                    (order.Side == "buy" && currentPrice > (decimal)order.Price),
                _ =>
                    (order.Side == "sell" && currentPrice > (decimal)order.Price) ||
                    (order.Side == "buy" && currentPrice < (decimal)order.Price)
            };

            if (shouldExecute)
            {
                // Execute the order (this involves parallel exchange requests)
                await ExecuteOrderAsync(order, currentPrice, _context, userDataMap, ordersByKey);
            }
        }

        private async Task ExecuteOrderAsync(Order order, decimal currentPrice, AutoSignalsDbContext _context, Dictionary<string, UserData> userDataMap, Dictionary<(string, int, string), List<Order>> ordersByKey)
        {
            try
            {
                var relatedOrders = ordersByKey.TryGetValue((order.UserId, order.SignalId, order.Symbol), out var cachedOrders) ? cachedOrders : new List<Order>();
                var userData = userDataMap.GetValueOrDefault(order.UserId);

                // Handle exchange requests in parallel
                switch (order.Description)
                {
                    case "Initial Entry Order":
                    case var desc when desc.StartsWith("DCA"):
                        if (!order.IsTest)
                        {
                            var result = await HandleExchangeEntryOrderAsync(order, userData);
                            if (result == null || !result.Success)
                            {
                                // If insufficient funds, close related open orders
                                if (result != null && result.ErrorCode == "40762")
                                {
                                    await CloseRelatedOpenOrdersDueToInsufficientBalanceAsync(order, _context, result.ErrorMessage);
                                }
                                // Do not create or update the position if the order failed for any reason
                                return;
                            }
                            // Only create/update position if the order was successful
                            await CreateOrUpdatePositionAsync(order, currentPrice, relatedOrders, _context);
                        }
                        else
                        {
                            // For test orders, always create/update the position
                            await CreateOrUpdatePositionAsync(order, currentPrice, relatedOrders, _context);
                        }
                        break;
                    case "Stoploss Order":
                    case "Stoploss On Entry Order":
                    case "Moonbag Order":
                        if (!order.IsTest)
                        {
                            await HandleExchangeStoplossOrderAsync(order, userData);
                        }
                        await CloseOrdersAndPositionAsync(relatedOrders, order, currentPrice);
                        break;

                    case var desc when desc.Contains("MSL"):
                        if (!order.IsTest)
                        {
                            await HandleExchangeTakeProfitOrderAsync(order, userData);
                        }
                        await HandleMSLAsync(order, currentPrice, _context);
                        await UpdatePositionForTPAsync(order, currentPrice, _context);
                        break;

                    case var desc when desc.Contains("Take Profit Order"):
                        if (!order.IsTest)
                        {
                            await HandleExchangeTakeProfitOrderAsync(order, userData);
                        }
                        await UpdatePositionForTPAsync(order, currentPrice, _context);
                        break;
                }

                order.Status = "EXECUTED";
                order.Time = DateTime.UtcNow;

                // Update the order
                _context.Orders.Update(order);

                // Telegram notification (best effort, don't block order execution if it fails)
                try
                {
                    using var notifyScope = _scopeFactory.CreateScope();
                    var telegramNotifier = notifyScope.ServiceProvider.GetRequiredService<ITelegramNotifier>();
                    await telegramNotifier.NotifyUserAsync(order.UserId, order);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to send Telegram notification for executed order {OrderId}.", order.Id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to execute order {order.Id}. Exception: {ex.Message}");
               
                // Fix for CS1503: Argument 2: cannot convert from 'System.Exception' to 'string?'
                // The issue is that the second argument of LogErrorAsync expects a nullable string (stackTrace), but `ex.InnerException` is being passed, which is of type Exception.
                // To fix this, we should pass `ex.InnerException?.StackTrace` instead, which extracts the stack trace as a string.

                await _errorLogService.LogErrorAsync(
                    $"Failed to execute order {order.Id}.",
                    ex.InnerException?.StackTrace,
                    "UserOrderWatchDogService.ExecuteOrderAsync",
                    $"Order ID: {order.Id}, Symbol: {order.Symbol}");
            }
        }

        private async Task<TResult> ExecuteWithExchangeAdapterAsync<TResult>(
            string? exchangeIdOrName,
            UserData? userData,
            Func<IExchangeOrderAdapter, ExchangeCredentials, Task<TResult>> operation,
            CancellationToken cancellationToken = default)
        {
            if (userData == null)
            {
                throw new InvalidOperationException("User data not found for exchange execution.");
            }

            var credentials = CreateExchangeCredentials(userData);

            using var scope = _scopeFactory.CreateScope();
            var adapterFactory = scope.ServiceProvider.GetRequiredService<ExchangeOrderAdapterFactory>();
            var adapter = await adapterFactory.GetRequiredAdapterAsync(exchangeIdOrName ?? userData.ExchangeId?.ToString(), cancellationToken);
            return await operation(adapter, credentials);
        }

        private ExchangeCredentials CreateExchangeCredentials(UserData userData)
        {
            var apiKey = _encryptionService.Decrypt(userData.ApiKey);
            var apiSecret = _encryptionService.Decrypt(userData.ApiSecret);
            var apiPassword = _encryptionService.Decrypt(userData.ApiPassword);

            if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(apiSecret))
            {
                throw new InvalidOperationException($"API credentials are missing for user {userData.Id}.");
            }

            return new ExchangeCredentials(apiKey, apiSecret, apiPassword);
        }

        private static void ApplyExchangeOrderResult(Order order, ExchangeOrderResult? result)
        {
            if (result == null)
            {
                return;
            }

            order.ExternalOrderId ??= result.ExternalOrderId;
            order.ClientOrderId ??= result.ClientOrderId;
            order.ExchangeOrderStatus = result.Status ?? result.ErrorCode;
            order.ExchangeResponseJson = SerializeExchangeResponse(result.Response, result.ErrorMessage);
            order.LastSyncTime = DateTime.UtcNow;
        }

        private static string? SerializeExchangeResponse(object? response, string? fallback)
        {
            if (response == null)
            {
                return fallback;
            }

            try
            {
                return JsonSerializer.Serialize(response);
            }
            catch
            {
                return response.ToString() ?? fallback;
            }
        }


        private async Task<ExchangeOrderResult?> HandleExchangeEntryOrderAsync(Order order, UserData? userData)
        {
            var result = await ExecuteWithExchangeAdapterAsync(
                order.ExchangeId,
                userData,
                (adapter, credentials) => adapter.SendEntryOrderAsync(order, credentials));

            ApplyExchangeOrderResult(order, result);

            if (!result.Success && (result.ErrorCode == "45110" || result.ErrorCode == "40762"))
            {
                await CloseOrderDueToMinSizeAsync(order, result.ErrorMessage);
            }

            return result;
        }

        private async Task HandleExchangeTakeProfitOrderAsync(Order order, UserData? userData)
        {
            var result = await ExecuteWithExchangeAdapterAsync(
                order.ExchangeId,
                userData,
                (adapter, credentials) => adapter.SendTakeProfitOrderAsync(order, credentials));

            ApplyExchangeOrderResult(order, result);
        }

        public async Task HandleExchangeStoplossOrderAsync(Order order, UserData? userData)
        {
            if(order.IsTest)
            {
                return; // Skip sending stoploss orders for test orders
            }

            var result = await ExecuteWithExchangeAdapterAsync(
                order.ExchangeId,
                userData,
                (adapter, credentials) => adapter.SendStoplossOrderAsync(order, credentials));

            ApplyExchangeOrderResult(order, result);
        }

        private async Task<Dictionary<string, decimal>> FetchLatestPricesAsync(List<string> symbols)
        {
            var latestPrices = new Dictionary<string, decimal>();
            var uniqueSymbols = symbols.Distinct().ToList();

            if (!uniqueSymbols.Any())
                return latestPrices;

            // Get user data once
            List<UserData> userData;
            using (var scope = _scopeFactory.CreateScope())
            {
                var _context = scope.ServiceProvider.GetRequiredService<AutoSignalsDbContext>();
                userData = await _context.UsersData
                    .Where(u => u.ApiTestResult == "1" && u.ExchangeId.HasValue)
                    .ToListAsync();
            }

            if (!userData.Any())
            {
                _logger.LogWarning("No active users found with valid API credentials");
                return latestPrices;
            }

            // Group symbols by exchange for better efficiency
            var semaphore = new SemaphoreSlim(2); // Reduce to 2 concurrent requests to avoid rate limiting
            var fetchTasks = new List<Task>();
            var failedSymbols = new ConcurrentBag<string>();

            foreach (var symbol in uniqueSymbols)
            {
                fetchTasks.Add(Task.Run(async () =>
                {
                    await semaphore.WaitAsync();
                    try
                    {
                        decimal? price = null;
                        var attempts = 0;
                        const int maxAttempts = 2; // Try each exchange at most twice

                        // Try each user/exchange until we get a price
                        foreach (var user in userData)
                        {
                            if (price.HasValue) break;

                            for (int retry = 0; retry < maxAttempts; retry++)
                            {
                                try
                                {
                                    using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
                                    {
                                        var fetchTask = FetchExchangePriceAsync(user, symbol, cts.Token);

                                        var completedTask = await Task.WhenAny(fetchTask, Task.Delay(3000, cts.Token));
                                        if (completedTask == fetchTask)
                                        {
                                            price = await fetchTask;
                                            if (price.HasValue)
                                            {
                                                _logger.LogDebug($"Successfully fetched price for {symbol} from Exchange {user.ExchangeId} on attempt {retry + 1}");        
                                                                                                    break;
                                            }
                                        }
                                        else
                                        {
                                            _logger.LogWarning($"Timeout fetching {symbol} from Exchange {user.ExchangeId}");
                                        }
                                    }
                                }
                                catch (Exception ex) when (ex.Message.Contains("Too many requests"))
                                {
                                    _logger.LogWarning($"Rate limit hit for {symbol} on Exchange {user.ExchangeId}, retry {retry + 1}");
                                        

                                    if (retry < maxAttempts - 1)
                                    {
                                        // Exponential backoff
                                        await Task.Delay(2000 * (retry + 1));
                                    }
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogDebug(ex, $"Failed to fetch {symbol} from Exchange {user.ExchangeId} on attempt {retry + 1}");

                                    if (retry < maxAttempts - 1)
                                    {
                                        await Task.Delay(1000);
                                    }
                                }
                            }

                            // Small delay between exchange attempts
                            if (!price.HasValue && user != userData.Last())
                            {
                                await Task.Delay(500);
                            }
                        }

                        if (price.HasValue)
                        {
                            lock (latestPrices)
                            {
                                latestPrices[symbol] = price.Value;
                            }
                        }
                        else
                        {
                            failedSymbols.Add(symbol);

                            // Try to get from database as fallback
                            try
                            {
                                using (var scope = _scopeFactory.CreateScope())
                                {
                                    var _context = scope.ServiceProvider.GetRequiredService<AutoSignalsDbContext>();
                                    var dbPrice = await _context.GeneralAssetPrices
                                        .AsNoTracking()
                                        .FirstOrDefaultAsync(p => p.Symbol == symbol);

                                    if (dbPrice != null)
                                    {
                                        lock (latestPrices)
                                        {
                                            latestPrices[symbol] = dbPrice.Price;
                                        }
                                        _logger.LogInformation($"Using database fallback price for {symbol}: {dbPrice.Price}");
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, $"Failed to get fallback price from database for {symbol}");
                            }
                        }
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }));
            }

            await Task.WhenAll(fetchTasks);

            // Log failed symbols
            if (!failedSymbols.IsEmpty)
            {
                var failedList = string.Join(", ", failedSymbols);
                _logger.LogWarning($"Failed to fetch prices for symbols: {failedList}");

                // Log to error service (but not for every failure to avoid spam)
                if (failedSymbols.Count > 0)
                {
                    using (var scope = _scopeFactory.CreateScope())
                    {
                        var errorLogService = scope.ServiceProvider.GetRequiredService<ErrorLogService>();
                        await errorLogService.LogErrorAsync(
                            $"Failed to fetch prices for {failedSymbols.Count} symbols: {failedList}",
                            null,
                            "UserOrderWatchDogService.FetchLatestPricesAsync",
                            $"Symbols: {failedList}");
                    }
                }
            }

            return latestPrices;
        }

        private async Task<decimal?> FetchExchangePriceAsync(UserData user, string symbol, CancellationToken cancellationToken)
        {
            try
            {
                return await ExecuteWithExchangeAdapterAsync(
                    user.ExchangeId?.ToString(),
                    user,
                    (adapter, credentials) => adapter.FetchPriceAsync(symbol, credentials, cancellationToken),
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error in FetchExchangePriceAsync for {Symbol} on exchange {ExchangeId}", symbol, user.ExchangeId);
                return null;
            }
        }


        private double CalculateEstimatedLiquidation(double entryPrice, int leverage, string side)
        {
            if (leverage <= 0)
            {
                throw new ArgumentException("Leverage must be greater than zero.");
            }

            return side.ToLower() switch
            {
                "buy" => Math.Round(entryPrice * (1 - (1.0 / leverage)), 8), // Long position
                "sell" => Math.Round(entryPrice * (1 + (1.0 / leverage)), 8), // Short position
                _ => throw new ArgumentException("Invalid position side. Must be 'buy' or 'sell'.")
            };
        }

        private async Task CloseOrderDueToMinSizeAsync(Order order, string? errorMessage)
        {
            using (var scope = _scopeFactory.CreateScope())
            {
                var _context = scope.ServiceProvider.GetRequiredService<AutoSignalsDbContext>();

                order.Status = "CANCELLED";
                order.CloseTime = DateTime.UtcNow;
                _context.Orders.Update(order);

                await _errorLogService.LogErrorAsync(
                    $"Order {order.Id} cancelled due to minimum size error: {errorMessage}",
                    null, "UserOrderWatchDogService.CloseOrderDueToMinSizeAsync", $"Order ID: {order.Id}, Symbol: {order.Symbol}");

                await _context.SaveChangesAsync();
            }
        }

        private async Task CreateOrUpdatePositionAsync(Order order, decimal currentPrice, List<Order> relatedOrders, AutoSignalsDbContext _context)
        {
            const int maxRetryAttempts = 3; // Maximum number of retry attempts
            int retryCount = 0;
            bool success = false;

            var startTime = DateTime.UtcNow; // Start the timer

            while (!success && retryCount < maxRetryAttempts)
            {
                try
                {
                    _logger.LogInformation("Attempting to create or update position for Order ID: {OrderId}, Attempt: {Attempt}", order.Id, retryCount + 1);

                    // Retrieve the existing position
                    var existingPosition = await _context.Positions
                        .FirstOrDefaultAsync(p => p.UserId == order.UserId && p.Symbol == order.Symbol && p.Side == order.Side && p.Status == "OPEN");


                    if (existingPosition != null)
                    {
                        // Update the existing position
                        double currentSize = existingPosition.Size;
                        double newSize = currentSize + order.Size;
                        existingPosition.Size = newSize;

                        // Recalculate the average entry price
                        double totalCost = (currentSize * existingPosition.Entry) + (order.Size * (double)currentPrice);
                        existingPosition.Entry = Math.Round(totalCost / newSize, 8);

                        // Update the stoploss if provided
                        if (order.Stoploss.HasValue)
                        {
                            existingPosition.Stoploss = order.Stoploss.Value;
                        }

                        // Calculate and update the Estimated Liquidation Price
                        existingPosition.EstLiquidation = CalculateEstimatedLiquidation(existingPosition.Entry, existingPosition.Leverage, existingPosition.Side);

                        // Mark the position as modified
                        _context.Positions.Update(existingPosition);
                    }
                    else
                    {
                        // Create a new position
                        var position = new Position
                        {
                            UserId = order.UserId,
                            ExchangeId = order.ExchangeId,
                            TelegramId = order.TelegramId ?? "No ID",
                            Side = order.Side,
                            Size = order.Size,
                            Leverage = (int)order.Leverage,
                            Symbol = order.Symbol,
                            Entry = (double)currentPrice,
                            Stoploss = order.Stoploss ?? 0,
                            IsIsolated = order.IsIsolated,
                            ROI = 0,
                            Status = "OPEN",
                            IsTest = order.IsTest,
                            Time = DateTime.UtcNow,
                            EstLiquidation = CalculateEstimatedLiquidation((double)currentPrice, (int)order.Leverage, order.Side)
                        };

                        _context.Positions.Add(position);
                        await _context.SaveChangesAsync();

                        order.PositionId = position.Id.ToString();
                    }

                    // Mark the triggering order as EXECUTED
                    order.Status = "EXECUTED";
                    _context.Orders.Update(order);

                    // Update related orders
                    foreach (var relatedOrder in relatedOrders)
                    {
                        if (relatedOrder.Status != "EXECUTED" && relatedOrder.Status != "CLOSED")
                        {
                            relatedOrder.Status = "OPEN";
                            relatedOrder.PositionId = existingPosition?.Id.ToString() ?? order.PositionId;

                            // Mark the related order as modified
                            _context.Orders.Update(relatedOrder);
                        }
                    }

                    // Ensure all stoploss orders in relatedOrders are linked to the position
                    var stoplossOrders = relatedOrders
                        .Where(o => o.Description == "Stoploss Order")
                        .ToList();

                    var positionIdToSet = existingPosition?.Id.ToString() ?? order.PositionId;

                    foreach (var stoplossOrder in stoplossOrders)
                    {
                        if (stoplossOrder.PositionId != positionIdToSet)
                        {
                            stoplossOrder.PositionId = positionIdToSet;
                            _context.Orders.Update(stoplossOrder);
                        }
                    }

                    // Save all changes atomically
                    await _context.SaveChangesAsync();

                    success = true; // Mark the operation as successful
                }
                catch (DbUpdateConcurrencyException ex)
                {
                    retryCount++;
                    _logger.LogWarning(ex, "Concurrency issue occurred while creating or updating position for Order ID: {OrderId}. Retrying... Attempt: {Attempt}", order.Id, retryCount);

                    if (retryCount >= maxRetryAttempts)
                    {
                        _logger.LogError(ex, "Max retry attempts reached for Order ID: {OrderId}. Operation failed.", order.Id);
                        await _errorLogService.LogErrorAsync(
                            $"Max retry attempts reached for Order ID: {order.Id}. Operation failed.",
                            ex.StackTrace, "UserOrderWatchDogService.CreateOrUpdatePositionAsync", $"Order ID: {order.Id}, Symbol: {order.Symbol}");

                        throw; // Re-throw the exception after max retries
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "An error occurred while creating or updating position for Order ID: {OrderId}. Exception: {Message}", order.Id, ex.Message);
                    await _errorLogService.LogErrorAsync(
                        $"An error occurred while creating or updating position for Order ID: {order.Id}.",
                        ex.StackTrace, "UserOrderWatchDogService.CreateOrUpdatePositionAsync", $"Order ID: {order.Id}, Symbol: {order.Symbol}");
                    throw; // Re-throw the exception for unexpected errors
                }
            }

            var endTime = DateTime.UtcNow; // End the timer
            _logger.LogInformation("CreateOrUpdatePositionAsync completed for Order ID: {OrderId} in {ExecutionTime} ms", order.Id, (endTime - startTime).TotalMilliseconds);
        }

        public async Task CloseOrdersAndPositionAsync(List<Order> relatedOrders, Order order, decimal currentPrice)
        {
            using (var scope = _scopeFactory.CreateScope())
            {
                var _context = scope.ServiceProvider.GetRequiredService<AutoSignalsDbContext>();
                const int maxRetryAttempts = 3; // Maximum number of retry attempts
                int retryCount = 0;
                bool success = false;

                var startTime = DateTime.UtcNow; // Start the timer

                while (!success && retryCount < maxRetryAttempts)
                {
                    try
                    {
                        _logger.LogInformation("Attempting to close orders and position for Order ID: {OrderId}, Attempt: {Attempt}", order.Id, retryCount + 1);

                        // Retrieve the position to close
                        var positionToClose = await _context.Positions
                            .FirstOrDefaultAsync(p => p.Id == int.Parse(order.PositionId));

                        if (positionToClose != null)
                        {
                            // Detach the entity if it's already being tracked
                            var trackedEntity = _context.ChangeTracker.Entries<Position>()
                                .FirstOrDefault(e => e.Entity.Id == positionToClose.Id);
                            if (trackedEntity != null)
                            {
                                _context.Entry(trackedEntity.Entity).State = EntityState.Detached;
                            }

                            // Calculate and update the Estimated Liquidation Price before closing
                            positionToClose.EstLiquidation = CalculateEstimatedLiquidation(positionToClose.Entry, positionToClose.Leverage, positionToClose.Side);

                            // Update the position's status and ROI
                            positionToClose.Status = "CLOSED";
                            positionToClose.CloseTime = DateTime.UtcNow;
                            positionToClose.ROI = CalculateUnrealizedROI(positionToClose, (double)currentPrice);

                            // Attach and mark the position as modified
                            _context.Attach(positionToClose);
                            _context.Entry(positionToClose).State = EntityState.Modified;
                        }
                        else
                        {
                            _logger.LogWarning("Position with ID {PositionId} not found for closing. Order ID: {OrderId}", order.PositionId, order.Id);
                            await _errorLogService.LogErrorAsync(
                                $"Position with ID {order.PositionId} not found for closing. Order ID: {order.Id}.",
                                null, "UserOrderWatchDogService.CloseOrdersAndPositionAsync", $"Order ID: {order.Id}, Position ID: {order.PositionId}");

                        }

                        // Mark the triggering order as EXECUTED
                        order.Status = "EXECUTED";
                        order.CloseTime = DateTime.UtcNow;
                        _context.Orders.Update(order);

                        // Update related orders
                        foreach (var relatedOrder in relatedOrders)
                        {
                            try
                            {
                                if (relatedOrder.Status == "OPEN")
                                {
                                    relatedOrder.Status = "CLOSED";

                                    // Detach the entity if it's already being tracked
                                    var trackedOrder = _context.ChangeTracker.Entries<Order>()
                                        .FirstOrDefault(e => e.Entity.Id == relatedOrder.Id);
                                    if (trackedOrder != null)
                                    {
                                        _context.Entry(trackedOrder.Entity).State = EntityState.Detached;
                                    }

                                    // Attach and mark the related order as modified
                                    _context.Attach(relatedOrder);
                                    _context.Entry(relatedOrder).State = EntityState.Modified;
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "An error occurred while processing related order {RelatedOrderId} for Order {OrderId}. Exception: {Message}", relatedOrder.Id, order.Id, ex.Message);
                                await _errorLogService.LogErrorAsync(
                                    $"An error occurred while processing related order {relatedOrder.Id} for Order {order.Id}.",
                                    ex.StackTrace, "UserOrderWatchDogService.CloseOrdersAndPositionAsync", $"Order ID: {order.Id}, Related Order ID: {relatedOrder.Id}");
                            }
                        }

                        try
                        {
                            await _context.SaveChangesAsync();
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error saving changes to the database.");
                            await _errorLogService.LogErrorAsync(
                                "Error saving changes to the database.",
                                ex.StackTrace, "UserOrderWatchDogService.CloseOrdersAndPositionAsync", $"Order ID: {order.Id}, Symbol: {order.Symbol}");
                            throw;
                        }

                        success = true; // Mark the operation as successful
                    }
                    catch (DbUpdateConcurrencyException ex)
                    {
                        retryCount++;
                        _logger.LogWarning(ex, "Concurrency issue occurred while closing orders and position for Order ID: {OrderId}. Retrying... Attempt: {Attempt}", order.Id, retryCount);

                        if (retryCount >= maxRetryAttempts)
                        {
                            _logger.LogError(ex, "Max retry attempts reached for Order ID: {OrderId}. Operation failed.", order.Id);
                            await _errorLogService.LogErrorAsync(
                                $"Max retry attempts reached for Order ID: {order.Id}. Operation failed.",
                                ex.StackTrace, "UserOrderWatchDogService.CloseOrdersAndPositionAsync", $"Order ID: {order.Id}, Position ID: {order.PositionId}");

                            throw; // Re-throw the exception after max retries
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "An error occurred while closing orders and position for Order ID: {OrderId}. Exception: {Message}", order.Id, ex.Message);
                        await _errorLogService.LogErrorAsync(
                            $"An error occurred while closing orders and position for Order ID: {order.Id}.",
                            ex.StackTrace, "UserOrderWatchDogService.CloseOrdersAndPositionAsync", $"Order ID: {order.Id}, Position ID: {order.PositionId}");
                        throw; // Re-throw the exception for unexpected errors
                    }
                }
                var endTime = DateTime.UtcNow; // End the timer
                _logger.LogInformation("CloseOrdersAndPositionAsync completed for Order ID: {OrderId} in {ExecutionTime} ms", order.Id, (endTime - startTime).TotalMilliseconds);
            }

        }

        public async Task CloseOrdersAndPositionAsync(int positionId, decimal currentPrice)
        {
            using (var scope = _scopeFactory.CreateScope())
            {
                var _context = scope.ServiceProvider.GetRequiredService<AutoSignalsDbContext>();

                // Fetch the position
                var position = await _context.Positions.FirstOrDefaultAsync(p => p.Id == positionId);
                if (position == null)
                {
                    _logger.LogWarning("Position with ID {PositionId} not found for closing.", positionId);
                    return;
                }

                // Fetch related orders (PENDING or OPEN)
                var relatedOrders = await _context.Orders
                    .Where(o => o.PositionId == positionId.ToString() && (o.Status == "PENDING" || o.Status == "OPEN"))
                    .ToListAsync();

                // Fetch the stoploss order
                var stoplossOrder = await _context.Orders
                    .FirstOrDefaultAsync(o =>
                        o.Description != null &&
                        o.Description.Contains("Stoploss") &&
                        o.Symbol == position.Symbol &&
                        o.UserId == position.UserId &&
                        (o.Status == "OPEN" || o.Status == "CLOSED" || o.Status == "EXECUTED"));

                // If the stoploss order exists and its PositionId is missing, set it
                if (stoplossOrder != null && string.IsNullOrEmpty(stoplossOrder.PositionId))
                {
                    var positionIdFromRelated = relatedOrders.FirstOrDefault(ro => !string.IsNullOrEmpty(ro.PositionId))?.PositionId
                        ?? position.Id.ToString();

                    stoplossOrder.PositionId = positionIdFromRelated;
                    _context.Orders.Update(stoplossOrder);
                    await _context.SaveChangesAsync();
                }

                // Now close the position and all related orders
                const int maxRetryAttempts = 3;
                int retryCount = 0;
                bool success = false;
                var startTime = DateTime.UtcNow;

                while (!success && retryCount < maxRetryAttempts)
                {
                    try
                    {
                        _logger.LogInformation("Attempting to close orders and position for Position ID: {PositionId}, Attempt: {Attempt}", position.Id, retryCount + 1);

                        // Update the position's status and ROI
                        position.Status = "CLOSED";
                        position.CloseTime = DateTime.UtcNow;
                        position.ROI = CalculateUnrealizedROI(position, (double)currentPrice);

                        _context.Positions.Update(position);

                        // Mark the stoploss order as EXECUTED
                        if (stoplossOrder != null)
                        {
                            stoplossOrder.Status = "EXECUTED";
                            stoplossOrder.CloseTime = DateTime.UtcNow;
                            _context.Orders.Update(stoplossOrder);
                        }

                        // Update related orders
                        foreach (var relatedOrder in relatedOrders)
                        {
                            if (relatedOrder.Status == "OPEN")
                            {
                                relatedOrder.Status = "CLOSED";
                                relatedOrder.CloseTime = DateTime.UtcNow;
                                _context.Orders.Update(relatedOrder);
                            }
                        }

                        try
                        {
                            await _context.SaveChangesAsync();
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error saving changes to the database.");
                            await _errorLogService.LogErrorAsync(
                                "Error saving changes to the database.",
                                ex.StackTrace, "UserOrderWatchDogService.CloseOrdersAndPositionAsync", $"Position ID: {position.Id}, Symbol: {position.Symbol}");
                            throw;
                        }

                        success = true;
                    }
                    catch (DbUpdateConcurrencyException ex)
                    {
                        retryCount++;
                        _logger.LogWarning(ex, "Concurrency issue occurred while closing orders and position for Position ID: {PositionId}. Retrying... Attempt: {Attempt}", position.Id, retryCount);

                        if (retryCount >= maxRetryAttempts)
                        {
                            _logger.LogError(ex, "Max retry attempts reached for Position ID: {PositionId}. Operation failed.", position.Id);
                            await _errorLogService.LogErrorAsync(
                                $"Max retry attempts reached for Position ID: {position.Id}. Operation failed.",
                                ex.StackTrace, "UserOrderWatchDogService.CloseOrdersAndPositionAsync", $"Position ID: {position.Id}");
                            throw;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "An error occurred while closing orders and position for Position ID: {PositionId}. Exception: {Message}", position.Id, ex.Message);
                        await _errorLogService.LogErrorAsync(
                            $"An error occurred while closing orders and position for Position ID: {position.Id}.",
                            ex.StackTrace, "UserOrderWatchDogService.CloseOrdersAndPositionAsync", $"Position ID: {position.Id}");
                        throw;
                    }
                }

                var endTime = DateTime.UtcNow;
                _logger.LogInformation("CloseOrdersAndPositionAsync completed for Position ID: {PositionId} in {ExecutionTime} ms", position.Id, (endTime - startTime).TotalMilliseconds);
            }
        }


        private async Task UpdatePositionForTPAsync(Order order, decimal currentPrice, AutoSignalsDbContext _context)
        {
            const int maxRetryAttempts = 3; // Maximum number of retry attempts
            int retryCount = 0;
            bool success = false;

            var startTime = DateTime.UtcNow; // Start the timer

            while (!success && retryCount < maxRetryAttempts)
            {
                try
                {
                    _logger.LogInformation("Attempting to update position for take profit for Order ID: {OrderId}, Attempt: {Attempt}", order.Id, retryCount + 1);

                    var existingPosition = await _context.Positions
                        .FirstOrDefaultAsync(p => p.Id == int.Parse(order.PositionId));

                    if (existingPosition != null)
                    {
                        // Calculate ROI using the singular executed order
                        existingPosition.ROI = CalculateUnrealizedROI(existingPosition, (double)currentPrice);

                        // Calculate the new position size after closing the specified percentage
                        double currentSize = existingPosition.Size;
                        double tpPercentage = order.Size;
                        double actualClosedSize = Math.Round(currentSize * (tpPercentage / 100), 8);
                        double newSize = Math.Round(currentSize - actualClosedSize, 8);
                        if (newSize <= 0)
                        {
                            newSize = 0;
                            existingPosition.Status = "CLOSED";
                            existingPosition.CloseTime = DateTime.UtcNow;

                        }

                        // Update the position size
                        existingPosition.Size = newSize;

                        // Recalculate and update the Estimated Liquidation Price
                        existingPosition.EstLiquidation = CalculateEstimatedLiquidation(existingPosition.Entry, existingPosition.Leverage, existingPosition.Side);

                        // Mark the triggering order as EXECUTED
                        order.Status = "EXECUTED";
                        order.CloseTime = DateTime.UtcNow;

                        // Mark the position as modified
                        _context.Positions.Update(existingPosition);
                        _context.Orders.Update(order);

                        // Save changes
                        try
                        {
                            await _context.SaveChangesAsync();
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "An error occurred while saving changes in UpdatePositionForTPAsync for Order ID: {OrderId}. Exception: {Message}", order.Id, ex.Message);
                            await _errorLogService.LogErrorAsync(
                                $"An error occurred while saving changes in UpdatePositionForTPAsync for Order ID: {order.Id}.",
                                ex.StackTrace, "UserOrderWatchDogService.UpdatePositionForTPAsync", $"Order ID: {order.Id}, Position ID: {order.PositionId}");
                            //throw; // Optionally rethrow if you want the error to bubble up
                        }
                    }
                    else
                    {
                        _logger.LogWarning("Position with ID {PositionId} not found for take profit update. Order ID: {OrderId}", order.PositionId, order.Id);
                        await _errorLogService.LogErrorAsync(
                            $"Position with ID {order.PositionId} not found for take profit update. Order ID: {order.Id}.",
                            null, "UserOrderWatchDogService.UpdatePositionForTPAsync", $"Order ID: {order.Id}, Position ID: {order.PositionId}");
                    }

                    success = true; // Mark the operation as successful
                }
                catch (DbUpdateConcurrencyException ex)
                {
                    retryCount++;
                    _logger.LogWarning(ex, "Concurrency issue occurred while updating position for take profit for Order ID: {OrderId}. Retrying... Attempt: {Attempt}", order.Id, retryCount);

                    if (retryCount >= maxRetryAttempts)
                    {
                        _logger.LogError(ex, "Max retry attempts reached for Order ID: {OrderId}. Operation failed.", order.Id);
                        await _errorLogService.LogErrorAsync(
                            $"Max retry attempts reached for Order ID: {order.Id}. Operation failed.",
                            ex.StackTrace, "UserOrderWatchDogService.UpdatePositionForTPAsync", $"Order ID: {order.Id}, Position ID: {order.PositionId}");
                        throw; // Re-throw the exception after max retries
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "An error occurred while updating position for take profit for Order ID: {OrderId}. Exception: {Message}", order.Id, ex.Message);
                    await _errorLogService.LogErrorAsync(
                        $"An error occurred while updating position for take profit for Order ID: {order.Id}.",
                        ex.StackTrace, "UserOrderWatchDogService.UpdatePositionForTPAsync", $"Order ID: {order.Id}, Position ID: {order.PositionId}");
                    throw; // Re-throw the exception for unexpected errors
                }
            }

            var endTime = DateTime.UtcNow; // End the timer
            _logger.LogInformation("UpdatePositionForTPAsync completed for Order ID: {OrderId} in {ExecutionTime} ms", order.Id, (endTime - startTime).TotalMilliseconds);
        }

        private async Task HandleMSLAsync(Order order, decimal currentPrice, AutoSignalsDbContext _context)
        {
            const int maxRetryAttempts = 3; // Maximum number of retry attempts
            int retryCount = 0;
            bool success = false;

            var startTime = DateTime.UtcNow; // Start the timer

            while (!success && retryCount < maxRetryAttempts)
            {
                try
                {
                    _logger.LogInformation("Attempting to handle MSL for Order ID: {OrderId}, Attempt: {Attempt}", order.Id, retryCount + 1);

                    var stoplossOrders = await _context.Orders
                        .Where(o => o.Symbol == order.Symbol && o.SignalId == order.SignalId && o.UserId == order.UserId && o.Description == "Stoploss Order")
                        .ToListAsync();

                    var initialEntryOrder = await _context.Orders
                        .Where(o => o.Symbol == order.Symbol && o.SignalId == order.SignalId && o.UserId == order.UserId && o.Description == "Initial Entry Order")
                        .FirstOrDefaultAsync();

                    if (initialEntryOrder != null)
                    {
                        foreach (var stoplossOrder in stoplossOrders)
                        {
                            // Ensure stoploss price is not set to the initial entry price
                            if (stoplossOrder.Price != initialEntryOrder.Price)
                            {
                                stoplossOrder.Price = initialEntryOrder.Price;
                                stoplossOrder.Description = "Stoploss On Entry Order";

                                // Mark the stoploss order as modified
                                _context.Orders.Update(stoplossOrder);
                            }
                        }
                    }

                    var dcaEntries = await _context.Orders
                        .Where(o => o.Symbol == order.Symbol && o.SignalId == order.SignalId && o.UserId == order.UserId && o.Description.Contains("DCA") && o.Status == "OPEN")
                        .ToListAsync();

                    foreach (var dcaEntry in dcaEntries)
                    {
                        dcaEntry.Status = "CLOSED";
                        dcaEntry.CloseTime = DateTime.UtcNow;

                        // Mark the DCA entry as modified
                        _context.Orders.Update(dcaEntry);
                    }

                    // Save changes
                    await _context.SaveChangesAsync();

                    success = true; // Mark the operation as successful
                }
                catch (DbUpdateConcurrencyException ex)
                {
                    retryCount++;
                    _logger.LogWarning(ex, "Concurrency issue occurred while handling MSL for Order ID: {OrderId}. Retrying... Attempt: {Attempt}", order.Id, retryCount);

                    if (retryCount >= maxRetryAttempts)
                    {
                        _logger.LogError(ex, "Max retry attempts reached for Order ID: {OrderId}. Operation failed.", order.Id);
                        await _errorLogService.LogErrorAsync(
                            $"Max retry attempts reached for Order ID: {order.Id}. Operation failed.",
                            ex.StackTrace, "UserOrderWatchDogService.HandleMSLAsync", $"Order ID: {order.Id}, Symbol: {order.Symbol}");
                        throw; // Re-throw the exception after max retries
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "An error occurred while handling MSL for Order ID: {OrderId}. Exception: {Message}", order.Id, ex.Message);
                    await _errorLogService.LogErrorAsync(
                        $"An error occurred while handling MSL for Order ID: {order.Id}.",
                        ex.StackTrace, "UserOrderWatchDogService.HandleMSLAsync", $"Order ID: {order.Id}, Symbol: {order.Symbol}");
                    throw; // Re-throw the exception for unexpected errors
                }
            }

            var endTime = DateTime.UtcNow; // End the timer
            _logger.LogInformation("HandleMSLAsync completed for Order ID: {OrderId} in {ExecutionTime} ms", order.Id, (endTime - startTime).TotalMilliseconds);
            //await _telegramBotService.LoggError($"HandleMSLAsync completed for Order ID: {order.Id} in {(endTime - startTime).TotalMilliseconds} ms");
        }

        private async Task HandleOpenPositionsAsync(Dictionary<string, decimal> priceData, AutoSignalsDbContext _context)
        {
            // Fetch all open positions
            var openPositions = await _context.Positions
                .Where(p => p.Status == "OPEN")
                .ToListAsync();

            foreach (var position in openPositions)
            {
                // Check if the current price data contains the symbol for the position
                if (!priceData.TryGetValue(position.Symbol, out var currentPrice))
                {
                    _logger.LogWarning($"Price data for symbol {position.Symbol} not found. Skipping position ID: {position.Id}");
                    continue;
                }

                // Update unrealized ROI
                position.ROI = CalculateUnrealizedROI(position, (double)currentPrice);

                // Check if the position should be closed due to liquidation
                bool shouldClose = position.Side.ToLower() switch
                {
                    "buy" => currentPrice <= (decimal)position.EstLiquidation, // Long position
                    "sell" => currentPrice >= (decimal)position.EstLiquidation, // Short position
                    _ => false
                };

                if (shouldClose && position.IsIsolated)
                {
                    _logger.LogInformation($"Closing position ID: {position.Id} due to price surpassing EstLiquidation. Current Price: {currentPrice}, EstLiquidation: {position.EstLiquidation}");

                    // Close the position
                    position.Status = "CLOSED";
                    position.ClosePrice = (double)currentPrice;
                    position.CloseTime = DateTime.UtcNow;

                    // Optionally, log the closure
                    await _errorLogService.LogErrorAsync(
                        $"Position ID: {position.Id} closed due to price surpassing EstLiquidation.",
                        null, "UserOrderWatchDogService.HandleOpenPositionsAsync", $"Position ID: {position.Id}, Symbol: {position.Symbol}");
                }

                // Mark the position as modified
                _context.Positions.Update(position);
            }

            // Save all changes
            await _context.SaveChangesAsync();
        }

        private double CalculateUnrealizedROI(Position position, double currentPrice)
        {
            double entryPrice = position.Entry;
            double leverage = position.Leverage;

            // Determine the price change percentage based on the position's side
            double priceChangePercentage = position.Side == "buy"
                ? ((currentPrice - entryPrice) / entryPrice) * 100 // For buy positions
                : ((entryPrice - currentPrice) / entryPrice) * 100; // For sell positions

            // Apply leverage to the percentage change
            double unrealizedROI = priceChangePercentage * leverage;

            return Math.Round(unrealizedROI, 2);
        }

        private async Task CancelOrderAsync(Order order, AutoSignalsDbContext _context)
        {
            var relatedOrders = await _context.Orders
                .Where(o => o.Symbol == order.Symbol && o.SignalId == order.SignalId && o.UserId == order.UserId && o.Status == "PENDING")
                .ToListAsync();

            order.Status = "CANCELLED";
            order.CloseTime = DateTime.UtcNow;
            foreach (var relatedOrder in relatedOrders)
            {
                relatedOrder.Status = "CANCELLED";
            }

            _logger.LogInformation($"Order {order.Id} and related orders cancelled due to being older than 24 hours.");
            await _context.SaveChangesAsync();
        }



        private async Task CloseRelatedOpenOrdersDueToInsufficientBalanceAsync(Order failedOrder, AutoSignalsDbContext context, string? errorMessage)
        {
            var relatedOrders = await context.Orders
                .Where(o => o.UserId == failedOrder.UserId
                    && o.SignalId == failedOrder.SignalId
                    && o.Symbol == failedOrder.Symbol
                    && o.Status == "OPEN")
                .ToListAsync();

            foreach (var order in relatedOrders)
            {
                order.Status = "CANCELLED";
                order.CloseTime = DateTime.UtcNow;
                context.Orders.Update(order);
            }
            await _errorLogService.LogErrorAsync(
                $"Related open orders for Order ID: {failedOrder.Id} cancelled due to insufficient balance: {errorMessage}",
                null, "UserOrderWatchDogService.CloseRelatedOpenOrdersDueToInsufficientBalanceAsync", $"Order ID: {failedOrder.Id}, Symbol: {failedOrder.Symbol}");

            await context.SaveChangesAsync();
        }
    }
}
