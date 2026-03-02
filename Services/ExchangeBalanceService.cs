using AutoSignals.Services;

namespace AutoSignals.Services
{
    public class ExchangeBalanceService
    {
        private readonly ErrorLogService _errorLogService;
        private readonly IServiceScopeFactory _scopeFactory;

        public ExchangeBalanceService(
            ErrorLogService errorLogService,
            IServiceScopeFactory scopeFactory)
        {
            _errorLogService = errorLogService;
            _scopeFactory = scopeFactory;
        }

        public async Task<decimal> GetExchangeBalanceAsync(
            int? exchangeId,
            string apiKey,
            string apiSecret,
            string apiPassword)
        {
            if (exchangeId is null)
            {
                return 0m;
            }

            try
            {
                return exchangeId.Value switch
                {
                    1 => await GetBitgetBalanceAsync(apiKey, apiSecret, apiPassword),
                    2 => await GetOkxBalanceAsync(apiKey, apiSecret, apiPassword),
                    _ => 0m
                };
            }
            catch
            {
                return 0m;
            }
        }

        private async Task<decimal> GetBitgetBalanceAsync(string apiKey, string apiSecret, string apiPassword)
        {
            var service = new BitgetPriceService(apiKey, apiSecret, apiPassword, _errorLogService, _scopeFactory);
            return await service.GetBalance(apiKey, apiSecret, apiPassword);
        }

        private async Task<decimal> GetOkxBalanceAsync(string apiKey, string apiSecret, string apiPassword)
        {
            var service = new OkxPriceService(apiKey, apiSecret, apiPassword, _errorLogService, _scopeFactory);
            return await service.GetBalance(apiKey, apiSecret, apiPassword);
        }
    }
}