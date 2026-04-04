# AutoSignals – Code Optimization Report

**Date:** 2025  
**Scope:** Full solution analysis – performance, DB access patterns, architecture, and responsiveness  
**Constraint:** No functionality may be removed

---

## Executive Summary

The codebase is functional and well-structured overall, but carries several patterns that put unnecessary load on the database and the CPU with every request and every background tick. The most impactful problems are: missing database indexes on high-frequency query columns, loading entire tables into memory when only a filtered subset is needed, synchronous N+1 DB calls during order dispatch, and a per-page-view analytics write that hits the DB on every HTTP request. Fixing the top 5 issues alone would likely reduce DB load by 60–70 % under normal traffic.

---

## Section 1 – Critical Issues (Fix First)

---

### C1 — Missing Database Indexes on Core Query Columns

**File:** `Data/AutoSignalsDbContext.cs`  
**Impact:** 🔴 Every query against Orders, Positions, Analytics, and SignalPerformances does a full-table scan.

`OnModelCreating` only defines unique indexes on the exchange-price tables. The tables queried most frequently in the app have no non-clustered indexes at all.

| Table | Column(s) queried frequently | Query location |
|---|---|---|
| `Orders` | `Status` | `UserOrderWatchDogService.ProcessOrdersAsync` every minute |
| `Orders` | `UserId` | `VipDashboard.Index`, `UsersDataController.Details` |
| `Orders` | `(UserId, SignalId, Symbol)` | `UserOrderWatchDogService.ExecuteOrderAsync` per execution |
| `Positions` | `Status` | `UserOrderWatchDogService.ProcessOrdersAsync` every minute |
| `Positions` | `UserId` | `VipDashboard.Index` every page load |
| `Positions` | `(UserId, Symbol, Side, Status)` | `CreateOrUpdatePositionAsync` per order execution |
| `Analytics` | `(PageName, Date)` | `TrackPageViewAsync` on every single HTTP request |
| `SignalPerformances` | `Status` | `SignalPerformanceService.TrackPerformance` every 3 minutes |
| `UsersData` | `SubscriptionActive` | `OrderService.CreateOrdersForActiveUsers` per signal |
| `ErrorLogs` | `Timestamp` (DESC) | `ErrorLogsController.Index` on demand |
| `GeneralAssetPrices` | `Symbol` | `UserOrderWatchDogService` fallback, every watchdog tick |

**Proposed solution:**

Add the following to `OnModelCreating` in `AutoSignalsDbContext`:

```csharp
// Orders
modelBuilder.Entity<Order>()
    .HasIndex(o => o.Status);
modelBuilder.Entity<Order>()
    .HasIndex(o => o.UserId);
modelBuilder.Entity<Order>()
    .HasIndex(o => new { o.UserId, o.SignalId, o.Symbol });

// Positions
modelBuilder.Entity<Position>()
    .HasIndex(p => p.Status);
modelBuilder.Entity<Position>()
    .HasIndex(p => p.UserId);
modelBuilder.Entity<Position>()
    .HasIndex(p => new { p.UserId, p.Symbol, p.Side, p.Status });

// Analytics — this table is hit on every page load
modelBuilder.Entity<Analytics>()
    .HasIndex(a => new { a.PageName, a.Date });

// SignalPerformances — queried on Status every 3 minutes
modelBuilder.Entity<SignalPerformance>()
    .HasIndex(sp => sp.Status);

// UsersData — queried for SubscriptionActive on every signal
modelBuilder.Entity<UserData>()
    .HasIndex(u => u.SubscriptionActive);

// ErrorLogs — shown in descending Id order
modelBuilder.Entity<ErrorLog>()
    .HasIndex(e => e.Timestamp);
```

Then generate and apply a new EF migration.

---

### C2 — TrackPageViewAsync: DB Read+Write on Every HTTP Request

**Files:** `Controllers/HomeController.cs`, `Controllers/SignalsController.cs`, `Controllers/ProvidersController.cs`, `Controllers/SignalPerformancesController.cs`  
**Impact:** 🔴 Every page visit = 1 DB read + 1 DB write. Under any real traffic this is the single biggest unnecessary DB hit.

The same `TrackPageViewAsync` private method is copy-pasted into at least 4 controllers. Each call does:
```csharp
await _context.Set<Analytics>().FirstOrDefaultAsync(a => a.PageName == pageName && a.Date == today);
// ...then SaveChangesAsync
```

This means the `Analytics` table absorbs every page hit synchronously, with a write-amplification factor of 1:1.

**Proposed solution:**

1. Extract `TrackPageViewAsync` into a dedicated singleton `AnalyticsTrackingService`.
2. Internally maintain a `ConcurrentDictionary<(string PageName, DateTime Date), int>` as an in-memory counter.
3. Flush accumulated counts to the DB on a background timer (e.g., every 60 seconds), using a single `ExecuteUpdateAsync` per (PageName, Date) key.
4. The controllers call a fire-and-forget method (`TrackingService.Increment(pageName)`) that never touches the DB inline.

This converts potentially thousands of individual DB writes per day into one write per minute per unique page, with zero latency impact on HTTP responses.

---

### C3 — AveragePriceService: Full Table Scan of All 5 Exchange Price Tables

**File:** `Services/AveragePriceService.cs`  
**Impact:** 🔴 Every 5-minute tick loads ALL rows from all 5 price tables entirely into memory, then does O(n²) in-memory lookups.

```csharp
// Current code — loads everything
var bitgetPrices  = await context.BitgetAssetPrices.AsNoTracking().ToListAsync();
var binancePrices = await context.BinanceAssetPrices.AsNoTracking().ToListAsync();
var bybitPrices   = await context.BybitAssetPrices.AsNoTracking().ToListAsync();
var okxPrices     = await context.OkxAssetPrices.AsNoTracking().ToListAsync();
var kucoinPrices  = await context.KuCoinAssetPrices.AsNoTracking().ToListAsync();
```

After loading, the code iterates `groupedBySymbolAndType` and then re-filters each in-memory list using `.Where(p => p.Symbol == symbol && ...)`. With ~2 000 symbols × 5 exchanges this is ~10 000 in-memory scans per calculation cycle.

**Proposed solution:**

Push the aggregation entirely to SQL using a UNION ALL + GROUP BY approach. EF Core supports this via raw SQL or by using `FromSqlRaw` / `ExecuteSqlRaw`:

```sql
INSERT INTO GeneralAssetPrices (Symbol, Type, Price, Open, High, Low, Close, Volume, Time)
SELECT Symbol, Type,
    AVG(Price) AS Price, AVG([Open]) AS [Open], AVG(High) AS High,
    AVG(Low) AS Low, AVG([Close]) AS [Close], AVG(Volume) AS Volume,
    MAX(Time) AS Time
FROM (
    SELECT Symbol, Type, Price, [Open], High, Low, [Close], Volume, Time FROM BitgetAssetPrices
    UNION ALL
    SELECT Symbol, Type, Price, [Open], High, Low, [Close], Volume, Time FROM BinanceAssetPrices
    UNION ALL
    SELECT Symbol, Type, Price, [Open], High, Low, [Close], Volume, Time FROM BybitAssetPrices
    UNION ALL
    SELECT Symbol, Type, Price, [Open], High, Low, [Close], Volume, Time FROM OkxAssetPrices
    UNION ALL
    SELECT Symbol, Type, Price, [Open], High, Low, [Close], Volume, Time FROM KuCoinAssetPrices
) AS AllPrices
GROUP BY Symbol, Type
```

If pushing fully to SQL is not viable yet, the minimum improvement is to replace the O(n²) in-memory lookups with `Dictionary<(string,string), T>` keyed by `(Symbol, NormalizedType)` so lookups are O(1) instead of O(n).

---

### C4 — SignalPerformanceService: Full GeneralAssetPrices Table Load

**File:** `Services/SignalPerformanceService.cs` — `TrackPerformance()`  
**Impact:** 🔴 Every 3-minute tick loads the entire `GeneralAssetPrices` table even when only a handful of symbols are active.

```csharp
var priceData = await _context.GeneralAssetPrices.ToListAsync(); // loads ALL
```

The code then filters in memory:
```csharp
var relevantPrices = priceData.Where(p => p.Symbol == signal.Symbol && p.Time >= performance.StartTime)
```

**Proposed solution:**

Collect the set of symbols needed first, then query only those:

```csharp
var activeSymbols = signalPerformances
    .Select(sp => signals.FirstOrDefault(s => s.Id == sp.SignalId)?.Symbol)
    .Where(s => s != null)
    .Distinct()
    .ToList();

var earliestStartTime = signalPerformances.Min(sp => sp.StartTime);

var priceData = await _context.GeneralAssetPrices
    .Where(p => activeSymbols.Contains(p.Symbol) && p.Time >= earliestStartTime)
    .ToListAsync();
```

This reduces the loaded data set from thousands of rows to only the rows for ~5–20 active symbols.

---

### C5 — OrderService.GetPrecisions: 10 Synchronous N+1 DB Calls

**File:** `Services/OrderService.cs` — `GetPrecisions()`  
**Impact:** 🔴 This method is called for every incoming signal, and inside it are 10 synchronous DB calls using non-async `FirstOrDefault()`.

```csharp
// Each of these is a synchronous DB call:
var bitgetMarket = _context.BitgetMarkets.FirstOrDefault(m => m.Symbol == symbol);
var exchange = _context.Exchanges.FirstOrDefault(e => e.Name == "Bitget" && e.IsEnabled == true);
// ... × 5 exchanges = 10 synchronous DB round-trips
```

Synchronous DB calls in async code block thread-pool threads, reducing throughput under concurrent load. More importantly, 10 sequential round-trips for what could be 1–2 queries is expensive.

**Proposed solution:**

1. Convert to async: use `FirstOrDefaultAsync()`.
2. Fetch all enabled exchanges in a single query at the start.
3. Optionally cache the exchange list (changes extremely rarely) as a `Dictionary<string, Exchange>` in a short-lived in-memory cache (e.g., `IMemoryCache` with a 15-minute TTL).

```csharp
private async Task<Dictionary<int, (...)>> GetPrecisionsAsync(string symbol)
{
    // Single query for all enabled exchanges (cacheable for 15 min)
    var exchanges = await _context.Exchanges
        .Where(e => e.IsEnabled)
        .ToDictionaryAsync(e => e.Name);

    // Parallel queries for all markets
    var (bitget, binance, bybit, okx, kucoin) = await (
        _context.BitgetMarkets.AsNoTracking().FirstOrDefaultAsync(m => m.Symbol == symbol),
        _context.BinanceMarkets.AsNoTracking().FirstOrDefaultAsync(m => m.Symbol == symbol),
        _context.BybitMarkets.AsNoTracking().FirstOrDefaultAsync(m => m.Symbol == symbol),
        _context.OkxMarkets.AsNoTracking().FirstOrDefaultAsync(m => m.Symbol == symbol),
        _context.KuCoinMarkets.AsNoTracking().FirstOrDefaultAsync(m => m.Symbol == symbol)
    ).WhenAll(); // Run all 5 queries in parallel

    // build precisions dict...
}
```

---

## Section 2 – Serious Issues

---

### S1 — VipDashboard: Date Filtering Done in C# Instead of SQL

**File:** `Controllers/VipDashboard.cs` — `Index()` and `GetDashboardData()`  
**Impact:** 🟠 Loads ALL positions and ALL orders for the user, then filters dates in C# memory.

```csharp
var allPositions = await context.Positions.Where(p => p.UserId == userId).ToListAsync();
var positionsInRange = allPositions.Where(p => p.Time >= start && p.Time <= end).ToList(); // C# filter

var allOrders = await context.Orders.Where(o => o.UserId == userId).ToListAsync();
var ordersInRange = allOrders.Where(o => o.Time >= start && o.Time <= end).ToList(); // C# filter
```

A user with 500+ positions and 2 000+ orders (which is expected over time) loads all of them every dashboard refresh.

The same pattern is repeated identically in both `Index()` and `GetDashboardData()`.

**Proposed solution:**

Move the date filter into the SQL query and only load what is required:

```csharp
// Load the in-range subset directly from DB
var positionsInRange = await context.Positions
    .Where(p => p.UserId == userId && p.Time >= start && p.Time <= end)
    .ToListAsync();

// For stats that need "all time" data (open counts etc.), use Count() queries instead
var openPositionsCount = await context.Positions
    .CountAsync(p => p.UserId == userId && p.Status == "OPEN");
```

---

### S2 — ExchangeHostedService: Exchange Fetches Are Sequential, Not Parallel

**File:** `Services/ExchangeHostedService.cs` — `FetchPricesAsync()` and `FetchMarketsAsync()`  
**Impact:** 🟠 The 5 exchange fetches are awaited one after the other. If each takes 2–5 minutes, the total fetch time may approach or exceed the 15-minute schedule interval.

```csharp
// Current — sequential
await FetchPriceData(() => bitgetService.FetchAllBitgetAssetPricesV2Async(), "Bitget");
await FetchPriceData(() => binanceService.FetchAllBinanceAssetPricesV2Async(), "Binance");
await FetchPriceData(() => bybitService.FetchAllBybitAssetPricesV2Async(), "Bybit");
await FetchPriceData(() => okxService.FetchAllOkxAssetPricesV2Async(), "OKX");
await FetchPriceData(() => kucoinService.FetchAllKuCoinAssetPricesV2Async(), "KuCoin");
```

**Proposed solution:**

Run all 5 fetches in parallel:

```csharp
await Task.WhenAll(
    FetchPriceData(() => bitgetService.FetchAllBitgetAssetPricesV2Async(), "Bitget"),
    FetchPriceData(() => binanceService.FetchAllBinanceAssetPricesV2Async(), "Binance"),
    FetchPriceData(() => bybitService.FetchAllBybitAssetPricesV2Async(), "Bybit"),
    FetchPriceData(() => okxService.FetchAllOkxAssetPricesV2Async(), "OKX"),
    FetchPriceData(() => kucoinService.FetchAllKuCoinAssetPricesV2Async(), "KuCoin")
);
```

The same applies to `FetchMarketsAsync`. Note: each exchange service uses its own scoped DB context so there is no concurrency conflict.

---

### S3 — UserOrderWatchDogService: Per-Order N+1 Queries for Related Orders and UserData

**File:** `Services/UserOrderWatchDogService.cs` — `ExecuteOrderAsync()`  
**Impact:** 🟠 For each order that triggers execution, two DB queries are made:

```csharp
var relatedOrders = await _context.Orders
    .Where(o => o.Symbol == order.Symbol && o.SignalId == order.SignalId && o.UserId == order.UserId)
    .ToListAsync(); // per order

var userData = await _context.UsersData.FindAsync(order.UserId); // per order
```

If 10 orders execute in a single watchdog cycle, this is 20 extra DB queries. `userData` for the same user is fetched repeatedly for each of their orders.

**Proposed solution:**

Pre-load both before the order loop in `ProcessOrdersAsync`:

```csharp
// Before the loop
var distinctUserIds = openOrders.Select(o => o.UserId).Distinct().ToList();
var userDataMap = await _context.UsersData
    .Where(u => distinctUserIds.Contains(u.Id))
    .ToDictionaryAsync(u => u.Id);

// All open orders already loaded — group as a lookup for related orders
var ordersByKey = openOrders
    .GroupBy(o => (o.UserId, o.SignalId, o.Symbol))
    .ToDictionary(g => g.Key, g => g.ToList());
```

Then in `ExecuteOrderAsync`, look up from the pre-built dictionary instead of querying the DB.

---

### S4 — AnalyticsController: N+1 Role Queries

**File:** `Controllers/AnalyticsController.cs` — `Index()`  
**Impact:** 🟠 Calls `GetRolesAsync(user)` inside a `foreach` loop over all users, creating N DB queries.

```csharp
foreach (var user in users)
{
    var roles = await _userManager.GetRolesAsync(user); // N DB hits
    ...
}
```

**Proposed solution:**

Batch the role lookups. The ASP.NET Identity framework stores roles in `AspNetUserRoles`. You can query that table directly via EF:

```csharp
var userIds = users.Select(u => u.Id).ToList();
// Resolve via claims or a single join query on AspNetUserRoles + AspNetRoles
var userRoles = await (
    from ur in _context.Set<IdentityUserRole<string>>()
    join r  in _context.Set<IdentityRole>() on ur.RoleId equals r.Id
    where userIds.Contains(ur.UserId)
    select new { ur.UserId, r.Name }
).ToListAsync();

var rolesByUser = userRoles.GroupBy(x => x.UserId)
    .ToDictionary(g => g.Key, g => g.Select(x => x.Name).ToList());
```

This replaces N queries with 1.

---

### S5 — DynamicSignalParserService: Two Separate DB Queries Per Telegram Message

**File:** `Services/DynamicSignalParserService.cs` — `ParseSignalAsync()`  
**Impact:** 🟠 Every Telegram message triggers two EF queries with `Include` (eager-loading parsing rules), then iterates all providers sequentially.

```csharp
var preferredProviders  = await dbContext.SignalProviders.Include(p => p.ParsingRules)
    .Where(p => p.IsActive && p.TelegramGroupId == telegramGroupId).ToListAsync();
var fallbackProviders = await dbContext.SignalProviders.Include(p => p.ParsingRules)
    .Where(p => p.IsActive && p.TelegramGroupId != telegramGroupId).ToListAsync();
```

Provider configurations and their parsing rules change infrequently (admin-managed). Loading from DB on every message is unnecessary.

**Proposed solution:**

The service already has a `ConcurrentDictionary<int, SignalProviderConfig> _providerConfigCache` but it is not pre-loaded. Pre-load all active providers at startup (or on first call) and refresh only when a `RefreshCacheAsync()` is explicitly triggered (which is already called from the admin editing actions):

```csharp
// On startup or first access:
private async Task EnsureCacheLoadedAsync()
{
    if (_allProvidersCache != null) return;
    using var scope = _scopeFactory.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AutoSignalsDbContext>();
    _allProvidersCache = await db.SignalProviders
        .Include(p => p.ParsingRules)
        .Where(p => p.IsActive)
        .ToListAsync();
}
```

Then `ParseSignalAsync` filters the in-memory list — zero DB calls per message.

---

### S6 — UserOrderWatchDogService: Unnecessary Nested DI Scopes for Error Logging

**File:** `Services/UserOrderWatchDogService.cs`  
**Impact:** 🟠 Throughout the file, a new DI scope is created every time an error needs to be logged, even when an outer scope with a valid `DbContext` already exists.

Example (appears ~15 times):
```csharp
using (var errorLogScope = _scopeFactory.CreateScope())
{
    var errorLogService = errorLogScope.ServiceProvider.GetRequiredService<ErrorLogService>();
    await errorLogService.LogErrorAsync(...);
}
```

`ErrorLogService` creates its own scope internally, so the outer scope here is redundant.

**Proposed solution:**

Inject `ErrorLogService` directly into `UserOrderWatchDogService`'s constructor. Since `ErrorLogService` is registered as `Scoped` and `UserOrderWatchDogService` is a `Singleton`, the correct approach is to resolve `ErrorLogService` through a factory or inject it via the same scope already being used for `_context`. Alternatively, register `ErrorLogService` as `Singleton` (safe because it creates its own scope internally).

---

### S7 — SignalPerformanceService: Multiple SaveChangesAsync Calls Per Tracking Cycle

**File:** `Services/SignalPerformanceService.cs`  
**Impact:** 🟠 A single call to `TrackPerformance()` may call `SaveChangesAsync()` up to 4 times in different methods: inside `HandlePendingSignal`, `HandleOpenSignal`, `CloseSignal`, and again at the end of `TrackPerformance` itself.

For a cycle with 5 open performances where 2 advance, this produces 6–8 DB save round-trips that could be 1.

**Proposed solution:**

Remove all intermediate `SaveChangesAsync()` calls from `HandlePendingSignal`, `HandleOpenSignal`, and `CloseSignal`. Let EF track all changes in the `_context` change tracker, then call `SaveChangesAsync()` exactly once at the end of `TrackPerformance()`. Telegram notifications that depend on a persisted `TelegramMessageId` can be sent optimistically before the save, relying on the fact that `_context` tracks the in-memory value.

---

### S8 — ErrorLogService: DB Write Per Error (No Batching)

**File:** `Services/ErrorLogService.cs`  
**Impact:** 🟠 Each call to `LogErrorAsync` creates a new DI scope, creates a `DbContext`, adds one row, and calls `SaveChangesAsync()`. During error storms (e.g., network issues with an exchange) this can produce hundreds of individual DB writes per minute.

**Proposed solution:**

Use a `Channel<ErrorLog>` or `ConcurrentQueue<ErrorLog>` backed by a short-interval background flusher:

```csharp
// In LogErrorAsync: enqueue only
_queue.Writer.TryWrite(new ErrorLog { ... });

// Background flusher (runs every 5 seconds):
while (await _queue.Reader.WaitToReadAsync())
{
    var batch = new List<ErrorLog>();
    while (_queue.Reader.TryRead(out var log)) batch.Add(log);
    using var scope = _scopeFactory.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AutoSignalsDbContext>();
    db.ErrorLogs.AddRange(batch);
    await db.SaveChangesAsync();
}
```

This converts N individual DB writes into 1 write per 5-second window.

---

## Section 3 – Architecture Issues

---

### A1 — UserOrderWatchDogService: ExecuteAsync Is Dead Code (Never Invoked)

**File:** `Services/UserOrderWatchDogService.cs`, `Program.cs`  
**Impact:** 🟡 Confusing and bug-prone. The service is registered as `AddSingleton`, NOT `AddHostedService`. The `ExecuteAsync` loop is therefore never started by the framework. Order processing only runs because `ExchangeHostedService` calls `TriggerOrderProcessing()` explicitly every minute.

```csharp
// Program.cs
builder.Services.AddSingleton<UserOrderWatchDogService>(); // ← NOT AddHostedService
```

The `while (!stoppingToken.IsCancellationRequested)` loop in `ExecuteAsync` is unreachable code.

**Proposed solution:**

Either:
- Remove the `ExecuteAsync` override and `BackgroundService` inheritance entirely, making it a plain service with a `ProcessOrdersAsync()` method, **or**
- Register it as `AddHostedService<UserOrderWatchDogService>()` and remove the duplicate call from `ExchangeHostedService` — ensuring orders are not processed twice.

The current state is a latent bug: if someone "fixes" the registration to `AddHostedService`, orders will be processed twice per minute.

---

### A2 — Program.cs: AddControllersWithViews() Registered Twice

**File:** `Program.cs` — lines 17 and 32  
**Impact:** 🟡 Minor but wasteful. The service is registered twice:

```csharp
builder.Services.AddControllersWithViews(); // line 17
// ...
builder.Services.AddControllersWithViews(); // line 32
```

The second registration is harmless due to ASP.NET Core's idempotency for this call, but it is noise that hints at copy-paste development.

**Proposed solution:** Remove the duplicate at line 32.

---

### A3 — AutoSignalsDbContext: Hardcoded Connection String in OnConfiguring

**File:** `Data/AutoSignalsDbContext.cs`  
**Impact:** 🟡 A hardcoded `localhost` connection string in `OnConfiguring` acts as a silent fallback that can override the production connection string when `DbContextOptions` are not passed correctly (e.g., in unit tests or migrations run without the host).

```csharp
protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    => optionsBuilder.UseSqlServer("Server=localhost;Database=AutoSignals;...");
```

**Proposed solution:**

Only configure when no options have been set:
```csharp
protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
{
    if (!optionsBuilder.IsConfigured)
    {
        // Only used as a last resort (e.g., EF tooling)
        optionsBuilder.UseSqlServer("Server=localhost;Database=AutoSignals;...");
    }
}
```

---

### A4 — Position.Size Stored as string Instead of a Numeric Type

**File:** `Models/Position.cs`  
**Impact:** 🟡 `Position.Size` is declared as `string`, requiring constant parsing:

```csharp
double currentSize = double.Parse(existingPosition.Size);   // in CreateOrUpdatePositionAsync
existingPosition.Size = newSize.ToString();
```

This wastes DB storage (varchar vs float), prevents SQL-side arithmetic, and adds parsing overhead in hot paths (the watchdog runs every minute).

**Proposed solution:**

Change `Position.Size` from `string` to `double` (or `decimal`). Add a migration for the type change. All references to `double.Parse(position.Size)` become direct property access.

---

### A5 — TrackPageViewAsync Duplicated Across Multiple Controllers

**File:** `Controllers/HomeController.cs`, `Controllers/SignalsController.cs`, `Controllers/ProvidersController.cs`, `Controllers/SignalPerformancesController.cs`  
**Impact:** 🟡 The same method body is copy-pasted into at least 4 controllers. Any future fix to this method must be applied in all copies.

**Proposed solution:**

Move `TrackPageViewAsync` to a dedicated `IAnalyticsTrackingService` (see C2 above). Controllers call a single interface method. This removes the duplication and is the foundation for the batching optimization described in C2.

---

## Section 4 – Optimization Opportunities

---

### O1 — FetchLatestPricesAsync: UsersData Queried on Every Watchdog Tick

**File:** `Services/UserOrderWatchDogService.cs` — `FetchLatestPricesAsync()`  
**Impact:** 🟡 Every minute, the watchdog queries `UsersData` to find users with valid API credentials:

```csharp
userData = await _context.UsersData
    .Where(u => u.ApiTestResult == "1" && u.ExchangeId.HasValue)
    .ToListAsync();
```

User API credentials change rarely (only when a user updates their settings). This result is identical on 99.9% of ticks.

**Proposed solution:**

Cache this result in `IMemoryCache` with a 5-minute expiry. Invalidate the cache from `UsersDataController` when a user updates their credentials.

---

### O2 — Open Position ROI Update: Marks ALL Positions as Modified Every Minute

**File:** `Services/UserOrderWatchDogService.cs` — `ProcessOrdersAsync()`  
**Impact:** 🟡 Every minute, for every open position that has a price entry, the code calls `_context.Positions.Update(position)` regardless of whether the ROI actually changed.

```csharp
foreach (var position in matchingPositions)
{
    position.ROI = CalculateUnrealizedROI(position, (double)currentPrice);
    _context.Positions.Update(position); // always marks modified
}
```

If there are 50 open positions, this generates 50 UPDATE statements every minute even when prices are unchanged.

**Proposed solution:**

Only update if the computed ROI differs from the stored value by more than a meaningful threshold (e.g., 0.01%):

```csharp
var newROI = CalculateUnrealizedROI(position, (double)currentPrice);
if (Math.Abs(newROI - position.ROI) > 0.0001)
{
    position.ROI = newROI;
    _context.Positions.Update(position);
}
```

---

### O3 — BinancePriceService / BybitPriceService: V1 Methods Still Present

**Files:** `Services/BinancePriceService.cs`, `Services/BybitPriceService.cs`  
**Impact:** 🟡 Both services contain legacy V1 `FetchAllAssetPrices` methods that fetch tickers one by one in a serial loop:

```csharp
foreach (var market in markets)
{
    var ticker = await _futures.fetchTicker(market.Symbol); // one API call per symbol
}
```

With ~2 000 symbols this is ~2 000 sequential HTTP calls. The V2 methods batch-fetch all tickers in one API call and are already in use. The V1 methods remain as dead code and create a risk of being called accidentally.

**Proposed solution:**

Delete the V1 methods (`FetchAllBinanceAssetPricesAsync`, `FetchAllBybitAssetPricesAsync`) and the corresponding interface members. All callers already use V2.

---

### O4 — UsersDataController.Details: Unbounded Order Query

**File:** `Controllers/UsersDataController.cs` — `Details()`  
**Impact:** 🟡 All orders for a user are loaded with no upper bound:

```csharp
var orders = await _context.Orders.Where(o => o.UserId == id).ToListAsync();
```

A user who has been active for months could have 10 000+ orders. This query will load them all on every admin page load.

**Proposed solution:**

Add a reasonable limit or pagination:
```csharp
var orders = await _context.Orders
    .Where(o => o.UserId == id)
    .OrderByDescending(o => o.Time)
    .Take(500)
    .ToListAsync();
```

---

### O5 — Random() Instantiated in Hot-Path Methods

**File:** `Services/SignalPerformanceService.cs`  
**Impact:** 🟢 Minor. `GetEncouragingMessage()` and `GetPraiseMessage()` each create `new Random()` on every call. In .NET 8, `Random.Shared` is thread-safe and should be used instead.

```csharp
// Current
var random = new Random();
return messages[random.Next(messages.Count)];

// Proposed
return messages[Random.Shared.Next(messages.Count)];
```

---

### O6 — DynamicSignalParserService: Two DB Queries Split Into Preferred + Fallback

**File:** `Services/DynamicSignalParserService.cs`  
**Impact:** 🟢 Minor once O-level caching is applied (see S5). Currently makes 2 queries; the preferred/fallback distinction can be applied after a single query if providers are already in memory.

---

### O7 — SignalPredictionService: Provider Queried by Name String

**File:** `Services/SignalPredictionService.cs` — `BuildPredictionAsync()`  
**Impact:** 🟢 Minor.

```csharp
var provider = await _context.Provider.AsNoTracking()
    .FirstOrDefaultAsync(p => p.Name == signal.Provider, cancellationToken);
```

Provider lookup is by string `Name` with no index. There is no index on `Provider.Name`. Adding one (or using the provider's ID as a foreign key in `Signal`) would make this query O(log n) instead of O(n).

---

## Section 5 — Response Cache Opportunities

None of the controllers use `[ResponseCache]` or `ETag`-based caching. The following pages serve the same data to every visitor and could benefit from output caching:

| Page / Endpoint | Suggested Cache Strategy |
|---|---|
| `ProvidersController.Index` | Short cache (60 s), vary by nothing |
| `ProvidersController.Details` | Medium cache (5 min), vary by `id` |
| `HomeController.index` (landing page) | Long cache (10 min), public |
| `SignalPerformancesController.Index` | Short cache (60 s), vary by nothing |
| `VipDashboard.GetDashboardData` | Short cache (30 s), vary by `userId` + `timeframe` |

In .NET 8, `app.UseOutputCache()` with attribute-level policies can be added without touching controller logic.

---

## Prioritized Implementation Roadmap

| # | Issue | Effort | Impact | Priority |
|---|---|---|---|---|
| C1 | Add missing DB indexes | Low (migration only) | 🔴 Critical | **P0** |
| C3 | AveragePriceService full table load | Medium | 🔴 Critical | **P0** |
| C4 | SignalPerformanceService scoped price load | Low | 🔴 Critical | **P0** |
| C2 | TrackPageViewAsync batching | Medium | 🔴 Critical | **P1** |
| C5 | GetPrecisions async + parallel | Low | 🔴 Critical | **P1** |
| S2 | Parallel exchange fetches | Low | 🟠 High | **P1** |
| S1 | VipDashboard in-memory date filter | Low | 🟠 High | **P1** |
| S3 | Pre-load relatedOrders and userData | Medium | 🟠 High | **P2** |
| S4 | N+1 role queries in Analytics | Low | 🟠 High | **P2** |
| S5 | DynamicSignalParser provider cache | Medium | 🟠 High | **P2** |
| A1 | Fix UserOrderWatchDog registration | Low | 🟡 Medium | **P2** |
| S7 | Batch SignalPerformance SaveChanges | Low | 🟠 High | **P2** |
| S8 | Error log batching | Medium | 🟠 High | **P3** |
| A4 | Position.Size as numeric | Medium (migration) | 🟡 Medium | **P3** |
| O1 | Cache UsersData in watchdog | Low | 🟡 Medium | **P3** |
| O2 | ROI threshold before update | Low | 🟡 Medium | **P3** |
| O3 | Remove V1 fetch methods | Low | 🟡 Medium | **P3** |
| S6 | Eliminate nested error-log scopes | Low | 🟡 Medium | **P3** |
| A2 | Remove duplicate DI registration | Trivial | 🟡 Low | **P4** |
| A3 | Guard OnConfiguring | Trivial | 🟡 Low | **P4** |
| A5 | Extract TrackPageViewAsync to service | Low | 🟡 Low | **P4** |
| O4 | Limit order query in UsersDataController | Trivial | 🟡 Medium | **P4** |
| O5 | Use Random.Shared | Trivial | 🟢 Low | **P5** |

---

## Appendix: Duplicate Code Inventory

| Method | Files where it appears |
|---|---|
| `TrackPageViewAsync` | `HomeController.cs`, `SignalsController.cs`, `ProvidersController.cs`, `SignalPerformancesController.cs` |
| Retry loop (while retryCount < 3) | `BinancePriceService.cs`, `BybitPriceService.cs`, `BitgetPriceService.cs`, `OkxPriceService.cs`, `KuCoinPriceService.cs` |
| `ErrorLogService` scope creation pattern | ~15 places in `UserOrderWatchDogService.cs` |
| `FetchTickersByTypeAsync` logic | `BinancePriceService.cs`, `BybitPriceService.cs`, `BitgetPriceService.cs` (similar structure, different types) |
| `CalculateEstimatedLiquidation` | `UserOrderWatchDogService.cs`, potentially `OrderService.cs` |

These duplicates should be consolidated as part of the general refactoring effort.
