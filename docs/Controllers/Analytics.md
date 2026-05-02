# AnalyticsController

**Authorization:** Admin role only

## Overview
The `AnalyticsController` provides the admin interface for viewing platform analytics data. It displays page-view counters and event hit counts collected by `AnalyticsService`.

## Actions

### Index (`GET`)
Lists all tracked analytics events with their hit counts, ordered by most frequent. Provides a quick overview of which pages and features are being used most.

### Create / Edit / Delete
Standard CRUD scaffolding for analytics records. Allows admins to manually manage or clean up analytics entries.

## Flow
```
User visits a page
  → AnalyticsService.Increment("Event Name") is called in the relevant controller
  → Count is batched and flushed to the Analytics table periodically
  → Admin views aggregated counts here
```

## Dependencies
- `AutoSignalsDbContext` — reads/writes `Analytics` table
