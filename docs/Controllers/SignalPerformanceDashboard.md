# SignalPerformanceDashboardController

**Route:** `/Admin/SignalPerformanceDashboard`  
**Authorization:** `Admin` role required  
**Response Cache:** 30 seconds

## Overview

Read-only admin dashboard providing a high-level view of `SignalPerformanceService` health: win/loss rates, TP hit rates, provider comparisons, and signal tracking status. All queries use `AsNoTracking()`. The page auto-refreshes every 60 seconds via JavaScript.

---

## Actions

### `GET /Admin/SignalPerformanceDashboard`

Queries the database, calculates all metrics in memory, maps to `SignalPerformanceDashboardViewModel`, then renders `Views/Admin/SignalPerformanceDashboard.cshtml`.

#### Data Sources

| Section | Tables Queried |
|---------|---------------|
| Performance tracking | `SignalPerformances` |
| Signal metadata | `Signals` (Id, Symbol, Provider, TakeProfits) |
| Service errors | `ErrorLogs` (Source contains `SignalPerformanceService`) |

---

## ViewModel: `SignalPerformanceDashboardViewModel`

### Tracking Health

| Property | Type | Description |
|----------|------|-------------|
| `TotalPending` | `int` | `Status = "Pending"` |
| `TotalOpen` | `int` | `Status = "Open"` |
| `ClosedToday` | `int` | `Status = "Closed"` with `EndTime` today |
| `CancelledToday` | `int` | `Status = "Canceled"` with `EndTime` today |
| `ServiceErrorCount24h` | `int` | ErrorLog entries for `SignalPerformanceService` (24h) |

### Win / Loss Rates (all-time, closed signals only)

| Property | Type | Description |
|----------|------|-------------|
| `TotalClosed` | `int` | Total closed signal performances |
| `TotalWins` | `int` | `Notes = "All Take Profits Achieved"` |
| `TotalLosses` | `int` | `Notes = "Stoploss Hit"` |
| `TotalPartialWins` | `int` | Closed with ≥1 TP but not all TPs achieved |
| `WinRate` | `double` | `Wins / Closed × 100` |
| `LossRate` | `double` | `Losses / Closed × 100` |
| `PartialWinRate` | `double` | `PartialWins / Closed × 100` |
| `AvgTpsAchieved` | `double` | Mean `TakeProfitsAchieved / TakeProfitCount` |
| `AvgProfitOnWins` | `double` | Mean `ProfitLoss` for winning signals |
| `AvgLossOnLosses` | `double` | Mean `ProfitLoss` for losing signals |

### Take Profit Hit Rates

| Property | Description |
|----------|-------------|
| `Tp1HitRate` | % of tracked signals that achieved TP1 |
| `Tp2HitRate` | % of tracked signals that achieved TP2 |
| `Tp3HitRate` | % of tracked signals that achieved TP3 |
| `Tp4HitRate` | % of tracked signals that achieved TP4 |
| `AvgDurationToCloseHours` | Mean hours from `StartTime` to `EndTime` for closed signals |

### Provider Breakdown (`List<ProviderPerformanceStat>`)

Grouped by `Signals.Provider`, ordered by total signal count descending.

| Property | Description |
|----------|-------------|
| `Provider` | Provider name |
| `Total` | Total signal performances |
| `Wins` | All-TPs-achieved closures |
| `Losses` | Stoploss-hit closures |
| `Open` | Currently open |
| `Cancelled` | Never reached entry |
| `WinRate` | `Wins / (Wins + Losses) × 100` |
| `AvgProfit` | Mean `ProfitLoss` for wins |
| `AvgLoss` | Mean `ProfitLoss` for losses |
| `AvgTpsAchieved` | Mean TP completion ratio |

### Symbol Breakdown (`List<SymbolPerformanceStat>`)

Top 20 symbols by signal count.

| Property | Description |
|----------|-------------|
| `Symbol` | Asset symbol |
| `Total` | Total signal performances |
| `Wins` | Win count |
| `WinRate` | Win rate % |
| `AvgPl` | Average P/L % across all closed performances |

### Chart Data

| Property | Chart Type | Description |
|----------|-----------|-------------|
| `OutcomePieLabels/Values` | Donut | All TPs Hit / Stoploss / Partial / Cancelled / Open |
| `DailyLabels/OpenedValues/ClosedValues` | Grouped Bar | Signals opened vs closed per day (30 days) |
| `PlHistogramLabels/Values` | Bar | P/L distribution buckets |
| `ProviderWinRateLabels/Values` | Vertical Bar | Top 15 providers by win rate (min 3 closed signals) |

---

## Visual Alert Thresholds

| Condition | Indicator |
|-----------|-----------|
| `WinRate < 40%` | KPI card turns red (`bg-danger`) |
| `ServiceErrorCount24h > 5` | KPI card turns red (`bg-danger`) |
| `CancelledToday > 10` | Today's Summary badge turns amber |
| Provider win rate badge | Green ≥50%, amber ≥35%, red <35% |
| Symbol win rate badge | Same colour thresholds as provider |

---

## Supporting Classes (`ViewModels/Admin/SignalPerformanceDashboardViewModel.cs`)

- `ProviderPerformanceStat` – full provider metrics row
- `SymbolPerformanceStat` – symbol summary row
