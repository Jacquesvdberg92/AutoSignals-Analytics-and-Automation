# KlineHistoryImportService

**Namespace:** `AutoSignals.Services.Exchange`  
**Type:** Scoped service

## Overview
`KlineHistoryImportService` fetches historical OHLCV candlestick data from exchange REST APIs and persists them to the `KLineAssetPrices` table. Used by the admin Kline import tools to backfill historical data.

## Methods

| Method | Description |
|--------|-------------|
| `ImportAsync(exchange, symbol, interval, limit)` | Fetches up to `limit` historical candles for the given symbol/interval from the specified exchange REST API. Returns the number of new records inserted (skips duplicates). |

## Supported Exchanges

| Key | Exchange |
|-----|----------|
| `binance` | Binance Futures |
| `bybit` | Bybit |
| `okx` | OKX |
| `kucoin` | KuCoin |
| `bitget` | Bitget |

## Flow
```
Admin clicks Import in KlineSettings
  → KlineHistoryImportService.ImportAsync("binance", "BTCUSDT", "1h", 500)
  → REST call to exchange kline/candlestick endpoint
  → Parse response → list of KLineAssetPrice records
  → Bulk insert (skip duplicates by Time+Symbol+Type index)
  → Return count of new rows
```

## Notes
- Uses `ExchangeLabels` dictionary for display names in UI.
- Duplicate candles (same symbol + time + type) are silently skipped via DB unique index.

## Dependencies
- `AutoSignalsDbContext` — bulk inserts `KLineAssetPrices`
- `HttpClient` — exchange REST API calls
