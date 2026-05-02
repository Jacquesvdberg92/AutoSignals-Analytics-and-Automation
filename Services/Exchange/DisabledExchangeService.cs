namespace AutoSignals.Services
{
    public class DisabledExchangeService : IBitgetService, IBinanceService, IBybitService, IOkxService, IKuCoinService
    {
        private readonly ILogger<DisabledExchangeService> _logger;
        private readonly string _exchangeName;

        public DisabledExchangeService(ILogger<DisabledExchangeService> logger, string exchangeName)
        {
            _logger = logger;
            _exchangeName = exchangeName;
        }

        private Task LogDisabledAsync()
        {
            _logger.LogInformation("{ExchangeName} integration is disabled because configuration is missing.", _exchangeName);
            return Task.CompletedTask;
        }

        public async Task<IEnumerable<object>> GetBitgetMarketsAsync()
        {
            await LogDisabledAsync();
            return Enumerable.Empty<object>();
        }

        public async Task FetchAllBitgetAssetPricesV2Async() => await LogDisabledAsync();
        public async Task GetTickerPricesViaWebSocketAsync() => await LogDisabledAsync();
        public async Task<decimal?> FetchBitgetAssetPriceAsync(string symbol)
        {
            await LogDisabledAsync();
            return null;
        }

        public async Task DeleteDuplicates() => await LogDisabledAsync();

        public async Task<IEnumerable<object>> GetBinanceMarketsAsync()
        {
            await LogDisabledAsync();
            return Enumerable.Empty<object>();
        }

        public async Task FetchAllBinanceAssetPricesV2Async() => await LogDisabledAsync();
        public async Task<decimal?> FetchBinanceAssetPriceAsync(string symbol)
        {
            await LogDisabledAsync();
            return null;
        }

        public async Task<IEnumerable<object>> GetBybitMarketsAsync()
        {
            await LogDisabledAsync();
            return Enumerable.Empty<object>();
        }

        public async Task FetchAllBybitAssetPricesV2Async() => await LogDisabledAsync();
        public async Task<decimal?> FetchBybitAssetPriceAsync(string symbol)
        {
            await LogDisabledAsync();
            return null;
        }

        public async Task<IEnumerable<object>> GetOkxMarketsAsync()
        {
            await LogDisabledAsync();
            return Enumerable.Empty<object>();
        }

        public async Task FetchAllOkxAssetPricesV2Async() => await LogDisabledAsync();
        public async Task<decimal?> FetchOkxAssetPriceAsync(string symbol)
        {
            await LogDisabledAsync();
            return null;
        }

        public async Task<IEnumerable<object>> GetKuCoinMarketsAsync()
        {
            await LogDisabledAsync();
            return Enumerable.Empty<object>();
        }

        public async Task FetchAllKuCoinAssetPricesV2Async() => await LogDisabledAsync();
        public async Task<decimal?> FetchKuCoinAssetPriceAsync(string symbol)
        {
            await LogDisabledAsync();
            return null;
        }
    }
}
