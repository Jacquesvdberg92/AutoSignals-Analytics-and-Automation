# Automation — Theoretical Overview

This page summarizes how signal automation is implemented in the AutoSignals codebase and explains the responsibilities and interactions of the main components. It references three core source files:

- `Models/ProviderSettings.cs`
- `Services/OrderService.cs`
- `Services/UserOrderWatchDogService.cs`

The goal: turn incoming trading signals into exchange orders, manage their lifecycle (entry, DCAs, stoploss, take-profits and moonbag), and keep positions and orders consistent in the database while interacting with exchange APIs.

---

## High-level flow

1. A signal arrives (external feed). A `Signal` object (not shown here) describes `Symbol`, `Entry`, `Side`, `TakeProfits`, `Leverage`, etc.
2. `OrderService` is responsible for creating database orders for active subscribers. It:
   - Queries active users and their `ProviderSettings` (`Models/ProviderSettings.cs`).
   - Computes per-user trade size and leverages using market precisions and user settings.
   - Creates entry orders (initial + DCA), a stoploss order and take-profit orders (including optional moonbag) and persists them to the DB.
3. `UserOrderWatchDogService` runs continuously (background worker). It:
   - Periodically fetches latest market prices for symbols with open orders.
   - Evaluates open orders against current prices and decides whether they should execute.
   - For orders that should execute, it calls exchange-specific services (e.g. Bitget/Okx) to place/close orders on exchanges.
   - Updates order and position entities in the DB, handles concurrency, and performs error logging.

---

## `ProviderSettings` (user-level automation preferences)

- File: `Models/ProviderSettings.cs`
- Purpose: stores per-user automation preferences and limits used during order creation.
- Key settings:
  - `IsEnabled`, `Testing` — enable automation and test mode flags.
  - `OverideLeverage`, `Leverage` — whether to override provider signal leverage and what leverage to use.
  - `IgnorLong`, `IgnorShort` — ignore certain signal sides.
  - `UseStoploss`, `IgnoreStoploss`, `StoplossPercentage`, `MoveStoploss` — stoploss preferences and behaviour.
  - `TpCount`, `TpPercentages` — how take-profits should be sized.
  - `RiskPercentage`, `MaxTradeSizeUsd`, `MinTradeSizeUsd` — risk and trade-size boundaries.
  - `IsIsolated`, `UseMoonbag`, `MoonbagPercentage` — margin and moonbag options.

`OrderService` reads these settings for each user and uses them to:
- Decide whether to skip a signal (e.g. `IgnorLong`)
- Compute `stoploss` value and trade sizes
- Create orders with correct `IsTest`, `IsIsolated`, `Leverage`, `Size`, `Description` and `Time` fields

---

## `OrderService` responsibilities

- Entry point for turning a `Signal` into persistent `Order` rows for each eligible user.
- Major steps:
  - Validate market precisions for the signal symbol (`GetPrecisions`).
  - For each active user, fetch `ProviderSettings` and calculate trade sizes via `CalculateTradeSize`.
  - Create three entry orders (initial, DCA1, DCA2) with sizes split (50/20/30) and DCA prices derived relative to `stoploss`.
  - Create the stoploss order and take-profit orders using `CreateStoplossOrder` and `CreateTakeProfitOrders`.
  - Persist orders in a database transaction with retries on save.

Notes:
- `CalculateTradeSize` respects user `RiskPercentage` and clamps notional to `MinTradeSizeUsd` / `MaxTradeSizeUsd`.
- Precision for amounts and prices is computed from market precision metadata so created orders meet exchange constraints.
- `CreateTakeProfitOrders` supports `TpPercentages`, `MoveStoploss` (MSL), and optional `Moonbag` final order.

---

## `UserOrderWatchDogService` responsibilities

- Runs as a background service and executes automation duties at runtime.
- Major responsibilities:
  - Collect all open `Order` and `Position` rows that require monitoring.
  - Fetch latest prices with `FetchLatestPricesAsync` using available user exchange credentials and fallback to DB prices.
  - Decide whether an `Order` should execute (slippage tolerance and description-based rules) and call `ExecuteOrderAsync`.
  - `ExecuteOrderAsync` calls exchange-specific wrappers (e.g. `BitgetPriceService`, `OkxPriceService`) to place actual exchange orders.
  - Handle position creation/updating (`CreateOrUpdatePositionAsync`) and order/position closing flows (`CloseOrdersAndPositionAsync`, `UpdatePositionForTPAsync`, `HandleMSLAsync`).

Reliability and safety features:
- Concurrency handling and retry loops to mitigate `DbUpdateConcurrencyException`.
- Time-based cancellation: stale pending entry orders older than 24 hours are cancelled.
- Error logging: on API timeouts, failures, or exceptions it logs details and records errors via `ErrorLogService`.
- Rate-limiting and parallelism controls when fetching prices (using a `SemaphoreSlim`).

---

## Error handling, retries and consistency

- Database writes frequently use retry patterns and transactions to keep orders and positions consistent.
- `UserOrderWatchDogService` isolates scoped DB contexts for each operation to avoid long-running context locking.
- On exchange errors (e.g. insufficient balance or minimum size errors), the code cancels or closes related orders and logs diagnostics.

---

## Practical notes and extension points

- Exchanges are abstracted by per-exchange services (`BitgetPriceService`, `OkxPriceService`, etc.). New exchanges can be integrated by adding a service and hooking it into `OrderService`/`UserOrderWatchDogService` switch statements.
- Precision and market metadata must be kept up-to-date in tables like `BitgetMarkets` / `OkxMarkets` so `OrderService` creates valid orders.
- Tune `UserOrderWatchDogService` polling interval and parallel fetch concurrency to balance latency vs API rate limits.

---

This document is a theoretical overview. For implementation details, read the source files:
- `Models/ProviderSettings.cs`
- `Services/OrderService.cs`
- `Services/UserOrderWatchDogService.cs`

For questions about a specific method or behavior, reference the file and the function name in the repo before asking.