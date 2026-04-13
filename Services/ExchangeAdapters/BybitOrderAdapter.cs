using AutoSignals.Models;
using AutoSignals.ViewModels;

namespace AutoSignals.Services.ExchangeAdapters
{
    public class BybitOrderAdapter : CcxtExchangeOrderAdapterBase
    {
        public BybitOrderAdapter(ErrorLogService errorLogService, IServiceScopeFactory scopeFactory)
            : base(errorLogService, scopeFactory)
        {
        }

        public override string ExchangeName => "Bybit";

        protected override ccxt.Exchange CreateClient(ExchangeCredentials credentials)
        {
            var client = new ccxt.bybit(new Dictionary<string, object>
            {
                { "apiKey", credentials.ApiKey },
                { "secret", credentials.ApiSecret },
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
                try { await client.setPositionMode(true, order.Symbol); } catch { }
                try { await client.setMarginMode(order.IsIsolated ? "isolated" : "cross", order.Symbol); } catch { }
                try { await client.setLeverage(order.Leverage, order.Symbol); } catch { }

                var response = await client.createOrder(
                    order.Symbol,
                    "market",
                    order.Side.ToLowerInvariant(),
                    order.Size,
                    null,
                    new Dictionary<string, object>
                    {
                        { "positionIdx", order.Side.Equals("buy", StringComparison.OrdinalIgnoreCase) ? 1 : 2 },
                        { "category", "linear" }
                    }) as Dictionary<string, object>;

                return FinalizeResult(response);
            }
            catch (Exception ex)
            {
                await ErrorLogService.LogErrorAsync($"Failed to place Bybit entry order: {ex.Message}", ex.StackTrace, nameof(SendEntryOrderAsync), Serialize(order));
                return BuildFailureResult(ex.Message, null, ExtractErrorCode(ex.Message));
            }
        }

        public override async Task<ExchangeOrderResult> SendTakeProfitOrderAsync(Order order, ExchangeCredentials credentials, CancellationToken cancellationToken = default)
        {
            return await SendReduceOnlyOrderAsync(order, credentials, useFullPositionSize: false, cancellationToken);
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
                // closePosition=true closes the full remaining position regardless of qty.
                // Quantity is doubled to ensure ccxt doesn't under-fill due to rounding.
                var response = await client.createOrder(
                    order.Symbol,
                    "market",
                    order.Side.ToLowerInvariant(),
                    position.Size * 2,
                    null,
                    new Dictionary<string, object>
                    {
                        { "closePosition", true },
                        { "positionIdx", order.Side.Equals("sell", StringComparison.OrdinalIgnoreCase) ? 1 : 2 },
                        { "category", "linear" }
                    }) as Dictionary<string, object>;

                return FinalizeResult(response);
            }
            catch (Exception ex)
            {
                await ErrorLogService.LogErrorAsync($"Failed to place Bybit stoploss order: {ex.Message}", ex.StackTrace, nameof(SendStoplossOrderAsync), Serialize(order));
                return BuildFailureResult(ex.Message, null, ExtractErrorCode(ex.Message));
            }
        }

        private async Task<ExchangeOrderResult> SendReduceOnlyOrderAsync(Order order, ExchangeCredentials credentials, bool useFullPositionSize, CancellationToken cancellationToken)
        {
            var position = await LoadPositionAsync(order, cancellationToken);
            if (position == null)
            {
                return BuildFailureResult("Position not found.");
            }

            var client = CreateClient(credentials);
            try
            {
                var positionSize = position.Size;
                var quantity = useFullPositionSize ? positionSize : positionSize * (order.Size / 100d);
                if (quantity <= 0)
                {
                    return BuildFailureResult("Computed close size is zero.");
                }

                var response = await client.createOrder(
                    order.Symbol,
                    "market",
                    order.Side.ToLowerInvariant(),
                    quantity,
                    null,
                    new Dictionary<string, object>
                    {
                        { "reduceOnly", true },
                        { "positionIdx", order.Side.Equals("sell", StringComparison.OrdinalIgnoreCase) ? 1 : 2 },
                        { "category", "linear" }
                    }) as Dictionary<string, object>;

                return FinalizeResult(response);
            }
            catch (Exception ex)
            {
                await ErrorLogService.LogErrorAsync($"Failed to place Bybit reduce-only order: {ex.Message}", ex.StackTrace, nameof(SendReduceOnlyOrderAsync), Serialize(order));
                return BuildFailureResult(ex.Message, null, ExtractErrorCode(ex.Message));
            }
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
                result.ExternalOrderId ??= ReadString(info, "orderId") ?? ReadString(info, "orderLinkId") ?? ReadString(info, "id");
                result.ClientOrderId ??= ReadString(info, "orderLinkId") ?? ReadString(info, "clientOrderId");
                result.Status ??= ReadString(info, "orderStatus") ?? ReadString(info, "status");
            }

            result.ExternalOrderId ??= ReadString(response, "orderId") ?? ReadString(response, "id");
            result.ClientOrderId ??= ReadString(response, "clientOrderId") ?? ReadString(response, "orderLinkId");
            result.Status ??= ReadString(response, "status");
            result.Success = string.IsNullOrWhiteSpace(ReadString(response, "retMsg"));
            return result;
        }
    }
}
