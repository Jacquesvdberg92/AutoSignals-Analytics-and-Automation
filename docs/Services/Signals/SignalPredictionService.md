# SignalPredictionService

**Namespace:** `AutoSignals.Services.Signals`  
**Type:** Scoped service

## Overview
`SignalPredictionService` generates an AI-written narrative prediction for a newly received signal. The prediction provides context, market analysis, and a plain-English commentary on the signal's probable outcome. It is displayed on the signal detail page for VIP users.

## Methods

| Method | Description |
|--------|-------------|
| `GeneratePredictionAsync(signal, cancellationToken)` | Generates and returns a `SignalPrediction` for the given signal. Returns `null` if generation fails. |

## Prediction Input Context
The prompt includes:
- Signal symbol, direction, entry, TP levels, SL
- Provider name and historical win rate
- Recent price context from `AveragePriceService`
- Provider's recent performance summary

## Output: `SignalPrediction`
| Field | Description |
|-------|-------------|
| `NarrativeAnalysis` | Multi-paragraph AI commentary on the signal |
| `Confidence` | Model's self-assessed confidence |
| `Symbol`, `Side`, `Provider` | Signal context for display |
| `Status` | Last known signal status at time of generation |

## Flow
```
New signal saved to database
  → SignalPredictionService.GeneratePredictionAsync(signal)
  → Build context prompt with signal data + provider stats
  → LLM API call
  → Parse response → SignalPrediction record
  → Save to SignalPredictions table
  → Displayed on /Signals/{id} for VIP users
```

## Dependencies
- LLM API client
- `AveragePriceService` — price context
- `AutoSignalsDbContext` — provider stats, signal storage
