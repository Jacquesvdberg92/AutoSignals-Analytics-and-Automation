# SignalProviderController

**Authorization:** Authenticated (VIP/Pro gated for full access)

## Overview
The `SignalProviderController` provides the per-provider dashboard experience for VIP users. It shows detailed signal feeds, performance charts, and statistics for a single selected provider.

## Actions

### Index (`GET /SignalProvider/{id}`)
Provider landing page. Overview stats, recent signals, performance summary.

### Dashboard (`GET /SignalProvider/{id}/Dashboard`)
Full dashboard with:
- TP distribution bar chart
- Win rate timeline
- Recent signal cards with status badges
- Performance image renders

## Dependencies
- `AutoSignalsDbContext` — signals, performances, provider settings
- `SignalProviderService` — statistical calculations
- `SignalPerformanceService` — performance image rendering
