# SubscriptionController

**Authorization:** Authenticated users

## Overview
The `SubscriptionController` handles subscription management for end users — upgrading, managing, and cancelling their subscriptions. It integrates with NOWPayments for crypto billing.

## Actions

### Manage (`GET /Subscription/Manage`)
Displays the user's current subscription status, tier, expiry date, trial status, and available upgrade options.

### Checkout (`POST /Subscription/Checkout`)
Initiates a crypto payment via NOWPayments. Creates a payment record and redirects the user to the NOWPayments payment page.

### Cancel (`POST /Subscription/Cancel`)
Cancels the user's active subscription. Records a cancellation event and downgrades the tier at next renewal.

### Success / Cancelled
Return pages after NOWPayments checkout redirect (informational only — actual activation happens via webhook).

## Flow
```
User selects a plan on /pricing
  → POST to Checkout
  → NOWPaymentsSubscriptionProvider.CreatePaymentAsync(plan)
  → User redirected to NOWPayments hosted checkout
  → User pays with crypto
  → NOWPayments sends IPN webhook → NOWPaymentsWebhookController
  → Subscription activated
  → User redirected back to /Subscription/Success
```

## Dependencies
- `ISubscriptionService` — tier management
- `NOWPaymentsSubscriptionProvider` — payment creation
- `AutoSignalsDbContext` — subscription event logging
