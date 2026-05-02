# OrderWatchdogDashboardController

**Route:** `/Admin/OrderWatchdogDashboard`  
**Authorization:** `Admin` role required  
**Response Cache:** 30 seconds

## Overview

Read-only admin dashboard providing real-time visibility into the `UserOrderWatchDogService` order-execution engine. All data is queried with `AsNoTracking()` for performance. The page auto-refreshes every 60 seconds via JavaScript.

---

## Actions

### `GET /Admin/OrderWatchdogDashboard`

Queries the database and maps results to `OrderWatchdogDashboardViewModel`, then renders `Views/Admin/OrderWatchdogDashboard.cshtml`.

#### Data Sources

| Section | Tables Queried |
|---------|---------------|
| Pipeline Health | `Orders` |
| Position Health | `Positions` |
| Errors | `ErrorLogs` (Source contains `UserOrderWatchDogService`) |
| Liquidation count | `ErrorLogs` (Message contains `EstLiquidation`) |

---

## ViewModel: `OrderWatchdogDashboardViewModel`

### Pipeline Health

| Property | Type | Description |
|----------|------|-------------|
| `TotalOpenOrders` | `int` | Orders with `Status = "OPEN"` |
| `TotalPendingOrders` | `int` | Orders with `Status = "PENDING"` |
| `ExecutedLast24h` | `int` | Orders executed in the last 24 hours |
| `CancelledLast24h` | `int` | Orders cancelled in the last 24 hours |
| `AvgExecutionMinutes` | `double` | Average minutes from `Time` to `CloseTime` for executed orders |

### Execution Breakdown (last 24h)

| Property | Description |
|----------|-------------|
| `EntryOrdersOpen` | `Description = "Initial Entry Order"` + OPEN |
| `EntryOrdersExecuted24h` | Initial entry orders executed |
| `EntryOrdersCancelled24h` | Initial entry orders cancelled |
| `DcaOrdersOpen` | Description contains `"DCA"` + OPEN |
| `DcaOrdersExecuted24h` | DCA orders executed |
| `StoplossOrdersExecuted24h` | Stoploss / Stoploss On Entry executed |
| `TakeProfitOrdersExecuted24h` | Description contains `"Take Profit Order"` executed |
| `MslOrdersExecuted24h` | Description contains `"MSL"` executed |

### Error & Failure Metrics (last 24h)

| Property | Description |
|----------|-------------|
| `InsufficientBalanceCancellations24h` | `ExchangeOrderStatus = "40762"` |
| `MinSizeCancellations24h` | `ExchangeOrderStatus = "45110"` |
| `WatchdogErrorCount24h` | ErrorLog entries for `UserOrderWatchDogService` |
| `PriceFetchFailures24h` | ErrorLog source contains `FetchLatestPricesAsync` |

### Position Health

| Property | Description |
|----------|-------------|
| `TotalOpenPositions` | Positions with `Status = "OPEN"` |
| `PositionsClosedToday` | Positions closed since midnight UTC |
| `PositionsLiquidatedToday` | ErrorLog entries mentioning `EstLiquidation` today |
| `AvgOpenROI` | Mean `ROI` across open positions |
| `NegativeROIPositions` | Count of open positions with `ROI < 0` |
| `UniqueSymbolsTracked` | Distinct symbols from open orders |

### Chart Data

| Property | Chart Type | Description |
|----------|-----------|-------------|
| `ExecutedByHourLabels/Values` | Bar | Orders executed per hour (last 24h) |
| `StatusPieLabels/Values` | Donut | OPEN / PENDING / EXECUTED / CANCELLED counts |
| `ErrorByHourLabels/Values` | Area | WatchDog errors per hour (last 24h) |
| `RoiHistogramLabels/Values` | Bar | Open position ROI distribution buckets |

### Tables

| Property | Description |
|----------|-------------|
| `TopUsersByOpenOrders` | Top 10 users by open order count |
| `TopUsersByCancelledOrders` | Top 10 users by cancelled orders (24h) |
| `TopSymbolsByOpenOrders` | Top 10 symbols by open order count |
| `OpenPositions` | All open positions ordered by ROI ascending |

---

## Visual Alert Thresholds

| Condition | Indicator |
|-----------|-----------|
| `TotalOpenOrders > 50` | KPI card turns amber (`bg-warning`) |
| `WatchdogErrorCount24h > 5` | KPI card turns red (`bg-danger`) |
| Position `ROI < -80%` | Table row highlighted `table-danger` |
| Position `ROI < 0` | Table row highlighted `table-warning` |

---

## Supporting Classes (`ViewModels/Admin/OrderWatchdogDashboardViewModel.cs`)

- `UserOrderStat` – `UserId`, `UserName`, `Count`
- `SymbolOrderStat` – `Symbol`, `Count`
- `OpenPositionRow` – `Id`, `UserId`, `Symbol`, `Side`, `ROI`, `Entry`, `Time`, `IsTest`
