# TelegramBotService

**Namespace:** `AutoSignals.Services.Telegram`  
**Type:** Background service (`BackgroundService`) + `ITelegramNotifier`

## Overview
`TelegramBotService` runs the platform's Telegram bot. It serves two purposes:
1. **Outbound notifications** — sends order execution alerts and direct messages to users via their Telegram account.
2. **Group posts** — posts signal cards and performance updates to configured Telegram groups.

## Interface: `ITelegramNotifier`

| Method | Description |
|--------|-------------|
| `NotifyUserAsync(userId, order, ct)` | Sends an order-executed notification to the user's linked Telegram account. |
| `SendDirectMessageToUserAsync(userId, htmlText, ct)` | Sends a custom HTML-formatted message directly to a user. |
| `PostMessageToGroupAsync(groupId, text, ct)` | Posts a message to a configured Telegram group/channel. |
| `LoggError(message)` | Posts an error alert to the admin error group. |

## Flow
```
Order executed
  → NotificationService.NotifyOrderExecutedAsync()
  → TelegramBotService.NotifyUserAsync(userId, order)
  → Look up user's Telegram chat ID
  → Format order message as HTML
  → Telegram Bot API: sendMessage(chatId, html, parse_mode=HTML)
```

## Configuration
- `TelegramBotToken` — bot API token from @BotFather
- `TelegramGroupsOptions` — group/channel IDs for public posts

## Notes
- `DisabledTelegramNotifier` is substituted when Telegram is not configured, making all calls no-ops.
- The bot must have been started by each user (they must `/start` the bot) before messages can be delivered.

## Dependencies
- Telegram Bot API (HTTP)
- `AutoSignalsDbContext` — user Telegram chat ID lookup
- `TelegramGroupsOptions` — group configuration
