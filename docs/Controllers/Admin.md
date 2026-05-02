# AdminController

**Route prefix:** `/Admin/`  
**Authorization:** Admin role only

## Overview
The `AdminController` is the central hub for platform administration. It manages system configuration, Kline (candlestick) data, payment tools, user tiers, subscription plans, and Telegram bot authentication. All actions are restricted to the `Admin` role.

## Actions

### KlineSettings (`GET/POST /Admin/KlineSettings`)
Displays and toggles the Kline (candlestick) data collection feature flag. Shows stats: row count, symbol count, oldest/newest snapshot timestamps.

### KlineImport (`POST /Admin/KlineImport`)
Triggers a historical kline import for a specific exchange, symbol, interval, and limit. Uses `KlineHistoryImportService`.

### KlineBulkImport (`GET/POST /Admin/KlineBulkImport`)
Manages bulk import of kline data across multiple symbols. Allows scheduling of background batch imports.

### KlineBulkImportStatus (`GET /Admin/KlineBulkImportStatus`)
Returns the current status of any in-progress bulk kline import job.

### TelegramUserAuth (`GET /Admin/TelegramUserAuth`)
Displays the Telegram user-client authentication flow. Used to authenticate the scanner bot with a Telegram phone number.

### ProvideVerificationCode / ProvidePassword
Step inputs for the Telegram user-client authentication process (code and 2FA password).

### NOWPaymentsAdmin (`GET /Admin/NOWPayments`)
Displays the NOWPayments admin panel for looking up and diagnosing specific payment records by payment ID.

### UserTier / TierOverride
Manages per-user subscription tier overrides. Allows admins to manually assign tiers to specific users.

### Plans
CRUD management of subscription plans displayed on the pricing page.

### SubscriptionEvents
Lists all subscription lifecycle events (activations, cancellations, expirations) for audit purposes.

## Dependencies
- `AdminSettingService` — feature flag key/value store
- `KlineHistoryImportService` — kline data import
- `AutoSignalsDbContext` — database access
- `ISubscriptionService` — subscription management
- `UserManager<IdentityUser>` — user lookup
