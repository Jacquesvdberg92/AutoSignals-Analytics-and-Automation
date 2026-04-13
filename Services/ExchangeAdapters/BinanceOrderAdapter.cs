using AutoSignals.Models;
using AutoSignals.ViewModels;

namespace AutoSignals.Services.ExchangeAdapters
{
    public class BinanceOrderAdapter : CcxtExchangeOrderAdapterBase
    {
        public BinanceOrderAdapter(ErrorLogService errorLogService, IServiceScopeFactory scopeFactory)
            : base(errorLogService, scopeFactory)
        {
        }

        public override string ExchangeName => "Binance";

        protected override ccxt.Exchange CreateClient(ExchangeCredentials credentials)
        {
            var client = new ccxt.binanceusdm(new Dictionary<string, object>
            {
                { "apiKey", credentials.ApiKey },
                { "secret", credentials.ApiSecret },
                { "enableRateLimit", true }
            });
            client.options["defaultType"] = "swap";
            client.options["adjustForTimeDifference"] = true;
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
                try { await client.setPositionMode(true); } catch { }
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
                        { "positionSide", order.Side.Equals("buy", StringComparison.OrdinalIgnoreCase) ? "LONG" : "SHORT" },
                        { "newOrderRespType", "RESULT" }
                    }) as Dictionary<string, object>;

                return FinalizeResult(response);
            }
            catch (Exception ex)
            {
                await ErrorLogService.LogErrorAsync($"Failed to place Binance entry order: {ex.Message}", ex.StackTrace, nameof(SendEntryOrderAsync), Serialize(order));
                return BuildFailureResult(ex.Message, null, ExtractErrorCode(ex.Message));
            }
        }

        public override async Task<ExchangeOrderResult> SendTakeProfitOrderAsync(Order order, ExchangeCredentials credentials, CancellationToken cancellationToken = default)
        {
            return await SendHedgeModeCloseOrderAsync(order, credentials, useFullPositionSize: false, cancellationToken);
        }

        public override async Task<ExchangeOrderResult> SendStoplossOrderAsync(Order order, ExchangeCredentials credentials, CancellationToken cancellationToken = default)
        {
            return await SendHedgeModeCloseOrderAsync(order, credentials, useFullPositionSize: true, cancellationToken);
        }

        private async Task<ExchangeOrderResult> SendHedgeModeCloseOrderAsync(Order order, ExchangeCredentials credentials, bool useFullPositionSize, CancellationToken cancellationToken)
        {
            var position = await LoadPositionAsync(order, cancellationToken);
            if (position == null)
            {
                return BuildFailureResult("Position not found.");
            }

            var client = CreateClient(credentials);
            try
            {
                var quantity = useFullPositionSize
                    ? position.Size
                    : position.Size * (order.Size / 100d);
                if (quantity <= 0)
                {
                    return BuildFailureResult("Computed close size is zero.");
                }

                // In hedge mode reduceOnly is not allowed; positionSide identifies which leg to close.
                // sell = closing a LONG, buy = closing a SHORT.
                var positionSide = order.Side.Equals("sell", StringComparison.OrdinalIgnoreCase) ? "LONG" : "SHORT";

                var response = await client.createOrder(
                    order.Symbol,
                    "market",
                    order.Side.ToLowerInvariant(),
                    quantity,
                    null,
                    new Dictionary<string, object>
                    {
                        { "positionSide", positionSide },
                        { "newOrderRespType", "RESULT" }
                    }) as Dictionary<string, object>;

                return FinalizeResult(response);
            }
            catch (Exception ex)
            {
                await ErrorLogService.LogErrorAsync($"Failed to place Binance hedge-mode close order: {ex.Message}", ex.StackTrace, nameof(SendHedgeModeCloseOrderAsync), Serialize(order));
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
                result.ExternalOrderId ??= ReadString(info, "orderId") ?? ReadString(info, "id");
                result.ClientOrderId ??= ReadString(info, "clientOrderId") ?? ReadString(info, "newClientOrderId");
                result.Status ??= ReadString(info, "status");
            }

            result.ExternalOrderId ??= ReadString(response, "id") ?? ReadString(response, "orderId");
            result.ClientOrderId ??= ReadString(response, "clientOrderId") ?? ReadString(response, "newClientOrderId");
            result.Status ??= ReadString(response, "status");
            result.Success = response.Count > 0 && string.IsNullOrWhiteSpace(ReadString(response, "msg"));
            return result;
        }
    }
}
