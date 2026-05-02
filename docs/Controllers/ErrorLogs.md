# ErrorLogsController

**Authorization:** Admin role only

## Overview
The `ErrorLogsController` provides the admin interface for viewing application error logs. Errors are captured throughout the application and persisted asynchronously by `ErrorLogService`.

## Actions

### Index (`GET`)
Displays a paginated list of error log entries, showing message, source, stack trace (truncated), and timestamp. Newest errors appear first.

### Details (`GET`)
Shows full detail for a single error log record including the complete stack trace and any additional diagnostic data.

### Delete (`POST`)
Removes a specific error log entry. Also supports bulk-clearing old logs.

## Flow
```
Exception or error occurs in application
  → ErrorLogService.LogErrorAsync(...) called
  → Error queued and flushed to ErrorLog table in background
  → Admin reviews logs here and takes action
```

## Dependencies
- `AutoSignalsDbContext` — reads `ErrorLog` table
