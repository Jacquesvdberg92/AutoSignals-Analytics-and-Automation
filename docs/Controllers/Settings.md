# SettingsController

**Authorization:** Authenticated users

## Overview
The `SettingsController` manages all user-configurable settings including notification preferences, exchange API key connections, and account options.

## Actions

### Index (`GET /Settings`)
Displays the user settings dashboard with tabs for notifications and exchange connections.

### UpdateNotificationSettings (`POST`)
Saves the user's notification preferences (Telegram alerts, email alerts, etc.) to `UserNotificationSettings`.

### Exchange Connections
- **AddConnection** (`POST`) — Encrypts and saves a new exchange API key pair using `AesEncryptionService`.
- **RemoveConnection** (`POST`) — Deletes an exchange connection record.
- **TestConnection** (`POST`) — Tests the saved API credentials by fetching the account balance.

### CopySettings (`POST`)
Internal tool that copies provider settings from one provider to another. Used for quick onboarding of new similar providers.

## Flow
```
User enters exchange API key + secret
  → AesEncryptionService encrypts key and secret
  → UserExchangeConnection saved to database
  → ExchangeOrderAdapterFactory can now use connection for trading
```

## Dependencies
- `AutoSignalsDbContext` — user exchange connections, notification settings
- `AesEncryptionService` — encrypts API credentials at rest
- `ExchangeOrderAdapterFactory` — tests connection via live balance fetch
