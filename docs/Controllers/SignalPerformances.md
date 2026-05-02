# SignalPerformancesController

**Authorization:** Admin role only

## Overview
The `SignalPerformancesController` provides admin access to the raw signal performance tracking records. Performance records are created automatically by `SignalPerformanceService` when price targets (take-profits or stop-losses) are hit.

## Actions

### Index (`GET`)
Lists all signal performance records with filters by provider, status (TP1/TP2/TP3/SL hit), and date range.

### Details
Shows performance detail for a single signal: entry, TPs hit, SL, time-to-close, and final outcome.

### Create / Edit / Delete
Manual CRUD access for admins to correct or supplement performance data.

## Dependencies
- `AutoSignalsDbContext` — reads `SignalPerformances` table
