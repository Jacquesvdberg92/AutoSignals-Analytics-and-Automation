# NotificationService

**Namespace:** `AutoSignals.Services`  
**Type:** Scoped service (implements `INotificationService`)

## Overview
`NotificationService` is the central dispatcher for user-facing notifications. When an order is executed, this service determines which delivery channels (Telegram, email) the user has enabled and dispatches the notification via the appropriate providers.

## Methods

| Method | Description |
|--------|-------------|
| `NotifyOrderExecutedAsync(userId, order, cancellationToken)` | Looks up the user's notification preferences and dispatches an order-executed notification via all enabled channels. |

## Flow
```
OrderService executes an order
  → NotificationService.NotifyOrderExecutedAsync(userId, order)
  → Load UserNotificationSettings for user
  → If TelegramEnabled:
      → ITelegramNotifier.NotifyUserAsync(userId, order)
  → If EmailEnabled:
      → EmailSender.SendEmailAsync(user.Email, subject, body)
```

## Dependencies
- `AutoSignalsDbContext` — reads `UserNotificationSettings`
- `ITelegramNotifier` — Telegram delivery
- `IEmailSender` — email delivery
- `UserManager<IdentityUser>` — user email lookup
