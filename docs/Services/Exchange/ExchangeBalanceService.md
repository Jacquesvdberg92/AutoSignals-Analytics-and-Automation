# ExchangeBalanceService

**Namespace:** `AutoSignals.Services.Exchange`  
**Type:** Scoped service

## Overview
`ExchangeBalanceService` fetches live account balances from connected exchanges using the user's stored API credentials. Results are used in the Portfolio dashboard.

## Methods

| Method | Description |
|--------|-------------|
| `GetBalancesAsync(userId)` | Returns a list of `AssetBalance` records (asset, free, locked) across all of the user's connected exchanges. |

## Flow
```
PortfolioController requests balances
  → ExchangeBalanceService.GetBalancesAsync(userId)
  → Loads UserExchangeConnections for user
  → For each connection:
      → AesEncryptionService.Decrypt(apiKey / apiSecret)
      → ExchangeOrderAdapterFactory.Create(exchange, credentials)
      → adapter.GetBalancesAsync()
  → Results merged and returned
```

## Notes
- API calls are made per-request (not cached). Consider adding short TTL caching for high-traffic scenarios.
- Connections with invalid credentials are skipped with error logged.

## Dependencies
- `AutoSignalsDbContext` — reads `UserExchangeConnections`
- `AesEncryptionService` — decrypts API credentials
- `ExchangeOrderAdapterFactory` — creates exchange clients
