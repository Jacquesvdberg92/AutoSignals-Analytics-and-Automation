# 1. Executive Summary

AutoSignals is an ASP.NET Core 8 cryptocurrency signal-ingestion and trading-automation platform. It combines Telegram signal parsing, per-user provider settings, automated order execution across multiple exchanges, and dashboard/analytics views. The codebase shows real product ambition, but it is not operationally safe in its current state.

The top priorities are:
- restore basic application integrity by fixing the broken build in `Program.cs:220` and removing the ignored/missing hosted service file referenced by `.gitignore:365`
- lock down unauthenticated admin-style endpoints, especially `Controllers/ErrorLogsController.cs` and `Controllers/ProvidersController.cs`
- remove the hard-coded SQL Server connection in `Data/AutoSignalsDbContext.cs:106-107`
- address platform/runtime risks in `Services/SignalPerformanceService.cs` and the exchange services
- establish automated tests; there is no application test project, and `npm test` is only a placeholder script

Overall health: **high risk**. The repository has useful domain logic and documentation, but security, deployment reliability, and regression safety all need significant work before production use.

# 2. Critical Bugs

## Bug 1: Application does not build because a hosted service is registered but its source file is absent
- **Description:** `Program.cs` registers `ExchangeHostedService`, but the class is not present in the repository. `.gitignore` also explicitly ignores `/Services/ExchangeHostedService.cs`, which strongly suggests the missing file was accidentally excluded.
- **Location:** `/home/runner/work/AutoSignals-Analytics-and-Automation/AutoSignals-Analytics-and-Automation/Program.cs:216-220`, `/home/runner/work/AutoSignals-Analytics-and-Automation/AutoSignals-Analytics-and-Automation/.gitignore:364-366`
- **Impact:** `dotnet build --no-restore` fails with `CS0246`, so the application cannot be shipped or validated in CI.
- **Suggested fix:** Either restore `Services/ExchangeHostedService.cs` and stop ignoring it, or remove the service registration and replace it with the intended hosted services.

## Bug 2: Provider edits can silently erase fields that are not bound by the form
- **Description:** `ProvidersController.Edit` binds only a subset of `Provider` properties, then calls `_context.Update(provider)`. Properties not included in the bind list, such as `Picture`, `TakeProfitDistribution`, `LastProvidedSignal`, `IsActive`, `LongRatio`, and `ShortRatio`, can be overwritten with `null`/default values on save.
- **Location:** `/home/runner/work/AutoSignals-Analytics-and-Automation/AutoSignals-Analytics-and-Automation/Controllers/ProvidersController.cs:138-159`, `/home/runner/work/AutoSignals-Analytics-and-Automation/AutoSignals-Analytics-and-Automation/Models/Provider.cs:20-31`
- **Impact:** Admin or user edits can unintentionally destroy provider metadata and images.
- **Suggested fix:** Load the existing entity from the database and update only the fields that are intentionally editable.

## Bug 3: Admin editing another user’s provider settings actually updates the current admin’s settings
- **Description:** The settings page supports opening another user’s settings via `Settings(string? userId)`, but `UpdateProviderSettings` always resolves `userId` from `_userManager.GetUserId(User)` instead of the model/user being edited.
- **Location:** `/home/runner/work/AutoSignals-Analytics-and-Automation/AutoSignals-Analytics-and-Automation/Controllers/SettingsController.cs:45-56`, `:225-268`
- **Impact:** Admins cannot reliably manage another user’s provider settings, and may overwrite their own settings by mistake.
- **Suggested fix:** Carry the target user ID through the form and enforce admin/self authorization consistently in the POST action.

## Bug 4: Requesting settings for a nonexistent user can trigger a null-path failure
- **Description:** `Settings` fetches `user` with `FindByIdAsync(userId)` and immediately calls `_userManager.GetRolesAsync(user)` without checking whether `user` is null.
- **Location:** `/home/runner/work/AutoSignals-Analytics-and-Automation/AutoSignals-Analytics-and-Automation/Controllers/SettingsController.cs:56-59`
- **Impact:** Invalid or stale user IDs can throw instead of returning a clean 404/validation error.
- **Suggested fix:** Guard `user == null` and return `NotFound()` or a user-friendly error before querying roles.

## Bug 5: Linux/runtime incompatibility in signal image generation
- **Description:** `SignalPerformanceService` uses `System.Drawing` APIs such as `Font`, `Bitmap`, `Graphics.FromImage`, and `Image.FromFile`. The existing build already raises CA1416 warnings for these Windows-only APIs.
- **Location:** `/home/runner/work/AutoSignals-Analytics-and-Automation/AutoSignals-Analytics-and-Automation/Services/SignalPerformanceService.cs:60-139`
- **Impact:** Signal rendering can fail at runtime on Linux containers/hosts even after the compile error is resolved.
- **Suggested fix:** Replace `System.Drawing` with a cross-platform imaging library such as ImageSharp or SkiaSharp.

# 3. Code Quality Improvements

## Refactoring and maintainability
- `Program.cs` contains duplicate `AddControllersWithViews()` registration (`Program.cs:15-17`, `:30-31`) and mixes configuration, service wiring, and operational decisions in one large file. Split startup registration into extension methods by concern.
- `ProvidersController`, `ErrorLogsController`, and several other controllers use scaffold-style CRUD patterns with weak boundaries. Separate admin-only management logic from public read-only pages.
- `Models/Signal.cs:5-13` stores financial values as `float`. This is inappropriate for trading calculations; use `decimal` plus validation attributes (`[Required]`, leverage bounds, max lengths).
- `Services/UserOrderWatchDogService.cs` and `Services/SignalPerformanceService.cs` are very large orchestration classes. Break them into focused collaborators for pricing, execution, persistence, notifications, and retry policies.

## Performance and reliability
- `Services/BinancePriceService.cs:56-62` and `Services/KuCoinPriceService.cs:61-67` use `Task.WhenAll(...)` and then block on `.Result`. Replace `.Result` with `await`ed values to avoid sync-over-async issues.
- `Services/SignalPerformanceService.cs:202-206` loads all signals and all asset prices into memory before filtering in-process. This will degrade sharply as history grows.
- `Controllers/AnalyticsController.cs:29-46` performs one `GetRolesAsync` call per user, producing an N+1 pattern on the admin dashboard.
- `Services/UserOrderWatchDogService.cs:90-99` loops through open positions and calls `_context.Positions.Update(position)` repeatedly after loading the entire open-position set. Consider narrowing queries, batching updates, and offloading repeated calculations.
- `Services/UserOrderWatchDogService.cs:38-41`, `Services/KuCoinPriceService.cs:75`, and `Services/RecaptchaService.cs:30` still use `Console.WriteLine`; switch to structured `ILogger` usage.
- Several services interpolate structured data into strings instead of using log parameters, e.g. `Services/TelegramBotService.cs:241`, `:276`, and `Services/DynamicSignalParserService.cs:226`.

## Broken patterns / dead code / correctness drift
- `Services/ExchangeBalanceService.cs:29-40` catches all exceptions and returns `0m`, masking real API, network, and credential failures.
- `Controllers/FunController.cs:5-15` is effectively an unrelated hidden route with no namespace consistency; it should be isolated or removed from production routing.
- The repository still includes scaffold-generated create/edit/delete surfaces for logs and providers, which conflicts with the otherwise role-protected admin approach used elsewhere.

# 4. Security Findings

## Finding 1: Public access to application error logs exposes stack traces and internal data
- **Description:** `ErrorLogsController` has no `[Authorize]` protection, yet exposes index/details/create/edit/delete actions over stored error logs. The error log model includes `StackTrace`, `Source`, and `AdditionalData`.
- **Severity:** Critical
- **Location:** `/home/runner/work/AutoSignals-Analytics-and-Automation/AutoSignals-Analytics-and-Automation/Controllers/ErrorLogsController.cs:12-152`, `/home/runner/work/AutoSignals-Analytics-and-Automation/AutoSignals-Analytics-and-Automation/Models/ErrorLog.cs:1-8`, `/home/runner/work/AutoSignals-Analytics-and-Automation/AutoSignals-Analytics-and-Automation/Views/ErrorLogs/Index.cshtml:43-98`
- **Exploit scenario:** An unauthenticated user can browse `/ErrorLogs`, read stack traces and operational data, and delete or modify records to hide evidence after probing the site.
- **Recommended fix:** Put the entire controller behind `[Authorize(Roles = "Admin")]`, disable create/edit/delete from the web UI, and sanitize what is stored in `AdditionalData`.

## Finding 2: Provider management endpoints are writable without authorization
- **Description:** `ProvidersController` has no controller-level or action-level authorization on create/edit/delete endpoints.
- **Severity:** High
- **Location:** `/home/runner/work/AutoSignals-Analytics-and-Automation/AutoSignals-Analytics-and-Automation/Controllers/ProvidersController.cs:97-117`, `:119-180`, `:183-213`
- **Exploit scenario:** Any anonymous or low-privilege user can create fake providers, alter public provider statistics, or upload arbitrary provider images.
- **Recommended fix:** Require admin authorization for all mutating provider actions and keep only read-only listing/details public if that is intentional.

## Finding 3: Hard-coded SQL Server connection bypasses configured connection handling and disables encryption
- **Description:** `AutoSignalsDbContext.OnConfiguring` hard-codes a local SQL Server connection using `Integrated Security=SSPI` and `Encrypt=false`, while `Program.cs` separately wires a configured connection string.
- **Severity:** High
- **Location:** `/home/runner/work/AutoSignals-Analytics-and-Automation/AutoSignals-Analytics-and-Automation/Data/AutoSignalsDbContext.cs:106-107`, `/home/runner/work/AutoSignals-Analytics-and-Automation/AutoSignals-Analytics-and-Automation/Program.cs:40-46`
- **Exploit scenario:** Production deployments can silently use the wrong database configuration; traffic to SQL Server is explicitly unencrypted.
- **Recommended fix:** Remove `OnConfiguring`, rely solely on DI-provided configuration, and require encrypted DB connections.

## Finding 4: User-configurable regex patterns execute without any timeout
- **Description:** Dynamic provider parsing executes regex patterns from persisted rules via `Regex.Match(...)` and `new Regex(...)` without a timeout.
- **Severity:** High
- **Location:** `/home/runner/work/AutoSignals-Analytics-and-Automation/AutoSignals-Analytics-and-Automation/Services/DynamicSignalParserService.cs:193-205`, `/home/runner/work/AutoSignals-Analytics-and-Automation/AutoSignals-Analytics-and-Automation/Controllers/SignalProvidersParsingController.cs:513-515`, `:1230-1231`, `:1394-1395`
- **Exploit scenario:** A catastrophic backtracking pattern entered through the admin parsing UI can hang parsing requests or the Telegram ingestion pipeline, causing a denial of service.
- **Recommended fix:** Enforce regex timeouts, validate patterns before persistence, and consider safe-regex checks for admin-defined patterns.

## Finding 5: File uploads accept arbitrary content with minimal validation
- **Description:** Feedback screenshots and provider pictures are copied straight into memory/database without validating MIME type, extension, image signature, or per-file size.
- **Severity:** Medium
- **Location:** `/home/runner/work/AutoSignals-Analytics-and-Automation/AutoSignals-Analytics-and-Automation/Controllers/UserFeedbacksController.cs:127-148`, `:257-272`, `/home/runner/work/AutoSignals-Analytics-and-Automation/AutoSignals-Analytics-and-Automation/Controllers/ProvidersController.cs:149-155`
- **Exploit scenario:** Attackers can store non-image payloads, oversized files, or decompression bombs; `GetImage` always serves stored bytes as `image/png` regardless of actual content.
- **Recommended fix:** Validate type and size per file, inspect file signatures, strip dangerous metadata, and persist the real content type.

## Finding 6: Admin access to user exchange credentials lacks auditability and least-privilege controls
- **Description:** The settings workflow allows admins to open another user’s settings and decrypt stored credentials when submitted fields are blank.
- **Severity:** Medium
- **Location:** `/home/runner/work/AutoSignals-Analytics-and-Automation/AutoSignals-Analytics-and-Automation/Controllers/SettingsController.cs:45-56`, `:129-131`, `:194-206`
- **Exploit scenario:** Any admin account compromise grants practical access to user trading credentials without a dedicated approval flow or audit trail.
- **Recommended fix:** Add audit logging for credential access, separate credential rotation from general profile edits, and consider one-way secret update flows that do not require decryption for display logic.

# 5. Dependency & Config Issues

## Vulnerable dependencies
- `npm audit --json` reports **20 JavaScript vulnerabilities** across **389 dependencies**, including:
  - `swiper` critical prototype pollution (`package.json:75`, audit result `/tmp/copilot-tool-output-1775167431978-y9rxtx.txt:656-687`)
  - `moment` high severity ReDoS/path traversal issues (`package.json:62`, audit result `:347-407`)
  - `dual-listbox` high severity vulnerable chain (`package.json:40`, audit result `:98-115`)
  - `lodash`, `lodash-es`, `immutable`, `minimatch`, and `picomatch` high-severity transitive issues
- `dotnet list package --vulnerable --include-transitive` reports vulnerable transitive packages:
  - `Microsoft.Build` 17.8.3 — High
  - `Azure.Identity` 1.10.3 — Moderate
  - `Microsoft.Identity.Client` 4.56.0 — Low/Moderate

## Configuration and deployment issues
- `appsettings.Development.json` in-repo only contains logging config, so local development depends on out-of-band secrets/config that are not validated by the repository.
- `appsettings - Sample.json` is better populated, but the runtime code still fights it because `AutoSignalsDbContext` hard-codes its own SQL connection.
- `.gitignore` ignores `/appsettings.json` but not `appsettings.Development.json`; that increases the chance of future secret leakage through committed development settings.
- There is no `.github/workflows/` directory, no `Dockerfile`, and no `docker-compose` file, so build, audit, and deployment checks are not automated.

# 6. Testing Gaps

- There is **no application test project** in the repository. `glob("**/*Tests*.csproj")` and `glob("**/*Test*.csproj")` returned no matches.
- `package.json:6-8` defines `npm test` as `echo "Error: no test specified" && exit 1`, so front-end dependency checks are also missing.
- Critical missing test areas:
  - startup/build regression test covering the hosted-service registration in `Program.cs`
  - authorization tests for `ProvidersController` and `ErrorLogsController`
  - settings tests for editing another user, invalid `userId`, and provider-settings persistence
  - parser tests for dynamic regex rules, especially malformed and catastrophic patterns
  - financial correctness tests for `OrderService`, `SignalPerformanceService`, and `UserOrderWatchDogService`
  - file-upload validation tests for provider pictures and feedback screenshots
  - platform tests for Linux-safe image generation or a replacement for `System.Drawing`
- Reliability is currently dependent on manual testing and production behavior, which is especially risky given the background workers and exchange integrations.

# 7. Product & Competitive Analysis

## What this product is
AutoSignals is a crypto-trading operations platform focused on:
- ingesting trade signals from Telegram
- normalizing and parsing them into a common `Signal` model
- automating order placement for subscribed users across multiple exchanges
- showing analytics, provider statistics, and educational content

## Comparable tools/products
- **Cornix**: Telegram-based signal trading automation
- **3Commas**: exchange-connected automation, portfolio management, and bot workflows
- **Bitsgap / Altrady**: multi-exchange execution and portfolio tooling
- **TradingView webhook bots**: automated execution pipelines driven by external signals

## Strengths
- Multi-exchange ambition is clear in `Services/*PriceService.cs`.
- Dynamic parser management via `SignalProvidersParsingController` is a good product differentiator for rapidly onboarding new signal providers.
- The repository includes user-facing education, analytics, feedback capture, and provider comparison pages, which can improve retention beyond pure trade execution.

## Weaknesses
- Core trust features are weak: broken build, public admin-style endpoints, weak testing posture, and fragile deployment assumptions.
- UX appears heavily server-rendered and admin-oriented; there is little evidence of onboarding guidance, health/status feedback, or safe failure states for users linking live exchange credentials.
- Signal/image/performance workflows are tightly coupled to background tasks and database state, which will make the product feel unreliable as load grows.
- There is no visible CI/CD, operational dashboard, or customer-safe audit trail, which competing products treat as baseline.

## Suggested features or improvements to increase user appeal
- Add an exchange-connection health dashboard showing credential status, last successful sync, and recent execution failures.
- Add a sandbox/paper-trading mode with first-class UX rather than hiding it inside admin/testing surfaces.
- Support webhooks and TradingView integrations in addition to Telegram.
- Add execution audit trails for every user order, including who changed settings and why an order was or was not executed.
- Add onboarding flows for parser validation, risk limits, and exchange connection checks before enabling live automation.
- Expose safer analytics: win rate, slippage, fill quality, provider reliability, and per-exchange uptime.
- Add mobile-friendly notifications and kill-switch controls so users can pause automation immediately.

# 8. Actionable Roadmap

## Immediate (critical issues)
1. Remove or restore the missing `ExchangeHostedService` reference so the repository builds again.
2. Protect `ErrorLogsController` with admin authorization and remove public create/edit/delete access.
3. Protect `ProvidersController` mutating endpoints with admin authorization.
4. Remove the hard-coded connection string from `AutoSignalsDbContext` and require encrypted SQL connections.
5. Patch or replace the critical/high vulnerable JS dependencies, starting with `swiper`, `moment`, and `dual-listbox`.

## Short-term
1. Refactor provider editing to update tracked entities instead of overwriting partially bound models.
2. Fix `SettingsController` to correctly handle nonexistent users and to persist provider settings for the intended target user.
3. Replace `System.Drawing` usage with a cross-platform library.
4. Add regex timeouts and validation to all dynamic parsing/testing paths.
5. Add file type/signature validation for uploaded images.
6. Replace `Console.WriteLine` and string-interpolated logs with structured `ILogger` calls.
7. Add a minimal CI workflow that runs restore, build, dependency audit, and tests.

## Long-term
1. Introduce dedicated test projects for domain logic, authorization, and integration scenarios.
2. Split large orchestration services into smaller components with clearer boundaries and retry policies.
3. Introduce secrets-management and credential-access auditing aligned with least-privilege practices.
4. Rework analytics and performance tracking to avoid full-table loads and N+1 queries.
5. Add operational tooling: health checks, background-job monitoring, structured alerts, and deployment/container support.
