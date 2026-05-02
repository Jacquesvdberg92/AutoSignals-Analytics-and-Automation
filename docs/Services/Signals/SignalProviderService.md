# SignalProviderService

**Namespace:** `AutoSignals.Services.Signals`  
**Type:** Scoped service

## Overview
`SignalProviderService` calculates and maintains the aggregate statistics displayed on each provider's card — win rate, TP distribution, average timeframe, and trade style tags. It also seeds default settings for new users.

## Methods

| Method | Description |
|--------|-------------|
| `CalculateTakeProfitDistrobution(performances)` | Returns a list of integers representing how many signals hit TP1, TP2, TP3 respectively. |
| `CalculateAverageTimeframe(performances)` | Computes the mean time (in hours/days) between signal open and close across all closed signals. |
| `AssignTradeStyleTags(averageTimeframe)` | Maps the average timeframe to human-readable tags: `Scalper`, `Day Trader`, `Swing Trader`, `Position Trader`. |
| `CalculateAverageTakeProfitPercentagePerTP(signals)` | Calculates the mean % gain from entry to each TP level. |
| `CalculateAndInsertProviderDataAsync()` | Runs all calculations for all providers and persists updated stats to `SignalProviders` table. |
| `CreateDefaultProviderSettingsForUsers()` | Seeds a default `ProviderSettings` record for any user that doesn't yet have one for each active provider. |

## Flow
```
Scheduled or triggered recalculation:
  → CalculateAndInsertProviderDataAsync()
  → For each active SignalProvider:
      → Load closed SignalPerformances
      → CalculateTakeProfitDistrobution()
      → CalculateAverageTimeframe()
      → AssignTradeStyleTags()
      → CalculateAverageTakeProfitPercentagePerTP()
      → Update SignalProvider record with new stats
```

## Dependencies
- `AutoSignalsDbContext` — signals, performances, providers, provider settings
