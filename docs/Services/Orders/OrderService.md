# OrderService

**Namespace:** `AutoSignals.Services.Orders`  
**Type:** Scoped service

## Overview
`OrderService` is the central service for placing trades on connected exchanges. It resolves the correct exchange adapter for a user's connection and executes orders, recording results to the database.

## Methods

| Method | Description |
|--------|-------------|
| `PlaceOrderAsync(userId, signal, connectionId, ct)` | Places a market or limit order based on the signal parameters using the user's specified exchange connection. Returns an `ExchangeOrderResult`. |
| `PlaceMarketOrderAsync(userId, symbol, side, qty, connectionId, ct)` | Places a raw market order (used by bots). |
| `ClosePositionAsync(userId, symbol, connectionId, ct)` | Closes an open position at market price. |

## Flow
```
Signal received (manually or via bot trigger)
  → OrderService.PlaceOrderAsync(userId, signal, connectionId)
  → Load UserExchangeConnection
  → AesEncryptionService.Decrypt(apiKey/secret)
  → ExchangeOrderAdapterFactory.Create(exchange, credentials)
  → adapter.PlaceOrderAsync(symbol, side, qty, price)
  → Result: fill price, order ID, status
  → Order record saved to database
  → NotificationService.NotifyOrderExecutedAsync()
```

## Error Handling
- Exchange API errors are caught and returned as failed `ExchangeOrderResult` (not thrown).
- All errors are logged via `ErrorLogService`.

## Dependencies
- `AutoSignalsDbContext` — reads connections, writes orders
- `AesEncryptionService` — credential decryption
- `ExchangeOrderAdapterFactory` — exchange client creation
- `NotificationService` — post-execution notifications
- `ErrorLogService` — error logging
