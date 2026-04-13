# AutoSignals – Manual Sanity-Check Testing Plan

> **Purpose:** A structured walkthrough to verify all major site features are working correctly.  
> **Roles required to complete full coverage:** Guest, Free User, Pro/Subscriber, VIP/Tester, Admin  
> **Legend:** ✅ Pass · ❌ Fail · ⚠️ Partial / Needs Attention

---

## Table of Contents
1. [Authentication & Account Management](#1-authentication--account-management)
2. [Public / Unauthenticated Pages](#2-public--unauthenticated-pages)
3. [Navigation & Layout](#3-navigation--layout)
4. [Signal Providers Dashboard](#4-signal-providers-dashboard)
5. [Assets / Coins & Tokens Dashboard](#5-assets--coins--tokens-dashboard)
6. [Signals](#6-signals)
7. [Signal Performances](#7-signal-performances)
8. [Portfolio](#8-portfolio)
9. [VIP Dashboard (Pro/VIP)](#9-vip-dashboard-provip)
10. [Settings / Profile](#10-settings--profile)
11. [Exchange Connections (API Keys)](#11-exchange-connections-api-keys)
12. [Subscription & Billing](#12-subscription--billing)
13. [Education Section](#13-education-section)
14. [Support – FAQ & Report a Problem](#14-support--faq--report-a-problem)
15. [Legal Pages](#15-legal-pages)
16. [Data Privacy & GDPR](#16-data-privacy--gdpr)
17. [Admin Panel](#17-admin-panel)
18. [Access Control Matrix](#18-access-control-matrix)

---

## 1. Authentication & Account Management

### 1.1 Registration
| # | Step | Expected Result | Status |
|---|------|-----------------|--------|
| 1 | Visit `/Identity/Account/Register` | Registration form loads | |
| 2 | Submit with a valid email and strong password | Account created; confirmation email sent | |
| 3 | Submit with a duplicate email | Validation error shown | |
| 4 | Submit with a weak/invalid password | Inline validation error shown | |
| 5 | Submit with mismatched password/confirm | Error shown | |
| 6 | Click the confirmation link in the email | Account confirmed; redirect to login | |
| 7 | Try logging in before confirming email | Should show "email not confirmed" message | |

### 1.2 Login
| # | Step | Expected Result | Status |
|---|------|-----------------|--------|
| 1 | Navigate to `/Identity/Account/Login` | Login form loads | |
| 2 | Log in with valid confirmed credentials | Redirected to home/dashboard | |
| 3 | Log in with wrong password | Error shown; account not locked after 1st attempt | |
| 4 | Log in with unconfirmed email | Appropriate error shown | |
| 5 | Log in with non-existent email | Generic error shown (no user enumeration) | |

### 1.3 Forgot Password / Reset
| # | Step | Expected Result | Status |
|---|------|-----------------|--------|
| 1 | Click "Forgot password" on login page | Forgot password form loads | |
| 2 | Enter registered email and submit | "Check your email" confirmation shown | |
| 3 | Click reset link in email | Reset password form loads | |
| 4 | Enter mismatched new passwords | Validation error shown | |
| 5 | Submit valid new password | Password changed; redirect to login | |
| 6 | Try re-using the same reset link | Link should be expired/invalid | |

### 1.4 Two-Factor Authentication (2FA)
| # | Step | Expected Result | Status |
|---|------|-----------------|--------|
| 1 | Navigate to `/Identity/Account/Manage/TwoFactorAuthentication` | 2FA management page loads | |
| 2 | Enable 2FA using authenticator app | QR code displayed; verification code accepted | |
| 3 | Log out and log back in with 2FA enabled | 2FA code prompt shown and accepted | |
| 4 | Enter wrong 2FA code | Error shown; not logged in | |
| 5 | Use a recovery code to log in | Login succeeds; recovery code consumed | |
| 6 | Disable 2FA | 2FA removed; login proceeds without code | |

### 1.5 Logout
| # | Step | Expected Result | Status |
|---|------|-----------------|--------|
| 1 | Click logout | Session cleared; redirected to home/login | |
| 2 | Try accessing `/settings` after logout | Redirected to login page | |

---

## 2. Public / Unauthenticated Pages

| # | URL | Expected Result | Status |
|---|-----|-----------------|--------|
| 1 | `/` (Landing page) | Landing page loads without error | |
| 2 | `/pricing` | Pricing page loads with all plans shown | |
| 3 | `/terms-conditions` | Terms & Conditions page loads | |
| 4 | `Home/FAQ` | FAQ page loads; all accordion tabs function | |
| 5 | `/Providers/Index` | Signal Providers list loads (public) | |
| 6 | `/Exchanges/Index` | Exchanges list loads (public) | |
| 7 | `/Assets/dashboard` | Assets dashboard loads (public) | |
| 8 | All education pages (see section 13) | Education pages load | |

---

## 3. Navigation & Layout

| # | Check | Expected Result | Status |
|---|-------|-----------------|--------|
| 1 | Sidebar loads on authenticated pages | Sidebar visible and functional | |
| 2 | Logo links to home (`/`) | Correct navigation | |
| 3 | Sidebar collapses/expands correctly | State preserved in `localStorage` | |
| 4 | Dashboard sub-menu defaults to open on first load | Dashboard menu is expanded | |
| 5 | Admin menu items visible only to Admin role | Non-admins do not see Admin section | |
| 6 | VIP section visible to VIP, Tester, and Admin roles only | Free/Pro users do not see VIP section | |
| 7 | "My Subscription" link visible only when authenticated | Hidden for guests | |
| 8 | "Portfolio" link visible only when authenticated | Hidden for guests | |
| 9 | "Report a problem" link visible only when authenticated | Hidden for guests | |
| 10 | Header loads correctly (avatar, user menu) | User name and avatar displayed | |
| 11 | Dark/light mode toggle works (if present) | Theme switches correctly | |
| 12 | Mobile responsive layout (resize browser) | Menu collapses to hamburger; content reflows | |

---

## 4. Signal Providers Dashboard

**Route:** `Providers/Index` and `Providers/Details/{id}`

| # | Step | Expected Result | Status |
|---|------|-----------------|--------|
| 1 | Navigate to Signal Providers | List of providers loads alphabetically | |
| 2 | Click on a provider | Provider detail page loads | |
| 3 | Detail page shows performance stats | Win rate, TP distribution, short/long ratio displayed | |
| 4 | Detail page shows recent signals (last 90 days) | Signal list rendered | |
| 5 | **Free user:** Verify only some providers are visible | Access limited per subscription | |
| 6 | **VIP/Admin:** All providers visible | Full list displayed | |

---

## 5. Assets / Coins & Tokens Dashboard

**Route:** `/Assets/dashboard` and `/Assets/Candles`

| # | Step | Expected Result | Status |
|---|------|-----------------|--------|
| 1 | Navigate to Coins & Tokens | Asset price table loads | |
| 2 | Prices shown for multiple exchanges (Bitget, Binance, Bybit, OKX, KuCoin) | Per-exchange columns populated | |
| 3 | Data has a recent timestamp | Prices are not stale (check time column) | |
| 4 | Navigate to `/Assets/Candles` | Candle chart page loads | |
| 5 | Select a symbol and timeframe | Chart renders correctly | |
| 6 | No JavaScript console errors on charts page | Console is clean | |

---

## 6. Signals

**Route:** `Signals/Index`

| # | Step | Expected Result | Status |
|---|------|-----------------|--------|
| 1 | Navigate to Signals (authenticated) | Signal list loads | |
| 2 | **Free user:** Signals are delayed by 24 hours | Signals only up to 24h ago; "delayed" banner shown | |
| 3 | **Free user:** Only last 7 days of signals shown | Older signals not visible | |
| 4 | **VIP/Admin:** Last 90 days of signals shown | Full range displayed | |
| 5 | Click on a signal to view details | Signal detail page loads with all fields | |
| 6 | Navigate to Signals/Delete as Admin | Confirmation page shown | |

---

## 7. Signal Performances

**Route:** `SignalPerformances/Index`

| # | Step | Expected Result | Status |
|---|------|-----------------|--------|
| 1 | Navigate to Signal Performances (authenticated) | Performance list loads | |
| 2 | **Free/Pro user:** Sees only last 30 days | Date range label "30-day history" shown | |
| 3 | **VIP/Admin:** Sees full history | All records visible | |
| 4 | Click Details on a performance record | Detail page loads with all stats | |
| 5 | Admin: Create a new performance record | Record created and appears in list | |
| 6 | Admin: Edit a performance record | Changes saved correctly | |
| 7 | Admin: Delete a performance record | Record removed from list | |

---

## 8. Portfolio

**Route:** `Portfolio/Dashboard`

| # | Step | Expected Result | Status |
|---|------|-----------------|--------|
| 1 | Navigate to Portfolio (authenticated) | Default portfolio loads | |
| 2 | **Free user:** Only 1 portfolio allowed | Create button disabled/hidden after 1 portfolio; extra portfolios hidden | |
| 3 | **Pro/Subscriber user:** Up to 3 portfolios allowed | Can create up to 3 | |
| 4 | **VIP/Admin:** Up to 10 portfolios allowed | Can create up to 10 | |
| 5 | Create a new portfolio | Portfolio appears in the list | |
| 6 | Add a holding to a portfolio | Holding saved and displayed | |
| 7 | Edit a holding | Changes reflected immediately | |
| 8 | Delete a holding | Holding removed from portfolio | |
| 9 | Delete a portfolio | Portfolio and all its holdings removed | |
| 10 | Rename a portfolio | New name saved and displayed | |
| 11 | Set a portfolio as default | Default star/flag shown | |
| 12 | Switch between portfolios using the tab/selector | Correct portfolio data shown | |
| 13 | Downgrade scenario: extra portfolios hidden but preserved | Portfolios reappear on upgrade | |

---

## 9. VIP Dashboard (Pro/VIP)

**Route:** `VipDashboard/Index`  
**Access:** Requires `RequiresPro` policy (VIP, Tester, Admin)

| # | Step | Expected Result | Status |
|---|------|-----------------|--------|
| 1 | **Free user:** Navigate to `/VipDashboard/Index` | Redirected (403 or to account-needed page) | |
| 2 | **VIP/Admin:** Navigate to VIP Dashboard | Dashboard loads with positions and orders | |
| 3 | Date range filter (7d, 30d, 90d, custom) | Data updates to reflect selected range | |
| 4 | Open positions shown | Open positions count and list are accurate | |
| 5 | Closed positions shown | P&L summary displayed | |
| 6 | Orders shown | Order history displayed | |
| 7 | Admin viewing another user's dashboard (`?userId=...`) | Admin can view, non-admin returns 403 | |

---

## 10. Settings / Profile

**Route:** `/settings`

| # | Step | Expected Result | Status |
|---|------|-----------------|--------|
| 1 | Navigate to `/settings` (authenticated) | Profile page loads with user info | |
| 2 | Open positions count shown correctly | Matches actual open positions in DB | |
| 3 | Change nickname | Saved and reflected in profile | |
| 4 | Change email via Identity manage | Confirmation email sent; old email still active until confirmed | |
| 5 | Change password via Identity manage | New password works on next login | |
| 6 | Provider settings modal opens | Per-provider copy settings configurable | |
| 7 | Save provider settings | Settings persisted | |
| 8 | Notification settings displayed and editable (VIP) | Saved correctly | |
| 9 | Telegram ID field shown | Can be saved and cleared | |
| 10 | **Admin accessing another user's settings** (`?userId=...`) | Admin can view; regular users get 403 | |

---

## 11. Exchange Connections (API Keys)

**Route:** Settings page → Exchange Connections tab

| # | Step | Expected Result | Status |
|---|------|-----------------|--------|
| 1 | Navigate to Settings; view connections section | Existing connections listed | |
| 2 | **Free user:** Add connection button visible | Attempt shows upgrade message or limit enforced | |
| 3 | **VIP/Admin:** Add up to 5 connections | All 5 saved and listed | |
| 4 | Add a connection (exchange, API key, secret) | Connection saved; secret encrypted at rest | |
| 5 | Edit an existing connection | Changes saved | |
| 6 | Delete a connection | Connection removed from list | |
| 7 | Set a connection as default | Default flag shown | |
| 8 | Attempt to add more connections than the tier allows | Error or upgrade prompt shown | |
| 9 | API key secret not shown in plaintext in the form | Field masked or not pre-populated | |

---

## 12. Subscription & Billing

**Routes:** `/pricing`, `Subscription/Manage`, `Subscription/Success`, `Subscription/Cancel`

| # | Step | Expected Result | Status |
|---|------|-----------------|--------|
| 1 | Visit `/pricing` | All subscription plans displayed correctly | |
| 2 | Click "Subscribe" on a plan (authenticated) | Redirected to LemonSqueezy checkout | |
| 3 | Complete checkout in LemonSqueezy | Redirected to `Subscription/Success`; role upgraded | |
| 4 | Navigate to `Subscription/Manage` | Current plan and status displayed | |
| 5 | Cancel subscription via manage page | Cancellation confirmed; access maintained until period end | |
| 6 | Navigate to `Subscription/Cancel` (cancel flow) | Cancel confirmation page loads | |
| 7 | Webhook processes subscription event | Role/tier updated in DB without manual intervention | |
| 8 | Trial period shown for new subscribers | Trial end date displayed on profile | |
| 9 | Expired subscription downgrades access | Features gated appropriately after expiry | |
| 10 | Admin: `/Admin/Plans` page loads | All plans visible and editable | |

---

## 13. Education Section

| # | URL / Route | Expected Result | Status |
|---|-------------|-----------------|--------|
| 1 | `/education/basics` | Crypto Basics page loads | |
| 2 | `/education/common-strategies` | Common Trading Strategies loads | |
| 3 | `/education/fundamental-analysis` | Fundamental Analysis loads | |
| 4 | `/education/leverage` | Leverage & Margin Trading loads | |
| 5 | `/education/risk-management` | Risk Management loads | |
| 6 | `/education/technical-analysis` | Technical Analysis loads | |
| 7 | `/education/volatility` | Understanding Volatility loads | |
| 8 | `/education/wallets` | Crypto Wallets & Security loads | |
| 9 | All pages accessible without login | No auth redirect | |
| 10 | All pages have no broken images or missing content | Visual check passes | |

---

## 14. Support – FAQ & Report a Problem

### 14.1 FAQ Page

**Route:** `Home/FAQ`

| # | Step | Expected Result | Status |
|---|------|-----------------|--------|
| 1 | Navigate to FAQ page | Page loads; "Trading" tab active by default | |
| 2 | Click "General" tab | General accordion items shown | |
| 3 | Click "User Data" tab | User Data accordion items shown | |
| 4 | Click "Troubleshooting and Support" tab | Troubleshooting accordion items shown | |
| 5 | Expand/collapse accordion items | Smooth open/close; only one open at a time per group | |
| 6 | "API key setup guide" link in Trading → Q1 | Navigates to correct API connection page | |
| 7 | "Report a problem" link in Troubleshooting → Q2 | Navigates to `UserFeedbacks/Create` | |
| 8 | "Profile" link in User Data → Q4 (delete account) | Navigates to `/settings` | |
| 9 | "Profile" link in User Data → Q5 (GDPR) | Navigates to `/settings` | |

### 14.2 Report a Problem (UserFeedbacks)

**Route:** `UserFeedbacks/Create`, `UserFeedbacks/Index`

| # | Step | Expected Result | Status |
|---|------|-----------------|--------|
| 1 | Navigate to `UserFeedbacks/Create` | Feedback form loads | |
| 2 | Submit a valid feedback report | Submitted; appears in the user's list | |
| 3 | Submit an empty form | Validation errors shown | |
| 4 | reCAPTCHA validated on submission | Bots blocked; real users pass | |
| 5 | Navigate to `UserFeedbacks/Index` (own feedback) | User sees only their own reports | |
| 6 | **Admin:** Navigate to `UserFeedbacks/Index` | Admin sees all users' feedback | |
| 7 | Admin: View details of a feedback item | Full detail page loads | |
| 8 | Admin: Delete a feedback item | Item removed | |

---

## 15. Legal Pages

| # | URL | Expected Result | Status |
|---|-----|-----------------|--------|
| 1 | `/terms-conditions` | Terms & Conditions loads without error | |
| 2 | `Home/Privacy` | Privacy policy page loads | |
| 3 | Links in sidebar/footer navigate correctly | No broken links | |

---

## 16. Data Privacy & GDPR

**Route:** Settings → Data & Privacy tab → `Identity/Account/Manage/DeletePersonalData`

| # | Step | Expected Result | Status |
|---|------|-----------------|--------|
| 1 | Navigate to Data & Privacy tab in Settings | Tab/section loads | |
| 2 | Download personal data (`DownloadPersonalData`) | JSON file downloads containing user data | |
| 3 | Navigate to delete account page | Password confirmation form shown | |
| 4 | Submit wrong password on delete page | Error shown; account not deleted | |
| 5 | Submit correct password on delete page | Account deleted; signed out; redirected | |
| 6 | Verify all user data purged from DB after deletion | Positions, API keys, settings, portfolio all removed | |
| 7 | Deleted account email cannot be used to log in | 404 or invalid credentials shown | |

---

## 17. Admin Panel

> All routes require the **Admin** role. Verify non-admins receive a 403/redirect.

### 17.1 Users Management

**Route:** `UsersData/Index`, `UsersData/Details/{id}`

| # | Step | Expected Result | Status |
|---|------|-----------------|--------|
| 1 | Navigate to Admin → Users | All registered users listed with roles | |
| 2 | Click Details on a user | User profile view loads | |
| 3 | Edit a user's role | Role updated; visible on next login | |
| 4 | Lock out a user | User cannot log in; lockout end date shown | |
| 5 | Send password reset email to a user | Email dispatched | |

### 17.2 Subscription Plans

**Route:** `/Admin/Plans`, `/Admin/TierOverride`

| # | Step | Expected Result | Status |
|---|------|-----------------|--------|
| 1 | Navigate to Admin → Subscription Plans | All plans listed | |
| 2 | Edit a plan | Changes saved | |
| 3 | Navigate to `/Admin/UserTier` | Tier override page loads | |
| 4 | Override a user's tier | Tier updated; feature access changes immediately | |

### 17.3 Signal Providers Parsing

**Route:** `SignalProvidersParsing/Index`

| # | Step | Expected Result | Status |
|---|------|-----------------|--------|
| 1 | Navigate to Admin → Parsing Settings | List of signal providers with parsing rules loads | |
| 2 | Create a new signal provider | Provider saved and appears in list | |
| 3 | Add a parsing rule to a provider | Rule saved | |
| 4 | Edit an existing rule | Changes saved | |
| 5 | Delete a rule | Rule removed | |
| 6 | Test parsing with a sample message | Parsed result shown in panel | |
| 7 | AI-generate rules (`GenerateRules`) | Generated rules shown for review | |

### 17.4 Analytics

**Route:** `Analytics/Index`

| # | Step | Expected Result | Status |
|---|------|-----------------|--------|
| 1 | Navigate to Admin → Analytics | Dashboard loads with user counts by role | |
| 2 | Active subscription count shown | Count matches DB | |
| 3 | Page view / event tracking data shown | Analytics events listed | |

### 17.5 Error Logs

**Route:** `ErrorLogs/Index`

| # | Step | Expected Result | Status |
|---|------|-----------------|--------|
| 1 | Navigate to Admin → ErrorLogs | Error log list loads | |
| 2 | View details of an error log entry | Detail page loads with full error info | |
| 3 | Delete an error log | Entry removed | |

### 17.6 Exchanges Management

**Route:** `Exchanges/Index`

| # | Step | Expected Result | Status |
|---|------|-----------------|--------|
| 1 | Navigate to Exchanges | All exchanges listed | |
| 2 | Admin: Create a new exchange | Exchange saved and appears in list | |
| 3 | Admin: Enable/disable an exchange | Availability updated; affects connection add form | |
| 4 | Admin: Delete an exchange | Exchange removed | |

### 17.7 Email Broadcast

**Route:** `EmailBroadcast/Index`

| # | Step | Expected Result | Status |
|---|------|-----------------|--------|
| 1 | Navigate to Admin → Mail Users | Broadcast form loads | |
| 2 | Send a test broadcast to a single email | Email received | |
| 3 | Send broadcast to all users | Bulk email dispatched | |

### 17.8 Order Testing (Admin)

**Route:** `OrderTesting/Index`

| # | Step | Expected Result | Status |
|---|------|-----------------|--------|
| 1 | Navigate to Admin → Order Testing | Test form loads (BTC/USDT, 20x leverage) | |
| 2 | Select an exchange and submit | Order test sequence runs; log output shown | |
| 3 | Invalid exchange selected | Appropriate error shown in logs | |

### 17.9 Kline Settings

**Route:** `/Admin/KlineSettings`

| # | Step | Expected Result | Status |
|---|------|-----------------|--------|
| 1 | Navigate to Admin → Kline Settings | Page loads with row count, symbol count, oldest/newest snapshot | |
| 2 | Toggle Kline data collection on | Setting saved; collection enabled | |
| 3 | Toggle Kline data collection off | Setting saved; collection paused | |

---

## 18. Access Control Matrix

Use this to quickly verify role-based access is enforced.

| Feature / Route | Guest | Free | Pro | VIP | Admin |
|----------------|:-----:|:----:|:---:|:---:|:-----:|
| Landing page (`/`) | ✅ | ✅ | ✅ | ✅ | ✅ |
| Pricing (`/pricing`) | ✅ | ✅ | ✅ | ✅ | ✅ |
| Exchanges list | ✅ | ✅ | ✅ | ✅ | ✅ |
| Providers list | ✅ | ✅ | ✅ | ✅ | ✅ |
| Assets dashboard | ✅ | ✅ | ✅ | ✅ | ✅ |
| Education pages | ✅ | ✅ | ✅ | ✅ | ✅ |
| Signals (real-time) | ❌ | ❌ (delayed) | ✅ | ✅ | ✅ |
| Full performance history | ❌ | ❌ (30d) | ❌ (30d) | ✅ | ✅ |
| Portfolio (multi) | ❌ | 1 only | Up to 3 | Up to 10 | Up to 10 |
| VIP Dashboard | ❌ | ❌ | ❌ | ✅ | ✅ |
| Exchange API connections | ❌ | ❌ | 1 | Up to 5 | Up to 5 |
| Analytics | ❌ | ❌ | ❌ | ❌ | ✅ |
| UsersData / Admin users | ❌ | ❌ | ❌ | ❌ | ✅ |
| Parsing settings | ❌ | ❌ | ❌ | ❌ | ✅ |
| Order Testing | ❌ | ❌ | ❌ | ❌ | ✅ |
| Kline Settings | ❌ | ❌ | ❌ | ❌ | ✅ |
| Error Logs | ❌ | ❌ | ❌ | ❌ | ✅ |
| Email Broadcast | ❌ | ❌ | ❌ | ❌ | ✅ |
| Subscription Plans mgmt | ❌ | ❌ | ❌ | ❌ | ✅ |

---

## Regression Checklist (Quick Smoke Test)

Use this after every deployment to verify nothing critical is broken.

- [ ] Landing page (`/`) loads
- [ ] Register a new account and confirm email
- [ ] Log in with the new account
- [ ] Signal Providers list loads
- [ ] Assets dashboard loads with prices
- [ ] Portfolio dashboard loads (create a holding, delete it)
- [ ] Navigate to all Education pages (no 500 errors)
- [ ] Submit a feedback report
- [ ] Navigate to FAQ; all 4 tabs work
- [ ] Visit `/pricing`; plans displayed
- [ ] Log out; confirm session is cleared
- [ ] Log in as Admin; check Admin menu items are visible
- [ ] Admin → Users list loads
- [ ] Admin → Analytics loads
- [ ] Admin → Error Logs loads
- [ ] Admin → Parsing Settings loads

---

*Last updated: auto-generated from codebase scan*
