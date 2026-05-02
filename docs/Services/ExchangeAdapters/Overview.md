# Exchange Order Adapters

**Namespace:** `AutoSignals.Services.ExchangeAdapters`

## Overview
Exchange order adapters provide a unified `IExchangeOrderAdapter` interface over each supported exchange's trading API. This abstraction allows `OrderService`, `ExchangeBalanceService`, and bot engines to work with any exchange without exchange-specific code.

---

## Interface: `IExchangeOrderAdapter`

| Method | Description |
|--------|-------------|
| `PlaceOrderAsync(symbol, side, type, qty, price, ct)` | Places a market or limit order. Returns `ExchangeOrderResult`. |
| `CancelOrderAsync(orderId, symbol, ct)` | Cancels an open order. |
| `GetOrderStatusAsync(orderId, symbol, ct)` | Returns current status and fill details for an order. |
| `GetOpenOrdersAsync(symbol, ct)` | Lists all open orders for a symbol. |
| `GetBalancesAsync(ct)` | Returns all account balances as `AssetBalance` list. |
| `GetPositionsAsync(ct)` | Returns open futures positions. |

---

## Implementations

| Adapter | Exchange | Notes |
|---------|----------|-------|
| `BinanceOrderAdapter` | Binance Futures | Uses Binance.Net SDK |
| `BybitOrderAdapter` | Bybit | Uses Bybit REST API |
| `OkxOrderAdapter` | OKX | Uses OKX REST API |
| `KuCoinOrderAdapter` | KuCoin | Uses KuCoin REST API |
| `BitgetOrderAdapter` | Bitget | Uses Bitget REST API |
| `CcxtExchangeOrderAdapterBase` | Generic CCXT base | Shared logic for CCXT-based adapters |

---

## Factory: `ExchangeOrderAdapterFactory`

| Method | Description |
|--------|-------------|
| `Create(exchange, credentials)` | Returns the correct `IExchangeOrderAdapter` implementation for the given exchange name and `ExchangeCredentials`. |

## Credentials Flow
```
UserExchangeConnection loaded from DB
  → AesEncryptionService.Decrypt(apiKey, apiSecret)
  → ExchangeCredentials { ApiKey, ApiSecret } constructed
  → ExchangeOrderAdapterFactory.Create("binance", credentials)
  → Returns BinanceOrderAdapter ready for use
```

## Result Types

- **`ExchangeOrderResult`** — fill price, order ID, status, error message
- **`OpenOrderResult`** — open order details (ID, symbol, side, qty, price)
- **`AssetBalance`** — asset name, free amount, locked amount
