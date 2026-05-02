using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AutoSignals.Services
{
    public class ExchangeHostedService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<ExchangeHostedService> _logger;

        public ExchangeHostedService(IServiceProvider serviceProvider, ILogger<ExchangeHostedService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var jobs = new[]
            {
                RunPeriodicJobAsync("FetchMarkets", TimeSpan.FromHours(12), FetchMarketsAsync, stoppingToken),
                //RunPeriodicJobAsync("FetchPrices", TimeSpan.FromMinutes(15), FetchPricesAsync, stoppingToken),
                //RunPeriodicJobAsync("CalculateAveragePrices", TimeSpan.FromMinutes(5), CalculateAveragePricesAsync, stoppingToken),
                //RunPeriodicJobAsync("SignalPerformance", TimeSpan.FromMinutes(3), SignalPerformanceAsync, stoppingToken),
                //RunPeriodicJobAsync("UserOrderWatchDog", TimeSpan.FromMinutes(1), UserOrderWatchDogAsync, stoppingToken),
                //RunPeriodicJobAsync("RunSignalProviderService", TimeSpan.FromHours(1), RunSignalProviderServiceAsync, stoppingToken),
                //RunPeriodicJobAsync("CreateDefaultProviderSettingsForUsers", TimeSpan.FromHours(8), CreateDefaultProviderSettingsForUsersAsync, stoppingToken)
            };

            return Task.WhenAll(jobs);
        }

        private async Task RunPeriodicJobAsync(
            string jobName,
            TimeSpan interval,
            Func<CancellationToken, Task> job,
            CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var startedAt = DateTime.UtcNow;

                try
                {
                    await job(cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unhandled exception while running background job {JobName}.", jobName);
                    await TryLogBackgroundFailureAsync(jobName, ex);
                }

                var elapsed = DateTime.UtcNow - startedAt;
                _logger.LogInformation("Background job {JobName} completed in {Elapsed}.", jobName, elapsed);

                try
                {
                    await Task.Delay(interval, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }

        private async Task TryLogBackgroundFailureAsync(string jobName, Exception ex)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var errorLogService = scope.ServiceProvider.GetRequiredService<ErrorLogService>();
                await errorLogService.LogErrorAsync(
                    $"Unhandled exception while running {jobName}. {ex.Message}",
                    ex.StackTrace,
                    nameof(ExchangeHostedService),
                    ex.InnerException?.ToString());
            }
            catch (Exception loggingEx)
            {
                _logger.LogError(loggingEx, "Failed to persist background job failure for {JobName}.", jobName);
            }
        }

        private async Task FetchMarketsAsync(CancellationToken cancellationToken)
        {
            async Task FetchMarketData<TService>(Func<TService, Task> fetchFunc, string marketName) where TService : class
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    var service = scope.ServiceProvider.GetRequiredService<TService>();
                    await fetchFunc(service);
                    Console.WriteLine($"{marketName} markets fetched successfully.");
                }
                catch (Exception ex)
                {
                    using var errorScope = _serviceProvider.CreateScope();
                    var errorLogService = errorScope.ServiceProvider.GetRequiredService<ErrorLogService>();
                    await errorLogService.LogErrorAsync(
                        $"Failed to fetch {marketName} markets Error: {ex.Message}, and Inner: {ex.InnerException}",
                        ex.StackTrace,
                        nameof(FetchMarketsAsync),
                        $"Market: {marketName}");

                    Console.WriteLine($"Failed to fetch {marketName} markets: {ex.Message}");
                    Console.WriteLine($"Stack Trace: {ex.StackTrace}");
                }
            }

            await Task.WhenAll(
                FetchMarketData<IBitgetService>(s => s.GetBitgetMarketsAsync(), "Bitget"),
                FetchMarketData<IBinanceService>(s => s.GetBinanceMarketsAsync(), "Binance"),
                FetchMarketData<IBybitService>(s => s.GetBybitMarketsAsync(), "Bybit"),
                FetchMarketData<IOkxService>(s => s.GetOkxMarketsAsync(), "OKX"),
                FetchMarketData<IKuCoinService>(s => s.GetKuCoinMarketsAsync(), "KuCoin")
            );
        }

        private async Task FetchPricesAsync(CancellationToken cancellationToken)
        {
            var startTime = DateTime.Now;

            async Task FetchPriceData<TService>(Func<TService, Task> fetchFunc, string marketName) where TService : class
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    var service = scope.ServiceProvider.GetRequiredService<TService>();
                    await fetchFunc(service);
                    Console.WriteLine($"{marketName} asset prices fetched successfully.");
                }
                catch (Exception ex)
                {
                    using var errorScope = _serviceProvider.CreateScope();
                    var errorLogService = errorScope.ServiceProvider.GetRequiredService<ErrorLogService>();
                    await errorLogService.LogErrorAsync(
                        $"Failed to fetch {marketName} asset prices: {ex.Message}",
                        ex.StackTrace,
                        nameof(FetchPricesAsync),
                        $"Market: {marketName}");

                    Console.WriteLine($"Failed to fetch {marketName} asset prices: {ex.Message}");
                    Console.WriteLine($"Stack Trace: {ex.StackTrace}");
                }
            }

            await Task.WhenAll(
                FetchPriceData<IBitgetService>(s => s.FetchAllBitgetAssetPricesV2Async(), "Bitget"),
                FetchPriceData<IBinanceService>(s => s.FetchAllBinanceAssetPricesV2Async(), "Binance"),
                FetchPriceData<IBybitService>(s => s.FetchAllBybitAssetPricesV2Async(), "Bybit"),
                FetchPriceData<IOkxService>(s => s.FetchAllOkxAssetPricesV2Async(), "OKX"),
                FetchPriceData<IKuCoinService>(s => s.FetchAllKuCoinAssetPricesV2Async(), "KuCoin")
            );

            var endTime = DateTime.Now;
            Console.WriteLine($"Elapsed time = {endTime - startTime}");
        }

        private async Task FetchWebSocketPricesAsync(CancellationToken cancellationToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var startTime = DateTime.Now;

            var bitgetService = scope.ServiceProvider.GetRequiredService<IBitgetService>();
            var binanceService = scope.ServiceProvider.GetRequiredService<IBinanceService>();
            var bybitService = scope.ServiceProvider.GetRequiredService<IBybitService>();
            var okxService = scope.ServiceProvider.GetRequiredService<IOkxService>();
            var kucoinService = scope.ServiceProvider.GetRequiredService<IKuCoinService>();

            async Task HandleWebSocket(Func<Task> wsFunc, Func<Task>? fallbackFunc, string marketName)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var s = DateTime.Now;
                    await wsFunc();
                    Console.WriteLine($"{marketName} WebSocket prices fetched successfully.");
                    var e = DateTime.Now;
                    Console.WriteLine($"Elips time =  {e - s}");
                }
                catch (Exception ex)
                {
                    using var errorScope = _serviceProvider.CreateScope();
                    var errorLogService = errorScope.ServiceProvider.GetRequiredService<ErrorLogService>();
                    await errorLogService.LogErrorAsync(
                        $"{marketName} WebSocket Failure: {ex.InnerException}",
                        ex.StackTrace,
                        nameof(FetchWebSocketPricesAsync),
                        $"Market: {marketName}");

                    Console.WriteLine($"Failed to fetch {marketName} WebSocket prices: {ex.Message}");
                    Console.WriteLine($"Stack Trace: {ex.StackTrace}");

                    if (fallbackFunc != null)
                    {
                        await fallbackFunc();
                    }
                }
            }

            await HandleWebSocket(
                () => bitgetService.GetTickerPricesViaWebSocketAsync(),
                () => bitgetService.FetchAllBitgetAssetPricesV2Async(),
                "Bitget");
            await HandleWebSocket(
                () => binanceService.GetTickerPricesViaWebSocketAsync(),
                () => binanceService.FetchAllBinanceAssetPricesV2Async(),
                "Binance");
            await HandleWebSocket(
                () => bybitService.GetTickerPricesViaWebSocketAsync(),
                () => bybitService.FetchAllBybitAssetPricesV2Async(),
                "Bybit");
            await HandleWebSocket(
                () => okxService.GetTickerPricesViaWebSocketAsync(),
                () => okxService.FetchAllOkxAssetPricesV2Async(),
                "OKX");
            await HandleWebSocket(
                () => kucoinService.GetTickerPricesViaWebSocketAsync(),
                () => kucoinService.FetchAllKuCoinAssetPricesV2Async(),
                "KuCoin");

            var endTime = DateTime.Now;
            Console.WriteLine($"Elips time =  {endTime - startTime}");
        }

        private async Task FetchBitgetWebSocketPricesAsync(CancellationToken cancellationToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var bitgetService = scope.ServiceProvider.GetRequiredService<IBitgetService>();

            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await bitgetService.GetTickerPricesViaWebSocketAsync();
                Console.WriteLine("Bitget WebSocket prices fetched successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to fetch Bitget WebSocket prices: {ex.Message}");
                Console.WriteLine($"Stack Trace: {ex.StackTrace}");
            }
        }

        private async Task FetchBinanceWebSocketPricesAsync(CancellationToken cancellationToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var binanceService = scope.ServiceProvider.GetRequiredService<IBinanceService>();

            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await binanceService.GetTickerPricesViaWebSocketAsync();
                Console.WriteLine("Binance WebSocket prices fetched successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to fetch Binance WebSocket prices: {ex.Message}");
                Console.WriteLine($"Stack Trace: {ex.StackTrace}");
            }
        }

        private async Task FetchBybitWebSocketPricesAsync(CancellationToken cancellationToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var bybitService = scope.ServiceProvider.GetRequiredService<IBybitService>();

            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await bybitService.GetTickerPricesViaWebSocketAsync();
                Console.WriteLine("Bybit WebSocket prices fetched successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to fetch Bybit WebSocket prices: {ex.Message}");
                Console.WriteLine($"Stack Trace: {ex.StackTrace}");
            }
        }

        private async Task FetchOkxWebSocketPricesAsync(CancellationToken cancellationToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var okxService = scope.ServiceProvider.GetRequiredService<IOkxService>();

            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await okxService.GetTickerPricesViaWebSocketAsync();
                Console.WriteLine("OKX WebSocket prices fetched successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to fetch OKX WebSocket prices: {ex.Message}");
                Console.WriteLine($"Stack Trace: {ex.StackTrace}");
            }
        }

        private async Task FetchKuCoinWebSocketPricesAsync(CancellationToken cancellationToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var kucoinService = scope.ServiceProvider.GetRequiredService<IKuCoinService>();

            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await kucoinService.GetTickerPricesViaWebSocketAsync();
                Console.WriteLine("KuCoin WebSocket prices fetched successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to fetch KuCoin WebSocket prices: {ex.Message}");
                Console.WriteLine($"Stack Trace: {ex.StackTrace}");
            }
        }

        private async Task CalculateAveragePricesAsync(CancellationToken cancellationToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var averagePriceService = scope.ServiceProvider.GetRequiredService<AveragePriceService>();

            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await averagePriceService.CalculateAndSaveAveragePricesAsync();
                Console.WriteLine("Average asset prices calculated and saved successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to calculate average asset prices: {ex.Message}");
                Console.WriteLine($"Stack Trace: {ex.StackTrace}");
            }
        }

        private async Task RunSignalProviderServiceAsync(CancellationToken cancellationToken)
        {
            await using var scope = _serviceProvider.CreateAsyncScope();
            var signalProviderService = scope.ServiceProvider.GetRequiredService<SignalProviderService>();

            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                Console.WriteLine("Running SignalProviderService...");
                await signalProviderService.CalculateAndInsertProviderDataAsync();
                Console.WriteLine("SignalProviderService ran successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to run SignalProviderService: {ex.Message}");
                Console.WriteLine($"Stack Trace: {ex.StackTrace}");
            }
        }

        private Task CreateDefaultProviderSettingsForUsersAsync(CancellationToken cancellationToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var signalProviderService = scope.ServiceProvider.GetRequiredService<SignalProviderService>();

            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                Console.WriteLine("Creating default provider settings for users...");
                signalProviderService.CreateDefaultProviderSettingsForUsers();
                Console.WriteLine("Default provider settings for users created successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to create default provider settings for users: {ex.Message}");
                Console.WriteLine($"Stack Trace: {ex.StackTrace}");
            }

            return Task.CompletedTask;
        }

        private async Task SignalPerformanceAsync(CancellationToken cancellationToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var signalPerformanceService = scope.ServiceProvider.GetRequiredService<SignalPerformanceService>();

            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                Console.WriteLine("Calculating signal performance...");
                await signalPerformanceService.TrackPerformance();
                Console.WriteLine("Signal performance calculated successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to signal performance: {ex.Message}");
                Console.WriteLine($"Stack Trace: {ex.StackTrace}");
            }
        }

        private async Task UserOrderWatchDogAsync(CancellationToken cancellationToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var userOrderWatchDogService = scope.ServiceProvider.GetRequiredService<UserOrderWatchDogService>();

            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                Console.WriteLine("Calculating user orders...");
                await userOrderWatchDogService.TriggerOrderProcessing();
                Console.WriteLine("User Orders calculated successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to calculate user orders: {ex.Message}");
                Console.WriteLine($"Stack Trace: {ex.StackTrace}");
            }
        }
    
    
    }
}
