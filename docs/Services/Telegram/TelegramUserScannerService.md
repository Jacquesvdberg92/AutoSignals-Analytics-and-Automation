# TelegramUserScannerService

**Namespace:** `AutoSignals.Services.Telegram`  
**Type:** Background service (`BackgroundService`)

## Overview
`TelegramUserScannerService` operates as a **Telegram user-client** (not a bot). It authenticates as a real Telegram user account and joins private signal channels to monitor messages that a bot cannot access. It feeds all incoming messages to `TelegramMessageProcessorService`.

## Key Members

| Member | Description |
|--------|-------------|
| `AuthStatus` | Current authentication status string (shown in admin UI). |
| `ProvideVerificationCode(code)` | Called by the admin UI to submit the SMS/app verification code during login. |
| `ProvidePassword(password)` | Called by the admin UI to submit the 2FA password during login. |

## Authentication Flow
```
Admin opens /Admin/TelegramUserAuth
  → TelegramUserScannerService.AuthStatus shows current state
  → If not authenticated:
      → Enter phone number → service starts Telegram login flow
      → Telegram sends SMS code → Admin enters code via UI
      → If 2FA enabled → Admin enters password via UI
      → Session authenticated and persisted
  → Scanner connects and begins monitoring all configured channels
```

## Message Scanning Flow
```
User-client receives message in monitored channel
  → Identify which SignalProvider matches this channel (by TelegramChannelId)
  → If text message → TelegramMessageProcessorService.ProcessMessageAsync()
  → If photo → TelegramMessageProcessorService.ProcessImageMessageAsync()
```

## Configuration
- `TelegramUserClientOptions` — phone number, session storage path, API ID/hash (from my.telegram.org)

## Notes
- Requires a real Telegram account dedicated to scanning. Do not use a personal account.
- Session is persisted to disk so re-authentication is only needed when the session expires or is revoked.
- Channel list is driven by `SignalProvider.TelegramChannelId` in the database.

## Dependencies
- `TelegramUserClientOptions` — credentials + session config
- `TelegramMessageProcessorService` — message handling pipeline
- `AutoSignalsDbContext` — provider channel ID list
