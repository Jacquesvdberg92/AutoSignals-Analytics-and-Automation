# GridBotController

**Authorization:** VIP or Admin role  
**Route prefix:** `/GridBot`

## Overview
Manages Grid trading bots. A Grid bot places a ladder of buy and sell limit orders across a defined price range, profiting from price oscillation within that range.

## Actions

### Index (`GET /GridBot`)
Lists the user's grid bots with status, grid range (low/high), number of grid levels, and realised profit.

### Create (`GET/POST`)
Setup form for a new Grid bot:
- Exchange, symbol, and direction
- Lower and upper price bounds
- Number of grid levels (determines spacing between orders)
- Total investment amount

### Start / Stop (`POST`)
Activates or pauses the grid. On start, `GridBotEngine` calculates grid levels and places all initial limit orders on the exchange.

### Delete (`POST`)
Cancels all open grid orders on the exchange, then removes the bot record.

## Execution Flow
```
User configures grid range + levels
  → GridBotEngine.InitialiseAsync()
      → Calculates N evenly-spaced price levels
      → Places limit buy orders below current price
      → Places limit sell orders above current price
  → BotEngineHostedService polls for filled orders
  → On buy fill → place corresponding sell one level up
  → On sell fill → place corresponding buy one level down
  → Profit captured as spread on each round-trip
```

## Dependencies
- `AutoSignalsDbContext` — GridBot records, open order tracking
- `GridBotEngine` / `GridBotService` — grid logic
- `ExchangeOrderAdapterFactory` — exchange-specific order placement
- `BotEngineHostedService` — polling loop
