using AutoSignals.Models;
using AutoSignals.Services;

namespace AutoSignals.Services
{
    public class ExchangeBalanceService
    {
        private readonly ErrorLogService _errorLogService;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly AesEncryptionService _encryptionService;

        public ExchangeBalanceService(
            ErrorLogService errorLogService,
            IServiceScopeFactory scopeFactory,
            AesEncryptionService encryptionService)
        {
            _errorLogService = errorLogService;
            _scopeFactory = scopeFactory;
            _encryptionService = encryptionService;
        }

        public async Task<decimal> GetConnectionBalanceAsync(UserExchangeConnection connection)
        {
            var apiKey    = _encryptionService.Decrypt(connection.ApiKey ?? "");
            var apiSecret = _encryptionService.Decrypt(connection.ApiSecret ?? "");
            var apiPwd    = _encryptionService.Decrypt(connection.ApiPassword ?? "");
            return await GetExchangeBalanceAsync(connection.ExchangeId, apiKey, apiSecret, apiPwd);
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