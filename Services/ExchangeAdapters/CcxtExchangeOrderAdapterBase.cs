using AutoSignals.Data;
using AutoSignals.Models;
using AutoSignals.ViewModels;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using System.Globalization;

namespace AutoSignals.Services.ExchangeAdapters
{
    public abstract class CcxtExchangeOrderAdapterBase : IExchangeOrderAdapter
    {
        protected readonly ErrorLogService ErrorLogService;
        protected readonly IServiceScopeFactory ScopeFactory;

        protected CcxtExchangeOrderAdapterBase(ErrorLogService errorLogService, IServiceScopeFactory scopeFactory)
        {
            ErrorLogService = errorLogService;
            ScopeFactory = scopeFactory;
        }

        public abstract string ExchangeName { get; }

        public abstract Task<decimal?> FetchPriceAsync(string symbol, ExchangeCredentials credentials, CancellationToken cancellationToken = default);
        public abstract Task<ExchangeOrderResult> SendEntryOrderAsync(Order order, ExchangeCredentials credentials, CancellationToken cancellationToken = default);
        public abstract Task<ExchangeOrderResult> SendTakeProfitOrderAsync(Order order, ExchangeCredentials credentials, CancellationToken cancellationToken = default);
        public abstract Task<ExchangeOrderResult> SendStoplossOrderAsync(Order order, ExchangeCredentials credentials, CancellationToken cancellationToken = default);
        protected abstract ccxt.Exchange CreateClient(ExchangeCredentials credentials);

        protected virtual Dictionary<string, object>? GetBalanceParameters()
        {
            return null;
        }

        public virtual async Task<decimal> GetBalanceAsync(ExchangeCredentials credentials, CancellationToken cancellationToken = default)
        {
            try
            {
                var client = CreateClient(credentials);
                Dictionary<string, object>? response = null;
                Exception? lastException = null;

                for (var retryCount = 0; retryCount < 3; retryCount++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        response = await client.fetchBalance(GetBalanceParameters()) as Dictionary<string, object>;
                        if (response != null && !ResponseContainsMessage(response, "Too many requests"))
                        {
                            break;
                        }

                        if (ResponseContainsMessage(response, "Too many requests"))
                        {
                            await Task.Delay(5000, cancellationToken);
                            continue;
                        }

                        break;
                    }
                    catch (Exception ex) when (IsRateLimit(ex))
                    {
                        lastException = ex;
                        await Task.Delay(5000, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        lastException = ex;
                        break;
                    }
                }

                var freeDict = response != null && response.TryGetValue("free", out var freeObj)
                    ? freeObj as Dictionary<string, object>
                    : null;

                if (freeDict != null && freeDict.TryGetValue("USDT", out var usdtBalance) && usdtBalance != null)
                {
                    return Convert.ToDecimal(usdtBalance, CultureInfo.InvariantCulture);
                }

                if (lastException != null)
                {
                    await ErrorLogService.LogErrorAsync(
                        $"Failed to fetch {ExchangeName} balance: {lastException.Message}",
                        lastException.StackTrace,
                        nameof(GetBalanceAsync));
                }
            }
            catch (Exception ex)
            {
                await ErrorLogService.LogErrorAsync(
                    $"Failed to fetch {ExchangeName} balance: {ex.Message}",
                    ex.StackTrace,
                    nameof(GetBalanceAsync));
            }

            return 0m;
        }

        public virtual async Task<ExchangeOrderSyncResult?> SyncOrderAsync(Order order, ExchangeCredentials credentials, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(order.ExternalOrderId))
            {
                return null;
            }

            try
            {
                var client = CreateClient(credentials);
                var response = await client.fetchOrder(order.ExternalOrderId, order.Symbol) as Dictionary<string, object>;
                if (response == null)
                {
                    return null;
                }

                return new ExchangeOrderSyncResult
                {
                    Success = true,
                    ExternalOrderId = ReadString(response, "id") ?? order.ExternalOrderId,
                    ClientOrderId = ReadString(response, "clientOrderId") ?? ReadString(response, "clientOid") ?? ReadString(response, "clOrdId"),
                    ExchangeStatus = ReadString(response, "status"),
                    NormalizedStatus = NormalizeStatus(ReadString(response, "status")),
                    AveragePrice = ReadDecimal(response, "average") ?? ReadDecimal(response, "avgPrice") ?? ReadDecimal(response, "price"),
                    FilledQuantity = ReadDecimal(response, "filled") ?? ReadDecimal(response, "filledSize") ?? ReadDecimal(response, "amount"),
                    ResponseJson = Serialize(response),
                    SyncedAtUtc = DateTime.UtcNow
                };
            }
            catch (Exception ex)
            {
                await ErrorLogService.LogErrorAsync(
                    $"Failed to sync {ExchangeName} order {order.ExternalOrderId}: {ex.Message}",
                    ex.StackTrace,
                    nameof(SyncOrderAsync),
                    Serialize(order));

                return new ExchangeOrderSyncResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    ErrorCode = ExtractErrorCode(ex.Message),
                    SyncedAtUtc = DateTime.UtcNow
                };
            }
        }

        protected async Task<Position?> LoadPositionAsync(Order order, CancellationToken cancellationToken)
        {
            if (!int.TryParse(order.PositionId, out var positionId))
            {
                return null;
            }

            using var scope = ScopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AutoSignalsDbContext>();
            return await context.Positions.FirstOrDefaultAsync(p => p.Id == positionId, cancellationToken);
        }

        protected static string Serialize(object? value)
        {
            return value == null
                ? string.Empty
                : JsonConvert.SerializeObject(value, Formatting.None);
        }

        protected static bool ResponseContainsMessage(Dictionary<string, object>? response, string text)
        {
            if (response == null)
            {
                return false;
            }

            return (ReadString(response, "message")?.Contains(text, StringComparison.OrdinalIgnoreCase) == true)
                || (ReadString(response, "msg")?.Contains(text, StringComparison.OrdinalIgnoreCase) == true);
        }

        protected static bool IsRateLimit(Exception ex)
        {
            return ex.Message.Contains("Too many requests", StringComparison.OrdinalIgnoreCase)
                || ex.Message.Contains("rate limit", StringComparison.OrdinalIgnoreCase);
        }

        protected static string? ExtractErrorCode(string? message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return null;
            }

            var digits = new string(message.Where(char.IsDigit).ToArray());
            return string.IsNullOrWhiteSpace(digits) ? null : digits;
        }

        protected static string? ReadString(Dictionary<string, object>? response, string key)
        {
            if (response == null || !response.TryGetValue(key, out var value) || value == null)
            {
                return null;
            }

            return value.ToString();
        }

        protected static decimal? ReadDecimal(Dictionary<string, object>? response, string key)
        {
            if (response == null || !response.TryGetValue(key, out var value) || value == null)
            {
                return null;
            }

            return value switch
            {
                decimal decimalValue => decimalValue,
                double doubleValue => Convert.ToDecimal(doubleValue, CultureInfo.InvariantCulture),
                float floatValue => Convert.ToDecimal(floatValue, CultureInfo.InvariantCulture),
                long longValue => longValue,
                int intValue => intValue,
                string stringValue when decimal.TryParse(stringValue, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) => parsed,
                _ => Convert.ToDecimal(value, CultureInfo.InvariantCulture)
            };
        }

        protected static string NormalizeStatus(string? status)
        {
            if (string.IsNullOrWhiteSpace(status))
            {
                return "UNKNOWN";
            }

            return status.Trim().ToLowerInvariant() switch
            {
                "closed" => "FILLED",
                "filled" => "FILLED",
                "open" => "OPEN",
                "new" => "OPEN",
                "partially_filled" => "PARTIALLY_FILLED",
                "partiallyfilled" => "PARTIALLY_FILLED",
                "canceled" => "CANCELLED",
                "cancelled" => "CANCELLED",
                "rejected" => "REJECTED",
                _ => status.ToUpperInvariant()
            };
        }

        protected static ExchangeOrderResult BuildFailureResult(string errorMessage, object? response = null, string? errorCode = null)
        {
            return new ExchangeOrderResult
            {
                Success = false,
                ErrorMessage = errorMessage,
                ErrorCode = errorCode,
                Response = response
            };
        }

        protected static ExchangeOrderResult BuildSuccessResult(Dictionary<string, object>? response)
        {
            return new ExchangeOrderResult
            {
                Success = response != null,
                Response = response,
                ErrorCode = response != null ? ReadString(response, "code") : null,
                ErrorMessage = response != null ? ReadString(response, "msg") ?? ReadString(response, "message") : null,
                ExternalOrderId = response != null ? ReadString(response, "id") : null,
                ClientOrderId = response != null ? ReadString(response, "clientOrderId") ?? ReadString(response, "clientOid") ?? ReadString(response, "clOrdId") : null,
                Status = response != null ? ReadString(response, "status") : null,
                AveragePrice = response != null ? ReadDecimal(response, "average") ?? ReadDecimal(response, "avgPrice") ?? ReadDecimal(response, "price") : null,
                FilledQuantity = response != null ? ReadDecimal(response, "filled") ?? ReadDecimal(response, "amount") : null
            };
        }
    }
}
