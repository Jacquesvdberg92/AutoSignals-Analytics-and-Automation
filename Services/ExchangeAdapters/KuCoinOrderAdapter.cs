using AutoSignals.Models;
using AutoSignals.ViewModels;

namespace AutoSignals.Services.ExchangeAdapters
{
    public class KuCoinOrderAdapter : CcxtExchangeOrderAdapterBase
    {
        public KuCoinOrderAdapter(ErrorLogService errorLogService, IServiceScopeFactory scopeFactory)
            : base(errorLogService, scopeFactory)
        {
        }

        public override string ExchangeName => "KuCoin";

        protected override ccxt.Exchange CreateClient(ExchangeCredentials credentials)
        {
            var client = new ccxt.kucoinfutures(new Dictionary<string, object>
            {
                { "apiKey", credentials.ApiKey },
                { "secret", credentials.ApiSecret },
                { "password", credentials.Passphrase ?? string.Empty },
                { "enableRateLimit", true }
            });
            client.options["defaultType"] = "swap";
            client.options["fetchCurrencies"] = false;
            return client;
        }

        protected override Dictionary<string, object>? GetBalanceParameters()
        {
            return new Dictionary<string, object> { { "type", "swap" } };
        }

        public override async Task<decimal?> FetchPriceAsync(string symbol, ExchangeCredentials credentials, CancellationToken cancellationToken = default)
        {
            var client = CreateClient(credentials);
            var ticker = await client.fetchTicker(symbol) as Dictionary<string, object>;
            return ReadDecimal(ticker, "last");
        }

        public override async Task<ExchangeOrderResult> SendEntryOrderAsync(Order order, ExchangeCredentials credentials, CancellationToken cancellationToken = default)
        {
            var client = CreateClient(credentials);
            try
            {
                try { await client.setMarginMode(order.IsIsolated ? "isolated" : "cross", order.Symbol); } catch { }
                try { await client.setLeverage(order.Leverage, order.Symbol); } catch { }

                var contractSize = await GetContractSizeAsync(client, order.Symbol);
                var contracts = BtcToContracts(order.Size, contractSize);

                var response = await client.createOrder(
                    order.Symbol,
                    "market",
                    order.Side.ToLowerInvariant(),
                    contracts,
                    null,
                    new Dictionary<string, object>
                    {
                        { "leverage", order.Leverage },
                        { "marginMode", order.IsIsolated ? "ISOLATED" : "CROSS" }
                    }) as Dictionary<string, object>;

                return FinalizeResult(response);
            }
            catch (Exception ex)
            {
                await ErrorLogService.LogErrorAsync($"Failed to place KuCoin entry order: {ex.Message}", ex.StackTrace, nameof(SendEntryOrderAsync), Serialize(order));
                return BuildFailureResult(ex.Message, null, ExtractErrorCode(ex.Message));
            }
        }

        public override async Task<ExchangeOrderResult> SendTakeProfitOrderAsync(Order order, ExchangeCredentials credentials, CancellationToken cancellationToken = default)
        {
            var position = await LoadPositionAsync(order, cancellationToken);
            if (position == null)
            {
                return BuildFailureResult("Position not found.");
            }

            var client = CreateClient(credentials);
            try
            {
                var btcQuantity = position.Size * (order.Size / 100d);
                if (btcQuantity <= 0)
                {
                    return BuildFailureResult("Computed close size is zero.");
                }

                var contractSize = await GetContractSizeAsync(client, order.Symbol);
                var contracts = BtcToContracts(btcQuantity, contractSize);

                // reduceOnly only — closeOrder=true would close the FULL position regardless of qty
                var response = await client.createOrder(
                    order.Symbol,
                    "market",
                    order.Side.ToLowerInvariant(),
                    contracts,
                    null,
                    new Dictionary<string, object>
                    {
                        { "reduceOnly", true }
                    }) as Dictionary<string, object>;

                return FinalizeResult(response);
            }
            catch (Exception ex)
            {
                await ErrorLogService.LogErrorAsync($"Failed to place KuCoin take-profit order: {ex.Message}", ex.StackTrace, nameof(SendTakeProfitOrderAsync), Serialize(order));
                return BuildFailureResult(ex.Message, null, ExtractErrorCode(ex.Message));
            }
        }

        public override async Task<ExchangeOrderResult> SendStoplossOrderAsync(Order order, ExchangeCredentials credentials, CancellationToken cancellationToken = default)
        {
            var position = await LoadPositionAsync(order, cancellationToken);
            if (position == null)
            {
                return BuildFailureResult("Position not found.");
            }

            var client = CreateClient(credentials);
            try
            {
                // closeOrder=true closes the full remaining position regardless of qty.
                // Quantity is doubled to ensure ccxt doesn't under-fill due to rounding.
                var contractSize = await GetContractSizeAsync(client, order.Symbol);
                var contracts = BtcToContracts(position.Size * 2, contractSize);

                var response = await client.createOrder(
                    order.Symbol,
                    "market",
                    order.Side.ToLowerInvariant(),
                    contracts,
                    null,
                    new Dictionary<string, object>
                    {
                        { "reduceOnly", true },
                        { "closeOrder", true }
                    }) as Dictionary<string, object>;

                return FinalizeResult(response);
            }
            catch (Exception ex)
            {
                await ErrorLogService.LogErrorAsync($"Failed to place KuCoin stoploss order: {ex.Message}", ex.StackTrace, nameof(SendStoplossOrderAsync), Serialize(order));
                return BuildFailureResult(ex.Message, null, ExtractErrorCode(ex.Message));
            }
        }

        private static async Task<double> GetContractSizeAsync(ccxt.Exchange client, string symbol)
        {
            try
            {
                var markets = await client.loadMarkets() as Dictionary<string, object>;
                if (markets != null &&
                    markets.TryGetValue(symbol, out var marketObj) &&
                    marketObj is Dictionary<string, object> market &&
                    market.TryGetValue("contractSize", out var cs) && cs != null)
                {
                    return Convert.ToDouble(cs);
                }
            }
            catch { }
            return 0.001; // KuCoin BTC/USDT:USDT default: 0.001 BTC per contract
        }

        private static long BtcToContracts(double btcQty, double contractSize)
        {
            if (contractSize <= 0) contractSize = 0.001;
            return Math.Max(1, (long)Math.Floor(btcQty / contractSize));
        }

        private static ExchangeOrderResult FinalizeResult(Dictionary<string, object>? response)
        {
            var result = BuildSuccessResult(response);
            if (response == null)
            {
                return result;
            }

            if (response.TryGetValue("info", out var infoObj) && infoObj is Dictionary<string, object> info)
            {
                result.ExternalOrderId ??= ReadString(info, "orderId") ?? ReadString(info, "id");
                result.ClientOrderId ??= ReadString(info, "clientOid") ?? ReadString(info, "clientOrderId");
                result.Status ??= ReadString(info, "status");
            }

            result.ExternalOrderId ??= ReadString(response, "orderId") ?? ReadString(response, "id");
            result.ClientOrderId ??= ReadString(response, "clientOid") ?? ReadString(response, "clientOrderId");
            result.Status ??= ReadString(response, "status");
            result.Success = response.Count > 0 && string.IsNullOrWhiteSpace(ReadString(response, "msg"));
            return result;
        }
    }
}
