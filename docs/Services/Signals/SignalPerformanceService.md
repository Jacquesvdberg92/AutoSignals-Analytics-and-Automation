# SignalPerformanceService

**Namespace:** `AutoSignals.Services.Signals`  
**Type:** Scoped service

## Overview
`SignalPerformanceService` has two responsibilities:
1. **Performance tracking** — periodically checks open signals against current market prices to detect when take-profit or stop-loss levels are hit, and records the outcome.
2. **Image rendering** — generates performance summary images (bar charts, win-rate visuals) for display on provider pages.

## Methods

| Method | Description |
|--------|-------------|
| `TrackPerformance()` | Checks all open signals against current prices. Updates `SignalPerformance` records when TP/SL levels are hit. |
| `RenderSignalImageAsync(image)` | Renders a performance chart image for a signal or provider. Returns a `System.Drawing.Image`. |

## Performance Tracking Flow
```
Scheduled tick (every ~5 minutes via ExchangeHostedService or timer)
  → Load all signals with Status = Open
  → For each signal:
      → AveragePriceService.GetAveragePriceAsync(signal.Symbol)
      → Check if current price >= TP1 → mark TP1 hit, update performance
      → Check if current price >= TP2 → mark TP2 hit
      → Check if current price >= TP3 → mark TP3 hit (signal closed)
      → Check if current price <= SL → mark SL hit (signal closed)
  → Save updated SignalPerformance records
  → Trigger provider stats recalculation if any changes
```

## Notes
- Long and short directions use opposite comparison logic (short: price ≤ TP, price ≥ SL).
- Performance records drive the provider statistics displayed on provider cards.

## Dependencies
- `AutoSignalsDbContext` — reads signals, writes performances
- `AveragePriceService` — current market prices
- `SignalProviderService` — triggers stat recalculation
