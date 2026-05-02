# SignalsController

**Authorization:** Public (read); some actions require authentication

## Overview
The `SignalsController` serves the main signal feed — the core product page where users browse incoming trading signals from all providers.

## Actions

### Index (`GET /Signals`)
Displays the live signal feed. Supports filtering by provider, exchange, direction (long/short), and date. Each signal card shows:
- Symbol and direction
- Entry price range
- Take-profit targets (TP1, TP2, TP3)
- Stop-loss
- Current status (Open, TP1 hit, SL hit, Closed)
- AI prediction summary (if available)

### Details (`GET /Signals/{id}`)
Full signal detail page. Shows complete signal data, performance history, and the AI-generated narrative prediction.

### Create / Edit / Delete
Admin-only CRUD for manually managing signal records.

## Dependencies
- `AutoSignalsDbContext` — reads `Signals`, `SignalPerformances`, `SignalPredictions`
- `ISubscriptionService` — gates advanced signal details for VIP users
