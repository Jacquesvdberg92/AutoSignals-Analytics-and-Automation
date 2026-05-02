# AnalyticsService

**Namespace:** `AutoSignals.Services`  
**Type:** Hosted service (`IHostedService`) + Singleton (`IAnalyticsService`)

## Overview
`AnalyticsService` is a lightweight, in-process analytics counter that tracks page views and feature usage events. Counts are held in memory and periodically flushed to the `Analytics` database table to avoid per-request DB writes.

## Interface: `IAnalyticsService`

| Method | Description |
|--------|-------------|
| `Increment(eventName)` | Increments the in-memory counter for the named event. Thread-safe. |

## Flow
```
Controller action fires
  → _analyticsService.Increment("Landing Page")
  → In-memory ConcurrentDictionary["Landing Page"]++

Background flush (every ~60 seconds):
  → For each key in dictionary
  → Upsert into Analytics table (increment existing count or insert new row)
  → Clear in-memory buffer
```

## Usage Example
```csharp
// In a controller
_analyticsService.Increment("Pricing Page");
```

## Notes
- Counts survive brief restarts only via the DB flush. Counts in-memory since last flush are lost on crash.
- The flush interval is configured internally (default ~60s).
- Admin can view counts in `AnalyticsController`.

## Dependencies
- `AutoSignalsDbContext` — writes to `Analytics` table
