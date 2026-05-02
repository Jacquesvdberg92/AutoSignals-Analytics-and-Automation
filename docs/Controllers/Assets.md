# AssetsController

**Authorization:** Public (some actions require authentication)

## Overview
The `AssetsController` handles all views related to crypto assets — prices, market data, candle charts, and the asset dashboard. It aggregates price data from multiple exchanges.

## Actions

### Dashboard (`GET /Assets/Dashboard`)
The main asset browser. Displays all tracked tokens and coins across supported exchanges with their current prices, 24h change, and market context.

### Candles (`GET /Assets/Candles`)
Displays the OHLCV (Open/High/Low/Close/Volume) candle chart viewer. Requires `KlineChartsEnabled` admin setting to be active.

### Index
Lists all general asset price records in the database.

### Privacy / Error
Standard ASP.NET scaffolded pages.

## Dependencies
- `AutoSignalsDbContext` — reads asset price tables (Binance, Bybit, OKX, KuCoin, Bitget)
- `AdminSettingService` — checks `KlineChartsEnabled` feature flag
