using AutoSignals.Data;
using Microsoft.EntityFrameworkCore;

namespace AutoSignals.Services.ExchangeAdapters
{
    public class ExchangeOrderAdapterFactory
    {
        private readonly AutoSignalsDbContext _context;
        private readonly IReadOnlyDictionary<string, IExchangeOrderAdapter> _adapters;

        public ExchangeOrderAdapterFactory(AutoSignalsDbContext context, IEnumerable<IExchangeOrderAdapter> adapters)
        {
            _context = context;
            _adapters = adapters.ToDictionary(a => a.ExchangeName, StringComparer.OrdinalIgnoreCase);
        }

        public async Task<IExchangeOrderAdapter> GetRequiredAdapterAsync(int exchangeId, CancellationToken cancellationToken = default)
        {
            var exchangeName = await ResolveExchangeNameAsync(exchangeId.ToString(), cancellationToken);
            return GetRequiredAdapterByName(exchangeName);
        }

        public async Task<IExchangeOrderAdapter> GetRequiredAdapterAsync(string? exchangeIdOrName, CancellationToken cancellationToken = default)
        {
            var exchangeName = await ResolveExchangeNameAsync(exchangeIdOrName, cancellationToken);
            return GetRequiredAdapterByName(exchangeName);
        }

        public async Task<string> ResolveExchangeNameAsync(string? exchangeIdOrName, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(exchangeIdOrName))
            {
                throw new InvalidOperationException("Exchange value was not provided.");
            }

            if (int.TryParse(exchangeIdOrName, out var exchangeId))
            {
                var exchange = await _context.Exchanges
                    .AsNoTracking()
                    .FirstOrDefaultAsync(e => e.Id == exchangeId, cancellationToken);

                if (!string.IsNullOrWhiteSpace(exchange?.Name))
                {
                    return exchange.Name;
                }

                return exchangeId switch
                {
                    1 => "Bitget",
                    2 => "Binance",
                    3 => "Bybit",
                    4 => "Okx",
                    5 => "KuCoin",
                    _ => throw new InvalidOperationException($"Unsupported exchange id '{exchangeId}'.")
                };
            }

            var normalized = exchangeIdOrName.Trim();
            var dbExchange = await _context.Exchanges
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.Name == normalized, cancellationToken);

            if (!string.IsNullOrWhiteSpace(dbExchange?.Name))
            {
                return dbExchange.Name;
            }

            return normalized switch
            {
                var name when name.Equals("OKX", StringComparison.OrdinalIgnoreCase) => "Okx",
                var name when name.Equals("BITGET", StringComparison.OrdinalIgnoreCase) => "Bitget",
                var name when name.Equals("BINANCE", StringComparison.OrdinalIgnoreCase) => "Binance",
                var name when name.Equals("BYBIT", StringComparison.OrdinalIgnoreCase) => "Bybit",
                var name when name.Equals("KUCOIN", StringComparison.OrdinalIgnoreCase) => "KuCoin",
                _ => throw new InvalidOperationException($"Unsupported exchange '{exchangeIdOrName}'.")
            };
        }

        private IExchangeOrderAdapter GetRequiredAdapterByName(string exchangeName)
        {
            if (_adapters.TryGetValue(exchangeName, out var adapter))
            {
                return adapter;
            }

            throw new InvalidOperationException($"No order adapter is registered for exchange '{exchangeName}'.");
        }
    }
}
