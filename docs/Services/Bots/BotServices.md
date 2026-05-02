# Trading Bot Services

**Namespace:** `AutoSignals.Services.Bots`

## Overview
The bot infrastructure is built around a shared engine/service pattern. Each bot type has an **Engine** (pure business logic) and a **Service** (state management + DB interaction), all coordinated by shared hosting infrastructure.

---

## BotEngineHostedService
**Type:** Background service (`BackgroundService`)

The central scheduler for all trading bots. Runs a polling loop (every ~60 seconds) and triggers execution for all registered bot engines that are due to run.

### Flow
```
Every 60 seconds:
  → For each IBotEngine in BotEngineRegistry:
      → engine.ShouldRunAsync() → check if interval elapsed
      → If yes → engine.ExecuteAsync()
      → Update last-run timestamp
```

---

## BotEngineRegistry
**Type:** Singleton

Maintains the active set of `IBotEngine` instances. Bot engines are registered here when users start bots and removed when they stop them.

---

## DcaBotEngine / DcaBotService
**Type:** Scoped (service) + registered engine

Implements the DCA (Dollar Cost Averaging) execution logic.

- `DcaBotService` — loads/saves `DcaBot` records, manages state
- `DcaBotEngine` — checks interval, calls `OrderService.PlaceMarketOrderAsync()`, updates step count and average entry

---

## GridBotEngine / GridBotService
**Type:** Scoped (service) + registered engine

Implements grid trading logic.

- `GridBotService` — loads/saves `GridBot` records, manages grid state
- `GridBotEngine` — on init: places full grid of limit orders; on poll: checks for fills and replenishes the grid

---

## ArbitrageScannerEngine / ArbitrageScannerService
**Type:** Scoped (service) + registered engine

Implements cross-exchange arbitrage opportunity detection.

- `ArbitrageScannerService` — manages `ArbitrageScannerBot` records
- `ArbitrageScannerEngine` — polls prices across exchanges, computes spreads, records `ArbitrageOpportunity` when threshold exceeded

---

## Interfaces

| Interface | Description |
|-----------|-------------|
| `IBotEngine` | `ShouldRunAsync()`, `ExecuteAsync()`, `BotId` |
| `IBotService` | `GetBotAsync()`, `UpdateBotAsync()`, `SetStatusAsync()` |

## Dependencies
- `OrderService` — order placement (DCA + Grid)
- `ExchangeOrderAdapterFactory` — exchange clients (Grid, Arbitrage)
- Exchange price services — live prices (Arbitrage)
- `AutoSignalsDbContext` — all bot records
