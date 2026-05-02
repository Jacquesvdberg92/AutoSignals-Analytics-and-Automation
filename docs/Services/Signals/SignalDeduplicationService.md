# SignalDeduplicationService

**Namespace:** `AutoSignals.Services.Signals`  
**Type:** Scoped service

## Overview
`SignalDeduplicationService` prevents the same signal from being saved multiple times. This can happen when a Telegram channel resends or edits a message, or when the scanner reconnects and reprocesses recent messages.

## Methods

| Method | Description |
|--------|-------------|
| `IsDuplicateAsync(signal)` | Returns `true` if a sufficiently similar signal already exists in the database within the deduplication window. |
| `GetStatsAsync()` | Returns deduplication statistics (total groups, symbols, signals processed). |

## Deduplication Logic
A signal is considered a duplicate if within the last **24 hours** there already exists a signal with the same:
- `ProviderId`
- `Symbol`
- `Side` (long/short)
- Entry price within ±0.5% tolerance

## Flow
```
New signal parsed from Telegram message
  → SignalDeduplicationService.IsDuplicateAsync(signal)
  → Query last 24h signals for same provider + symbol + side
  → Check entry price proximity
  → If duplicate → discard signal, log dedup event
  → If unique → proceed to save signal
```

## Notes
- The 24-hour window and price tolerance are internal constants — adjust in source if needed.
- Deduplication stats are accessible via the `DeduplicationStats` inner class.

## Dependencies
- `AutoSignalsDbContext` — queries recent signals
