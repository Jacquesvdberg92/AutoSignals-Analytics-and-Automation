# CandleService

**Namespace:** `AutoSignals.Services.Exchange`  
**Type:** Scoped service

## Overview
`CandleService` provides access to OHLCV (Open/High/Low/Close/Volume) candlestick data stored in the `KLineAssetPrices` table. It is used to power the candle chart viewer in the Assets section.

## Methods

| Method | Description |
|--------|-------------|
| `GetCandlesAsync(symbol, interval, exchange, limit)` | Returns a list of `CandleDto` records for the requested symbol/interval/exchange combination. Most recent candles returned first. |

## Notes
- Data availability depends on what has been imported via `KlineHistoryImportService` or collected by the live price services.
- Requires `KlineChartsEnabled` admin setting to be `true` to be surfaced in the UI.

## Dependencies
- `AutoSignalsDbContext` — queries `KLineAssetPrices` table
