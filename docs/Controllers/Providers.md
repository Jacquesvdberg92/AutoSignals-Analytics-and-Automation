# ProvidersController

**Authorization:** Public (read); Admin for management actions

## Overview
The `ProvidersController` serves the public signal provider listing — the main discovery page where users browse available signal channels. Admins can create, edit, and manage provider records.

## Actions

### Index (`GET /Providers`)
Displays all active signal providers with stats: win rate, average TP%, trade style tags, active signal count. Filters and sorting available.

### Details (`GET /Providers/{id}`)
Individual provider detail page. Shows full statistics, recent signals, TP distribution chart, and performance history.

### Create / Edit / Delete
Admin-only CRUD for managing provider records including name, exchange, Telegram channel ID, parsing configuration, and display settings.

### BulkUpdateSettings (`POST`)
Admin bulk-update of settings across multiple providers (e.g., enabling AI fallback for all providers at once).

## Dependencies
- `AutoSignalsDbContext` — provider, signal, and performance data
- `SignalProviderService` — calculates provider statistics
