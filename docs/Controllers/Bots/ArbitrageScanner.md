# ArbitrageScannerController

**Authorization:** VIP or Admin role  
**Route prefix:** `/ArbitrageScanner`

## Overview
Manages the Arbitrage Scanner bot. The scanner continuously monitors price spreads for the same asset across multiple connected exchanges and surfaces profitable arbitrage opportunities.

## Actions

### Index (`GET /ArbitrageScanner`)
Dashboard showing:
- Currently monitored asset pairs
- Live spread table across exchanges
- Detected opportunities with estimated profit % after fees
- Scanner running/stopped status

### Start / Stop (`POST`)
Starts or stops the `ArbitrageScannerEngine` background loop.

### Configure (`GET/POST`)
Allows the user to select which assets and exchanges to monitor, and set a minimum spread threshold for alerts.

## Execution Flow
```
Scanner Started
  → ArbitrageScannerEngine.ExecuteAsync() loop begins
  → For each monitored symbol:
      → Fetch best bid/ask from Exchange A, B, C...
      → Calculate spread: (ask_low - bid_high) / ask_low * 100
      → If spread > threshold → record ArbitrageOpportunity
  → Results surfaced on Index page in near-real-time
```

## Dependencies
- `AutoSignalsDbContext` — ArbitrageScannerBot + ArbitrageOpportunity records
- `ArbitrageScannerEngine` / `ArbitrageScannerService`
- Exchange price services (Binance, Bybit, OKX, etc.)
- `BotEngineHostedService`
