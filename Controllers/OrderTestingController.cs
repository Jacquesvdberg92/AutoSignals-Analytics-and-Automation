using AutoSignals.Data;
using AutoSignals.Models;
using AutoSignals.Services;
using AutoSignals.Services.ExchangeAdapters;
using AutoSignals.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AutoSignals.Controllers
{
    [Authorize(Roles = "Admin")]
    public class OrderTestingController : Controller
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ErrorLogService _errorLogService;
        private readonly ExchangeOrderAdapterFactory _adapterFactory;

        private const string TestSymbol = "BTC/USDT:USDT";
        private const int TestLeverage = 20;
        private const decimal TestMarginUsdt = 20m;

        public OrderTestingController(
            IServiceScopeFactory scopeFactory,
            ErrorLogService errorLogService,
            ExchangeOrderAdapterFactory adapterFactory)
        {
            _scopeFactory = scopeFactory;
            _errorLogService = errorLogService;
            _adapterFactory = adapterFactory;
        }

        [HttpGet]
        public IActionResult Index()
        {
            return View("~/Views/TestingDevelopment/Index.cshtml", new TestSequenceViewModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Index(TestSequenceViewModel vm)
        {
            if (!ModelState.IsValid)
                return View("~/Views/TestingDevelopment/Index.cshtml", vm);

            void Log(string msg) => vm.Logs.Add(msg);

            try
            {
                IExchangeOrderAdapter adapter;
                try
                {
                    adapter = await _adapterFactory.GetRequiredAdapterAsync(vm.Exchange);
                }
                catch (Exception ex)
                {
                    Log($"❌ Could not resolve exchange '{vm.Exchange}': {ex.Message}");
                    return View("~/Views/TestingDevelopment/Index.cshtml", vm);
                }

                var exchangeId = NormalizeExchange(vm.Exchange);
                var creds = new ExchangeCredentials(vm.ApiKey, vm.ApiSecret, string.IsNullOrWhiteSpace(vm.Password) ? null : vm.Password);

                // ── Step 1: Balance check ────────────────────────────────────────
                Log($"🔍 Step 1: Checking USDT futures balance on {adapter.ExchangeName}...");
                var balance = await adapter.GetBalanceAsync(creds);
                Log($"   → Available: {balance:F2} USDT");

                if (balance < TestMarginUsdt)
                {
                    Log($"   ✗ Insufficient balance — need ≥ ${TestMarginUsdt:F2} USDT. Aborting.");
                    return View("~/Views/TestingDevelopment/Index.cshtml", vm);
                }
                Log("   ✓ Balance check passed.");

                // ── Step 2: Live BTC price ───────────────────────────────────────
                Log($"🔍 Step 2: Fetching live {TestSymbol} price...");
                var price = await adapter.FetchPriceAsync(TestSymbol, creds);

                if (!price.HasValue || price.Value <= 0)
                {
                    Log("   ✗ Failed to fetch price. Aborting.");
                    return View("~/Views/TestingDevelopment/Index.cshtml", vm);
                }
                Log($"   → BTC price: {price.Value:F2} USDT");
                Log("   ✓ Price fetched.");

                // ── Step 3: Calculate order parameters ──────────────────────────
                Log("🔍 Step 3: Calculating order parameters...");
                var notional = TestMarginUsdt * TestLeverage;
                var size = (double)(notional / price.Value);
                var slPrice = vm.Direction.ToLower() == "buy"
                    ? (double)(price.Value * 0.97m)
                    : (double)(price.Value * 1.03m);
                Log($"   → Margin: ${TestMarginUsdt:F2} × {TestLeverage}× = ${notional:F2} notional");
                Log($"   → Order size: {size:F6} BTC @ {price.Value:F2} USDT");
                Log($"   → Stop price: {slPrice:F2} USDT ({(vm.Direction.ToLower() == "buy" ? "−3%" : "+3%")} from entry)");

                // All adapters expect the close direction (opposite of entry) for TP/SL orders
                var closeDirection = vm.Direction.Equals("buy", StringComparison.OrdinalIgnoreCase) ? "sell" : "buy";

                // ── Step 4: Entry order ──────────────────────────────────────────
                Log("🔍 Step 4: Sending entry order...");
                var entryOrder = new Order
                {
                    UserId = "TEST",
                    ExchangeId = exchangeId.ToString(),
                    Symbol = TestSymbol,
                    Side = vm.Direction,
                    Price = (double)price.Value,
                    Stoploss = slPrice,
                    Size = size,
                    Leverage = TestLeverage,
                    Status = "OPEN",
                    Description = "Initial Entry Order",
                    IsTest = true,
                    IsIsolated = true
                };

                var entryResult = await adapter.SendEntryOrderAsync(entryOrder, creds);

                if (!entryResult.Success)
                {
                    Log($"   ✗ Entry failed: {entryResult.ErrorMessage ?? "Unknown error"} (code: {entryResult.ErrorCode})");
                    return View("~/Views/TestingDevelopment/Index.cshtml", vm);
                }
                Log("   ✓ Entry order placed.");
                if (entryResult.ExternalOrderId != null)
                    Log($"   → Exchange order ID: {entryResult.ExternalOrderId}");

                // ── Step 5: Record test position ────────────────────────────────
                Log("🔍 Step 5: Recording test position in database...");
                var positionId = await CreateOrUpdateTestPositionAsync(entryOrder, price.Value);
                Log($"   ✓ Position ID: {positionId}");

                // ── Step 6: Take Profit at 50% ──────────────────────────────────
                Log("🔍 Step 6: Sending Take Profit order (50% of position)...");
                var tpOrder = new Order
                {
                    UserId = "TEST",
                    ExchangeId = exchangeId.ToString(),
                    Symbol = TestSymbol,
                    Side = closeDirection,
                    Price = (double)price.Value,
                    Size = 50.0,    // percentage per project convention
                    Leverage = TestLeverage,
                    Status = "OPEN",
                    Description = "Take Profit Order 1",
                    PositionId = positionId.ToString(),
                    IsTest = true,
                    IsIsolated = true
                };

                var tpResult = await adapter.SendTakeProfitOrderAsync(tpOrder, creds);

                if (!tpResult.Success)
                {
                    Log($"   ✗ Take Profit failed: {tpResult.ErrorMessage ?? "Unknown error"}");
                    Log("   ⚠ Continuing to Stop Loss step...");
                }
                else
                {
                    Log("   ✓ Take Profit order sent.");
                    if (tpResult.ExternalOrderId != null)
                        Log($"   → Order ID: {tpResult.ExternalOrderId}");
                    await UpdateTestPositionSizeAsync(positionId, 0.5);
                }

                // ── Step 7: Stop Loss for remaining position ────────────────────
                Log("🔍 Step 7: Sending Stop Loss order (remaining position)...");
                var slOrder = new Order
                {
                    UserId = "TEST",
                    ExchangeId = exchangeId.ToString(),
                    Symbol = TestSymbol,
                    Side = closeDirection,
                    Price = (double)price.Value,
                    Stoploss = slPrice,
                    Size = size,
                    Leverage = TestLeverage,
                    Status = "OPEN",
                    Description = "Stoploss Order",
                    PositionId = positionId.ToString(),
                    IsTest = true,
                    IsIsolated = true
                };

                var slResult = await adapter.SendStoplossOrderAsync(slOrder, creds);

                if (!slResult.Success)
                    Log($"   ✗ Stop Loss failed: {slResult.ErrorMessage ?? "Unknown error"}");
                else
                {
                    Log("   ✓ Stop Loss order sent.");
                    if (slResult.ExternalOrderId != null)
                        Log($"   → Order ID: {slResult.ExternalOrderId}");
                }

                Log("✅ Full test sequence completed.");
                vm.IsCompleted = true;
            }
            catch (Exception ex)
            {
                vm.Logs.Add($"❌ Unexpected error: {ex.Message}");
                await _errorLogService.LogErrorAsync(ex.Message, ex.StackTrace, nameof(Index), "OrderTestSequence");
            }

            return View("~/Views/TestingDevelopment/Index.cshtml", vm);
        }

        private async Task<int> CreateOrUpdateTestPositionAsync(Order order, decimal executedPrice)
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AutoSignalsDbContext>();

            // Close any stale open test positions for this exchange+symbol+side so sizes
            // from previous test runs don't accumulate across runs or across exchanges.
            var stale = await context.Positions
                .Where(p =>
                    p.UserId == "TEST" &&
                    p.ExchangeId == order.ExchangeId &&
                    p.Symbol == order.Symbol &&
                    p.Side == order.Side &&
                    p.Status == "OPEN")
                .ToListAsync();

            foreach (var pos in stale)
            {
                pos.Status = "CLOSED";
                pos.CloseTime = DateTime.UtcNow;
            }

            var position = new Position
            {
                UserId = "TEST",
                ExchangeId = order.ExchangeId,
                TelegramId = "TEST",
                Side = order.Side,
                Size = order.Size,
                Leverage = (int)order.Leverage,
                Symbol = order.Symbol,
                Entry = (double)executedPrice,
                Stoploss = order.Stoploss ?? 0,
                ROI = 0,
                Status = "OPEN",
                IsTest = true,
                Time = DateTime.UtcNow,
                EstLiquidation = CalculateEstimatedLiquidation((double)executedPrice, (int)order.Leverage, order.Side)
            };

            context.Positions.Add(position);
            await context.SaveChangesAsync();
            order.PositionId = position.Id.ToString();
            return position.Id;
        }

        private async Task UpdateTestPositionSizeAsync(int positionId, double remainingFactor)
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AutoSignalsDbContext>();
            var position = await context.Positions.FindAsync(positionId);
            if (position != null)
            {
                position.Size *= remainingFactor;
                await context.SaveChangesAsync();
            }
        }

        private double CalculateEstimatedLiquidation(double entryPrice, int leverage, string side)
        {
            if (leverage <= 0) leverage = 1;
            return side.ToLower() switch
            {
                "buy" => Math.Round(entryPrice * (1 - (1.0 / leverage)), 8),
                "sell" => Math.Round(entryPrice * (1 + (1.0 / leverage)), 8),
                _ => entryPrice
            };
        }

        private static int NormalizeExchange(string? exchange)
        {
            if (int.TryParse(exchange, out var id))
                return id;

            var val = (exchange ?? "").Trim().ToUpperInvariant();
            return val switch
            {
                "BITGET"  => 1,
                "BINANCE" => 2,
                "BYBIT"   => 3,
                "OKX"     => 4,
                "KUCOIN"  => 5,
                _ => 0
            };
        }
    }
}