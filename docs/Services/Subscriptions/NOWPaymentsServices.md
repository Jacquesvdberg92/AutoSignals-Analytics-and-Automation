# NOWPayments Services

**Namespace:** `AutoSignals.Services.NOWPayments`

## Overview
Three services handle the full NOWPayments crypto payment lifecycle:

---

## NOWPaymentsSubscriptionProvider
**Implements:** `ISubscriptionProvider`  
**Type:** Scoped service

Creates payment invoices for new subscriptions via the NOWPayments REST API.

### Method
| Method | Description |
|--------|-------------|
| `CreatePaymentAsync(userId, plan)` | Creates a NOWPayments invoice for the given plan. Returns the payment URL to redirect the user to. Stages a pending `SubscriptionEvent` in the DB. |

### Flow
```
User selects plan → SubscriptionController.Checkout()
  → NOWPaymentsSubscriptionProvider.CreatePaymentAsync()
  → POST to NOWPayments /v1/invoice
  → Response: payment ID + hosted checkout URL
  → SubscriptionEvent staged as Pending (paymentId saved)
  → User redirected to NOWPayments checkout
```

---

## NOWPaymentsWebhookService
**Type:** Scoped service

Processes validated IPN webhook payloads from NOWPayments.

### Method
| Method | Description |
|--------|-------------|
| `ProcessAsync(payload)` | Matches the payment ID to a staged event, checks payment status, and activates the subscription if confirmed. |

### Handled Statuses
| Status | Action |
|--------|--------|
| `finished` | Activate subscription |
| `confirmed` | Activate subscription |
| `partially_paid` | Log warning, do not activate |
| `failed` / `expired` | Log, mark event as failed |

---

## NOWPaymentsRecoveryService
**Type:** Scoped service

Handles recovery for payments that were completed but whose webhooks were missed or failed.

### Method
| Method | Description |
|--------|-------------|
| `RecoverPendingPaymentsAsync()` | Queries the NOWPayments API for the status of all staged-but-unresolved payments and processes any that are now confirmed. |

### Usage
Called via the Payment Diagnostics admin page to manually trigger recovery.

---

## Configuration: `NOWPaymentsOptions`
| Setting | Description |
|---------|-------------|
| `ApiKey` | NOWPayments REST API key |
| `IpnSecret` | HMAC secret for IPN signature validation |
| `SuccessUrl` | Redirect URL after successful payment |
| `CancelUrl` | Redirect URL after cancelled payment |
