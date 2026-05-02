# AveragePriceService

**Namespace:** `AutoSignals.Services.Exchange`  
**Type:** Singleton service

## Overview
`AveragePriceService` computes a consolidated "average price" for an asset by aggregating live prices from all active exchange price services. This provides a single reference price independent of any one exchange.

## Methods

| Method | Description |
|--------|-------------|
| `GetAveragePriceAsync(symbol)` | Returns the mean price across all exchanges that have a known price for the symbol. Returns `null` if no exchange has the symbol. |

## Flow
```
Request for average price of "BTCUSDT"
  → Query BinancePriceService, BybitPriceService, OkxPriceService...
  → Collect all non-null prices
  → Return arithmetic mean
```

## Usage
- Signal performance tracking uses average price to evaluate whether a TP or SL has been reached.
- Displayed on asset detail pages as "Market Price".

## Dependencies
- All `IExchangeService` implementations (injected as `IEnumerable<IExchangeService>`)
