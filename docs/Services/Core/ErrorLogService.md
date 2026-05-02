# ErrorLogService

**Namespace:** `AutoSignals.Services`  
**Type:** Background service (`BackgroundService`)

## Overview
`ErrorLogService` provides non-blocking error logging. Callers enqueue error details into an in-memory channel; a background loop persists them to the `ErrorLog` table in batches. This prevents database latency from affecting the request pipeline during error conditions.

## Methods

| Method | Description |
|--------|-------------|
| `LogErrorAsync(message, stackTrace, source, additionalData)` | Enqueues an error record for async persistence. Returns immediately. |

## Flow
```
Exception caught anywhere in application
  → ErrorLogService.LogErrorAsync(ex.Message, ex.StackTrace, source)
  → Written to System.Threading.Channels.Channel<ErrorLog>
  → Background ExecuteAsync() loop reads channel
  → Batch written to ErrorLog table via EF Core
```

## Admin Access
Logs are viewable at `ErrorLogsController` → Index. Records include full stack trace and additional diagnostic context.

## Notes
- Non-blocking by design: `LogErrorAsync` never awaits DB writes.
- If the process crashes before flush, queued-but-unflushed errors are lost.
- Older logs can be manually pruned via the admin interface.

## Dependencies
- `AutoSignalsDbContext` — writes to `ErrorLog` table
