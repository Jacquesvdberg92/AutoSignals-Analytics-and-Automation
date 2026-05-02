# OrderTestingController

**Authorization:** Admin role only

## Overview
The `OrderTestingController` provides a sandboxed interface for admins to test order placement on connected exchanges without relying on live signal data. It is used to verify that exchange API credentials and order adapters are working correctly.

## Actions

### Index (`GET`)
Displays the order testing form. Allows selection of exchange, symbol, direction (long/short), order type, and quantity.

### PlaceTestOrder (`POST`)
Submits a test order through `OrderService`. The result (fill price, status, error) is displayed on the page.

### TestSequence (`POST`)
Runs a full order sequence test: open position → set stop-loss/take-profits → close position. Useful for end-to-end adapter validation.

## Flow
```
Admin selects exchange + parameters
  → POST to PlaceTestOrder
  → ExchangeOrderAdapterFactory selects correct adapter
  → Adapter places order on exchange
  → Result returned and displayed
```

## Dependencies
- `OrderService` — routes order through the adapter layer
- `ExchangeOrderAdapterFactory` — selects exchange-specific adapter
- `AutoSignalsDbContext` — reads user exchange connections
