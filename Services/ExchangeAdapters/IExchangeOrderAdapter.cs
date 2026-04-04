using AutoSignals.Models;
using AutoSignals.ViewModels;

namespace AutoSignals.Services.ExchangeAdapters
{
    public interface IExchangeOrderAdapter
    {
        string ExchangeName { get; }
        Task<decimal> GetBalanceAsync(ExchangeCredentials credentials, CancellationToken cancellationToken = default);
        Task<decimal?> FetchPriceAsync(string symbol, ExchangeCredentials credentials, CancellationToken cancellationToken = default);
        Task<ExchangeOrderResult> SendEntryOrderAsync(Order order, ExchangeCredentials credentials, CancellationToken cancellationToken = default);
        Task<ExchangeOrderResult> SendTakeProfitOrderAsync(Order order, ExchangeCredentials credentials, CancellationToken cancellationToken = default);
        Task<ExchangeOrderResult> SendStoplossOrderAsync(Order order, ExchangeCredentials credentials, CancellationToken cancellationToken = default);
        Task<ExchangeOrderSyncResult?> SyncOrderAsync(Order order, ExchangeCredentials credentials, CancellationToken cancellationToken = default);
    }
}
