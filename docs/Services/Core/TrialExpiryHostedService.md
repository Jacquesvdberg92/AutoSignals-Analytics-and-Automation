# TrialExpiryHostedService

**Namespace:** `AutoSignals.Services`  
**Type:** Background service (`BackgroundService`)

## Overview
`TrialExpiryHostedService` is a scheduled background service that periodically checks for expired trial subscriptions and downgrades users who have exceeded their trial period back to the free tier.

## Flow
```
Service wakes every 1 hour
  → Query all users with SubscriptionTier = Trial AND TrialEndDate < UtcNow
  → For each expired trial user:
      → SubscriptionService.CancelSubscriptionAsync(userId, "Trial expired")
      → Tier downgraded to Free
      → SubscriptionEvent logged
```

## Notes
- The check interval is 1 hour — expired trials may have up to 1 hour of grace access.
- Users are notified of trial expiry via email (if email notifications enabled).
- Trial duration is set in `SubscriptionService.StartTrialAsync`.

## Dependencies
- `ISubscriptionService` — tier management + cancellation
- `AutoSignalsDbContext` — queries expired trial records
