# VisitTrackingService

**Namespace:** `AutoSignals.Services`  
**Type:** Background service (`BackgroundService`)

## Overview
`VisitTrackingService` records authenticated user visits to the platform. It works in tandem with `VisitTrackingMiddleware`, which enqueues visit events. The background service dequeues and persists them asynchronously to avoid blocking requests.

## Flow
```
Authenticated user makes a request
  → VisitTrackingMiddleware intercepts
  → Enqueues (userId, timestamp, path) into in-memory channel
  → VisitTrackingService background loop reads channel
  → Writes UserVisit record to database
      → Deduplicates: only one visit recorded per user per day
```

## Notes
- Visit data is viewable per-user in `UsersDataController` → Details.
- Daily deduplication prevents inflated visit counts from single-session page loads.
- Unauthenticated visitors are not tracked (only logged-in users).

## Dependencies
- `AutoSignalsDbContext` — writes `UserVisits` table
- `VisitTrackingMiddleware` — request-pipeline integration
