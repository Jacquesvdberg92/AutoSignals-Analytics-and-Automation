# PortfolioController

**Authorization:** Authenticated users (VIP/Pro features gated further)

## Overview
The `PortfolioController` manages the user's personal portfolio view. It aggregates positions, orders, and P&L data from connected exchanges and displays them in a unified dashboard.

## Actions

### Dashboard (`GET /Portfolio/Dashboard`)
The main portfolio view. Shows:
- Open positions across all connected exchanges
- Estimated liquidation prices
- Unrealized P&L
- Total portfolio value breakdown

### Positions / Orders
Sub-views listing position and order history with filtering by exchange and date range.

### Holdings Modal
Returns a partial view (used via AJAX) showing the holdings detail for a specific asset.

## Flow
```
User opens Portfolio Dashboard
  → ExchangeBalanceService fetches live balances from connected exchanges
  → Open positions loaded from database (synced by UserOrderWatchDogService)
  → Aggregated view rendered
```

## Dependencies
- `AutoSignalsDbContext` — reads positions, orders
- `ExchangeBalanceService` — live exchange balance data
- `ISubscriptionService` — checks feature access
