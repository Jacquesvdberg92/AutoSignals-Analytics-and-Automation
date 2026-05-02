# SubscriptionService

**Namespace:** `AutoSignals.Services.Subscriptions`  
**Type:** Scoped service (implements `ISubscriptionService`)

## Overview
`SubscriptionService` is the authoritative source for all subscription-related business logic. It manages tier assignment, trial lifecycle, feature access checks, and subscription activation/cancellation.

## Methods

| Method | Description |
|--------|-------------|
| `GetTierAsync(userId)` | Returns the user's current `SubscriptionTier` (Free, Trial, Pro, VIP). |
| `IsTrialActiveAsync(userId)` | Returns `true` if the user has an active, non-expired trial. |
| `CanAccessFeatureAsync(userId, feature)` | Checks whether the user's current tier grants access to a specific `SubscriptionFeature`. |
| `StartTrialAsync(userId)` | Starts a new trial for the user. Sets tier to Trial and records `TrialStartDate`. |
| `ActivateSubscriptionAsync(userId, tier, ...)` | Upgrades the user to the given tier with an expiry date. Logs a `SubscriptionEvent`. |
| `StageSubscriptionActivationAsync(userId, tier, ...)` | Creates a pending activation record (used before payment confirmation). |
| `CancelSubscriptionAsync(userId, reason)` | Cancels the current subscription. Logs the cancellation event and schedules tier downgrade. |

## Feature Access Matrix

| Feature | Free | Trial | Pro | VIP |
|---------|------|-------|-----|-----|
| Signal Feed | ✓ | ✓ | ✓ | ✓ |
| AI Predictions | — | ✓ | ✓ | ✓ |
| Portfolio | — | ✓ | ✓ | ✓ |
| Trading Bots | — | — | — | ✓ |
| Exchange Connections | — | ✓ | ✓ | ✓ |

## Flow
```
User completes payment (webhook received)
  → NOWPaymentsWebhookService resolves staged activation
  → SubscriptionService.ActivateSubscriptionAsync(userId, tier, expiryDate)
  → UserData.SubscriptionTier updated
  → SubscriptionEvent recorded (type=Activated)
  → ASP.NET Identity role updated to match tier
```

## Dependencies
- `AutoSignalsDbContext` — reads/writes `UserData`, `SubscriptionEvents`
- `UserManager<IdentityUser>` — role assignment
