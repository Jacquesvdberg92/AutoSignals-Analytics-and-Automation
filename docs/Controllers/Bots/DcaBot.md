# DcaBotController

**Authorization:** VIP or Admin role  
**Route prefix:** `/DcaBot`

## Overview
Manages Dollar Cost Averaging (DCA) trading bots. A DCA bot automatically places repeat buy/sell orders for a chosen asset at a set interval, smoothing out entry price over time.

## Actions

### Index (`GET /DcaBot`)
Lists all of the user's DCA bot configurations with current status (Running / Stopped), total invested, average entry price, and unrealised P&L.

### Create (`GET/POST`)
Configuration wizard for a new DCA bot:
- Exchange and symbol selection
- Order interval (hourly / daily / weekly)
- Per-step order size
- Optional maximum investment cap or step count

### Start / Stop (`POST`)
Toggles execution state. Active bots are picked up by `BotEngineHostedService` on its next tick.

### Delete (`POST`)
Permanently removes a DCA bot and its execution history.

## Execution Flow
```
User creates DCA bot
  → Saved to DcaBot table with Status = Stopped
  → User clicks Start → Status = Running
  → BotEngineHostedService ticks every minute
  → DcaBotEngine.ShouldRunAsync() checks interval elapsed
  → DcaBotEngine.ExecuteAsync()
      → OrderService.PlaceMarketOrderAsync()
      → Step recorded, average entry recalculated
```

## Dependencies
- `AutoSignalsDbContext` — DCA bot + order records
- `DcaBotEngine` / `DcaBotService` — execution logic
- `OrderService` — order placement
- `BotEngineHostedService` — scheduling
