using AutoSignals.Models;
using AutoSignals.ViewModels;
using Newtonsoft.Json;

namespace AutoSignals.Services.ExchangeAdapters
{
    public class OkxOrderAdapter : CcxtExchangeOrderAdapterBase
    {
        public OkxOrderAdapter(ErrorLogService errorLogService, IServiceScopeFactory scopeFactory)
            : base(errorLogService, scopeFactory)
        {
        }

        public override string ExchangeName => "Okx";

        protected override ccxt.Exchange CreateClient(ExchangeCredentials credentials)
        {
            var client = new ccxt.okx(new Dictionary<string, object>
            {
                { "apiKey", credentials.ApiKey },
                { "secret", credentials.ApiSecret },
                { "password", credentials.Passphrase ?? string.Empty },
                { "enableRateLimit", true }
            });
            client.options["defaultType"] = "swap";
            client.options["defaultSettle"] = "USDT";
            return client;
        }

        protected override Dictionary<string, object>? GetBalanceParameters()
        {
            return new Dictionary<string, object> { { "type", "swap" } };
        }

        public override async Task<decimal?> FetchPriceAsync(string symbol, ExchangeCredentials credentials, CancellationToken cancellationToken = default)
        {
            var service = new OkxPriceService(credentials.ApiKey, credentials.ApiSecret, credentials.Passphrase ?? string.Empty, ErrorLogService, ScopeFactory);
            return await service.FetchOkxAssetPriceAsync(symbol);
        }

        public override async Task<ExchangeOrderResult> SendEntryOrderAsync(Order order, ExchangeCredentials credentials, CancellationToken cancellationToken = default)
        {
            var service = new OkxPriceService(credentials.ApiKey, credentials.ApiSecret, credentials.Passphrase ?? string.Empty, ErrorLogService, ScopeFactory);
            var result = await service.SendEntryOrderAsync(order, credentials.ApiKey, credentials.ApiSecret, credentials.Passphrase ?? string.Empty);
            EnrichResult(result);
            return result;
        }

        public override async Task<ExchangeOrderResult> SendTakeProfitOrderAsync(Order order, ExchangeCredentials credentials, CancellationToken cancellationToken = default)
        {
            var service = new OkxPriceService(credentials.ApiKey, credentials.ApiSecret, credentials.Passphrase ?? string.Empty, ErrorLogService, ScopeFactory);
            var responseText = await service.SendTakeProfitOrderAsync(order, credentials.ApiKey, credentials.ApiSecret, credentials.Passphrase ?? string.Empty);
            return BuildResultFromText(responseText);
        }

        public override async Task<ExchangeOrderResult> SendStoplossOrderAsync(Order order, ExchangeCredentials credentials, CancellationToken cancellationToken = default)
        {
            var service = new OkxPriceService(credentials.ApiKey, credentials.ApiSecret, credentials.Passphrase ?? string.Empty, ErrorLogService, ScopeFactory);
            var responseText = await service.SendStoplossOrderAsync(order, credentials.ApiKey, credentials.ApiSecret, credentials.Passphrase ?? string.Empty);
            return BuildResultFromText(responseText);
        }

        private ExchangeOrderResult BuildResultFromText(string responseText)
        {
            if (string.IsNullOrWhiteSpace(responseText))
            {
                return BuildFailureResult("Empty OKX response.");
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
                    result.ExternalOrderId ??= ReadString(first, "ordId") ?? ReadString(first, "orderId") ?? ReadString(first, "id");
                    result.ClientOrderId ??= ReadString(first, "clOrdId") ?? ReadString(first, "clientOrderId");
                    result.Status ??= ReadString(first, "state") ?? ReadString(first, "status");
                }
            }

            result.ExternalOrderId ??= ReadString(response, "ordId") ?? ReadString(response, "orderId") ?? ReadString(response, "id");
            result.ClientOrderId ??= ReadString(response, "clOrdId") ?? ReadString(response, "clientOrderId");
            result.Status ??= ReadString(response, "status") ?? ReadString(response, "msg");
        }
    }
}
