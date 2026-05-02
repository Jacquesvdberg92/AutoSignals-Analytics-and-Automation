# NOWPaymentsWebhookController

**Authorization:** None (public webhook endpoint — validated by signature)

## Overview
The `NOWPaymentsWebhookController` is a `ControllerBase` (API controller) that receives Instant Payment Notifications (IPNs) from the NOWPayments crypto payment gateway. It validates the HMAC-SHA512 signature on each incoming request before processing.

## Endpoint

### POST `/api/nowpayments/webhook`
Receives payment status update payloads from NOWPayments. Payload contains payment ID, order ID, payment status, and amount details.

## Flow
```
NOWPayments sends POST to /api/nowpayments/webhook
  → Controller reads raw body
  → HMAC-SHA512 signature verified against NOWPayments IPN secret
  → If valid → NOWPaymentsWebhookService.ProcessAsync(payload)
    → Payment matched to pending subscription event
    → If confirmed → SubscriptionService.ActivateSubscriptionAsync(...)
    → User subscription tier updated
  → 200 OK returned to NOWPayments
```

## Security
- Requests without a valid `x-nowpayments-sig` header are rejected with 400.
- Raw body is read before model binding to ensure signature is computed on the original bytes.

## Dependencies
- `NOWPaymentsWebhookService` — business logic for payment processing
- `NOWPaymentsOptions` — IPN secret configuration
