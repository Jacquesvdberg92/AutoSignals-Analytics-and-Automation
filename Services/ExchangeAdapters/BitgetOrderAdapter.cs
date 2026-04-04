using AutoSignals.Models;
using AutoSignals.ViewModels;
using Newtonsoft.Json;

namespace AutoSignals.Services.ExchangeAdapters
{
    public class BitgetOrderAdapter : CcxtExchangeOrderAdapterBase
    {
        public BitgetOrderAdapter(ErrorLogService errorLogService, IServiceScopeFactory scopeFactory)
            : base(errorLogService, scopeFactory)
        {
        }

        public override string ExchangeName => "Bitget";

        protected override ccxt.Exchange CreateClient(ExchangeCredentials credentials)
        {
            var client = new ccxt.bitget(new Dictionary<string, object>
            {
                { "apiKey", credentials.ApiKey },
                { "secret", credentials.ApiSecret },
                { "password", credentials.Passphrase ?? string.Empty },
                { "enableRateLimit", true }
            });
            client.options["defaultType"] = "swap";
            return client;
        }

        protected override Dictionary<string, object>? GetBalanceParameters()
        {
            return new Dictionary<string, object> { { "type", "swap" } };
        }

        public override async Task<decimal?> FetchPriceAsync(string symbol, ExchangeCredentials credentials, CancellationToken cancellationToken = default)
        {
            var service = new BitgetPriceService(credentials.ApiKey, credentials.ApiSecret, credentials.Passphrase ?? string.Empty, ErrorLogService, ScopeFactory);
            return await service.FetchBitgetAssetPriceAsync(symbol);
        }

        public override async Task<ExchangeOrderResult> SendEntryOrderAsync(Order order, ExchangeCredentials credentials, CancellationToken cancellationToken = default)
        {
            var service = new BitgetPriceService(credentials.ApiKey, credentials.ApiSecret, credentials.Passphrase ?? string.Empty, ErrorLogService, ScopeFactory);
            var result = await service.SendEntryOrderAsync(order, credentials.ApiKey, credentials.ApiSecret, credentials.Passphrase ?? string.Empty);
            EnrichResult(result);
            return result;
        }

        public override async Task<ExchangeOrderResult> SendTakeProfitOrderAsync(Order order, ExchangeCredentials credentials, CancellationToken cancellationToken = default)
        {
            var service = new BitgetPriceService(credentials.ApiKey, credentials.ApiSecret, credentials.Passphrase ?? string.Empty, ErrorLogService, ScopeFactory);
            var responseText = await service.SendTakeProfitOrderAsync(order, credentials.ApiKey, credentials.ApiSecret, credentials.Passphrase ?? string.Empty);
            return BuildResultFromText(responseText);
        }

        public override async Task<ExchangeOrderResult> SendStoplossOrderAsync(Order order, ExchangeCredentials credentials, CancellationToken cancellationToken = default)
        {
            var service = new BitgetPriceService(credentials.ApiKey, credentials.ApiSecret, credentials.Passphrase ?? string.Empty, ErrorLogService, ScopeFactory);
            var responseText = await service.SendStoplossOrderAsync(order, credentials.ApiKey, credentials.ApiSecret, credentials.Passphrase ?? string.Empty);
            return BuildResultFromText(responseText);
        }

        private ExchangeOrderResult BuildResultFromText(string responseText)
        {
            if (string.IsNullOrWhiteSpace(responseText))
            {
                return BuildFailureResult("Empty Bitget response.");
            }

            if (responseText.StartsWith("Error:", StringComparison.OrdinalIgnoreCase))
            {
                return BuildFailureResult(responseText);
            }

            try
            {
                var response = JsonConvert.DeserializeObject<Dictionary<string, object>>(responseText);
                var result = BuildSuccessResult(response);
                EnrichResult(result);
                return result;
            }
            catch
            {
                return new ExchangeOrderResult
                {
                    Success = true,
                    Response = responseText,
                    Status = "submitted"
                };
            }
        }

        private static void EnrichResult(ExchangeOrderResult result)
        {
            if (result.Response is not Dictionary<string, object> response)
            {
                return;
            }

            if (response.TryGetValue("data", out var dataObj) && dataObj is IEnumerable<object> rows)
            {
                var first = rows.OfType<Dictionary<string, object>>().FirstOrDefault();
                if (first != null)
                {
                    result.ExternalOrderId ??= ReadString(first, "orderId") ?? ReadString(first, "ordId") ?? ReadString(first, "id");
                    result.ClientOrderId ??= ReadString(first, "clientOid") ?? ReadString(first, "clOrdId");
                    result.Status ??= ReadString(first, "status");
                }
            }

            result.ExternalOrderId ??= ReadString(response, "orderId") ?? ReadString(response, "id");
            result.ClientOrderId ??= ReadString(response, "clientOid") ?? ReadString(response, "clOrdId");
            result.Status ??= ReadString(response, "status") ?? ReadString(response, "msg");
        }
    }
}
