# VipDashboard

**Authorization:** VIP, Pro, Tester, or Admin role

## Overview
The `VipDashboard` controller serves the VIP member dashboard — the primary experience for paying subscribers. It aggregates signal performance, portfolio data, and exchange positions in one view.

## Actions

### Index (`GET /VipDashboard`)
Main VIP dashboard. Displays:
- Recent signals with live performance indicators
- Portfolio summary (total value, positions, unrealised P&L)
- Provider performance leaderboard
- Quick links to trading bots

## Dependencies
- `AutoSignalsDbContext` — signals, positions, orders
- `ExchangeBalanceService` — live balance data
- `ISubscriptionService` — tier/feature verification
