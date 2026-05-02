# PaymentDiagnosticsGroup

**Authorization:** Admin role only  
**Type:** Minimal API endpoint group (not a classic MVC controller)

## Overview
`PaymentDiagnosticsGroup` is a Minimal API group that exposes diagnostic endpoints for payment troubleshooting. It maps HTTP endpoints used by the Payment Diagnostics admin page.

## Endpoints

### GET `/Admin/PaymentDiagnostics`
Returns diagnostic information for the payment system:
- Pending staged subscription activations
- Recent subscription events
- NOWPayments configuration status (API key presence, IPN secret configured)

### POST `/Admin/PaymentDiagnostics/Retry/{paymentId}`
Manually retries processing for a specific NOWPayments payment ID. Useful when a webhook was missed or failed.

## Dependencies
- `AutoSignalsDbContext` — reads subscription events
- `NOWPaymentsWebhookService` — retry logic
- `NOWPaymentsRecoveryService` — recovery helpers
