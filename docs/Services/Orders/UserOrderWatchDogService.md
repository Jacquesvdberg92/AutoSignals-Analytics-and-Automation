# UserOrderWatchDogService

**Namespace:** `AutoSignals.Services.Orders`  
**Type:** Background service (`BackgroundService`)

## Overview
`UserOrderWatchDogService` monitors open orders and positions across all users' connected exchanges. It polls exchanges for order fill status, updates the database when orders complete, and syncs position data.

## Flow
```
Service wakes every ~30 seconds
  → Load all open Order records from database
  → Group by exchange connection
  → For each group:
      → ExchangeOrderAdapterFactory.Create(exchange, credentials)
      → adapter.GetOrderStatusAsync(orderId)
      → If filled → update Order.Status = Filled, record fill price
      → If cancelled → update Order.Status = Cancelled
  → Load all open Position records
  → For each position:
      → Sync unrealised P&L and estimated liquidation price
      → If position closed on exchange → mark closed in DB
```

## Notes
- Polling interval is a balance between API rate limits and data freshness.
- Exchange API rate limits are respected via per-exchange throttling.
- Failed polls (network errors, invalid credentials) are logged and skipped — not retried on same tick.

## Dependencies
- `AutoSignalsDbContext` — reads/writes orders and positions
- `ExchangeOrderAdapterFactory` — per-exchange clients
- `AesEncryptionService` — credential decryption
- `ErrorLogService` — error logging
