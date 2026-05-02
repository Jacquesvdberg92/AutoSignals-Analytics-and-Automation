# Exchange Price Services

**Namespace:** `AutoSignals.Services.Exchange`  
**Type:** Background services (one per exchange)

## Overview
Each supported exchange has a dedicated price service that maintains a real-time stream of asset prices. All services implement `IExchangeService` and are orchestrated by `ExchangeHostedService`.

## Services

| Service | Exchange | Method |
|---------|----------|--------|
| `BinancePriceService` | Binance | WebSocket ticker stream |
| `BybitPriceService` | Bybit | WebSocket ticker stream |
| `OkxPriceService` | OKX | WebSocket ticker stream |
| `KuCoinPriceService` | KuCoin | WebSocket ticker stream |
| `BitgetPriceService` | Bitget | WebSocket ticker stream |
| `DisabledExchangeService` | N/A | No-op — used when an exchange is disabled in settings |

## Interface: `IExchangeService`

| Member | Description |
|--------|-------------|
| `GetPriceAsync(symbol)` | Returns the latest known price for a symbol |
| `GetAllPricesAsync()` | Returns the full price snapshot for all tracked symbols |
| `IsRunning` | Whether the price stream is currently connected |

## Flow
```
ExchangeHostedService starts
  → Each enabled price service registered and started
  → Service opens WebSocket to exchange
  → On price update:
      → In-memory dictionary updated (symbol → price)
      → Periodic snapshot written to DB price table (e.g. BinanceAssetPrice)
```

## Notes
- Prices are cached in-memory for fast reads by other services (e.g. `AveragePriceService`, performance tracking).
- If the WebSocket disconnects, the service attempts automatic reconnection with exponential backoff.
- `DisabledExchangeService` is substituted when an exchange is toggled off via the Exchanges admin page.

## Dependencies
- `AutoSignalsDbContext` — periodic price snapshot persistence
- `AdminSettingService` — checks exchange enabled status
