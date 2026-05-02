# ExchangeHostedService

**Namespace:** `AutoSignals.Services.Exchange`  
**Type:** Background service (`BackgroundService`)

## Overview
`ExchangeHostedService` is the orchestrator for all exchange price streaming services. It starts, monitors, and restarts individual exchange services (`BinancePriceService`, `BybitPriceService`, etc.) based on their enabled/disabled configuration.

## Flow
```
Application starts
  → ExchangeHostedService.ExecuteAsync()
  → Reads enabled exchanges from configuration/DB
  → Starts each enabled IExchangeService
  → Monitoring loop: every 30s checks IsRunning on each service
  → If a service has stopped unexpectedly → restart it
  → If an exchange is disabled mid-run → stop its service
```

## Notes
- Acts as a health supervisor for price stream services.
- Adding a new exchange requires implementing `IExchangeService` and registering it here.

## Dependencies
- All `IExchangeService` implementations (Binance, Bybit, OKX, KuCoin, Bitget)
- `AdminSettingService` — enabled/disabled state per exchange
