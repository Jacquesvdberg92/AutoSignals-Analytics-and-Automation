# HomeController

**Authorization:** Public

## Overview
The `HomeController` serves the public-facing pages of the AutoSignals platform — the landing page, education content, legal pages, pricing, and utility pages.

## Actions

### index (`GET /`, `GET /index`)
Landing page. Loads active subscription plans and active provider count for display. Increments the "Landing Page" analytics counter.

### Pricing (`GET /pricing`)
Displays the subscription pricing page with all active plans from the database.

### Education Pages
A collection of static educational articles:

| Action | Route | Topic |
|--------|-------|-------|
| `EduBasics` | `/education/basics` | Crypto fundamentals |
| `EduCommonStrategies` | `/education/strategies` | Trading strategies |
| `EduFA` | `/education/fundamental-analysis` | Fundamental analysis |
| `EduLeverage` | `/education/leverage` | Leverage & margin |
| `EduRiskManagement` | `/education/risk` | Risk management |
| `EduTA` | `/education/technical-analysis` | Technical analysis |
| `EduVolatility` | `/education/volatility` | Market volatility |
| `EduWallets` | `/education/wallets` | Wallets & security |

### Legal / Support Pages
- `TermsConditions` — Terms & Conditions
- `PrivacyPolicy` — Privacy Policy
- `RefundPolicy` — Refund Policy
- `Faq` — Frequently Asked Questions
- `ApiConnection` — Guide to connecting exchange API keys

### AccountNeeded
Redirect page shown to unauthenticated users who attempt to access a protected feature.

### Error
Standard error page, receives `RequestId` for correlation.

## Dependencies
- `AutoSignalsDbContext` — reads subscription plans
- `IAnalyticsService` — increments page-view counters
