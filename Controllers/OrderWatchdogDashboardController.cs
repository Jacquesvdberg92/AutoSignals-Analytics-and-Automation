using AutoSignals.Data;
using AutoSignals.ViewModels.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace AutoSignals.Controllers
{
    [Authorize(Roles = "Admin")]
    public class OrderWatchdogDashboardController : Controller
    {
        private readonly AutoSignalsDbContext _context;

        public OrderWatchdogDashboardController(AutoSignalsDbContext context)
        {
            _context = context;
        }

        [HttpGet("/Admin/OrderWatchdogDashboard")]
        [ResponseCache(Duration = 30)]
        public async Task<IActionResult> Index()
        {
            var now = DateTime.UtcNow;
            var since24h = now.AddHours(-24);
            var today = now.Date;

            // ── Raw data pulls (AsNoTracking for perf) ────────────────────────
            var allOrders = await _context.Orders
                .AsNoTracking()
                .Select(o => new
                {
                    o.Id, o.UserId, o.UserName, o.Symbol, o.Status,
                    o.Description, o.Time, o.CloseTime,
                    o.ExchangeOrderStatus, o.IsTest
                })
                .ToListAsync();

            var openPositions = await _context.Positions
                .AsNoTracking()
                .Where(p => p.Status == "OPEN")
                .Select(p => new { p.Id, p.UserId, p.Symbol, p.Side, p.ROI, p.Entry, p.Time, p.IsTest })
                .ToListAsync();

            var closedPositionsToday = await _context.Positions
                .AsNoTracking()
                .Where(p => p.Status == "CLOSED" && p.CloseTime >= today)
                .CountAsync();

            var watchdogErrors = await _context.ErrorLogs
                .AsNoTracking()
                .Where(e => e.Timestamp >= since24h && e.Source != null && e.Source.Contains("UserOrderWatchDogService"))
                .Select(e => new { e.Timestamp, e.Source })
                .ToListAsync();

            // ── Pipeline Health ───────────────────────────────────────────────
            var openOrders = allOrders.Where(o => o.Status == "OPEN").ToList();
            var pendingOrders = allOrders.Where(o => o.Status == "PENDING").ToList();
            var executed24h = allOrders.Where(o => o.Status == "EXECUTED" && o.CloseTime >= since24h).ToList();
            var cancelled24h = allOrders.Where(o => o.Status == "CANCELLED" && o.CloseTime >= since24h).ToList();

            var avgExecMinutes = executed24h
                .Where(o => o.CloseTime.HasValue)
                .Select(o => (o.CloseTime!.Value - o.Time).TotalMinutes)
                .DefaultIfEmpty(0)
                .Average();

            // ── Execution Breakdown ───────────────────────────────────────────
            var entryOrdersOpen = openOrders.Count(o => o.Description == "Initial Entry Order");
            var entryExecuted24h = executed24h.Count(o => o.Description == "Initial Entry Order");
            var entryCancelled24h = cancelled24h.Count(o => o.Description == "Initial Entry Order");
            var dcaOpen = openOrders.Count(o => o.Description != null && o.Description.Contains("DCA"));
            var dcaExecuted24h = executed24h.Count(o => o.Description != null && o.Description.Contains("DCA"));
            var slExecuted24h = executed24h.Count(o => o.Description == "Stoploss Order" || o.Description == "Stoploss On Entry Order");
            var tpExecuted24h = executed24h.Count(o => o.Description != null && o.Description.Contains("Take Profit Order"));
            var mslExecuted24h = executed24h.Count(o => o.Description != null && o.Description.Contains("MSL"));

            // ── Error & Failure ───────────────────────────────────────────────
            var insufficientBalance = allOrders.Count(o =>
                o.ExchangeOrderStatus == "40762" && o.CloseTime >= since24h);
            var minSizeCancellations = allOrders.Count(o =>
                o.ExchangeOrderStatus == "45110" && o.CloseTime >= since24h);
            var priceFetchFailures = watchdogErrors.Count(e =>
                e.Source != null && e.Source.Contains("FetchLatestPricesAsync"));

            // ── Position Health ───────────────────────────────────────────────
            var avgROI = openPositions.Any() ? openPositions.Average(p => p.ROI) : 0;
            var negativeROI = openPositions.Count(p => p.ROI < 0);
            var liquidatedToday = await _context.ErrorLogs
                .AsNoTracking()
                .CountAsync(e => e.Timestamp >= today
                    && e.Message != null
                    && e.Message.Contains("EstLiquidation"));

            // ── Top Users / Symbols ───────────────────────────────────────────
            var topUsersByOpen = openOrders
                .GroupBy(o => o.UserId)
                .Select(g => new UserOrderStat
                {
                    UserId = g.Key,
                    UserName = g.FirstOrDefault()?.UserName ?? g.Key,
                    Count = g.Count()
                })
                .OrderByDescending(x => x.Count)
                .Take(10)
                .ToList();

            var topUsersByCancel = cancelled24h
                .GroupBy(o => o.UserId)
                .Select(g => new UserOrderStat
                {
                    UserId = g.Key,
                    UserName = g.FirstOrDefault()?.UserName ?? g.Key,
                    Count = g.Count()
                })
                .OrderByDescending(x => x.Count)
                .Take(10)
                .ToList();

            var topSymbols = openOrders
                .GroupBy(o => o.Symbol)
                .Select(g => new SymbolOrderStat { Symbol = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .Take(10)
                .ToList();

            // ── Chart: Executed by Hour ───────────────────────────────────────
            var executedByHour = executed24h
                .Where(o => o.CloseTime.HasValue)
                .GroupBy(o => o.CloseTime!.Value.Hour)
                .ToDictionary(g => g.Key, g => g.Count());

            var execLabels = new List<string>();
            var execValues = new List<int>();
            for (int h = 0; h < 24; h++)
            {
                execLabels.Add($"{h:D2}:00");
                execValues.Add(executedByHour.TryGetValue(h, out var v) ? v : 0);
            }

            // ── Chart: Status Pie ─────────────────────────────────────────────
            var statusLabels = new List<string> { "OPEN", "PENDING", "EXECUTED (24h)", "CANCELLED (24h)" };
            var statusValues = new List<int>
            {
                openOrders.Count,
                pendingOrders.Count,
                executed24h.Count,
                cancelled24h.Count
            };

            // ── Chart: Errors by Hour ─────────────────────────────────────────
            var errorByHourDict = watchdogErrors
                .GroupBy(e => e.Timestamp.Hour)
                .ToDictionary(g => g.Key, g => g.Count());

            var errorLabels = new List<string>();
            var errorValues = new List<int>();
            for (int h = 0; h < 24; h++)
            {
                errorLabels.Add($"{h:D2}:00");
                errorValues.Add(errorByHourDict.TryGetValue(h, out var v) ? v : 0);
            }

            // ── Chart: ROI Histogram ──────────────────────────────────────────
            var roiBuckets = new (string Label, double Min, double Max)[]
            {
                ("< -80%", double.MinValue, -80),
                ("-80 to -50%", -80, -50),
                ("-50 to -20%", -50, -20),
                ("-20 to 0%", -20, 0),
                ("0 to +20%", 0, 20),
                ("+20 to +50%", 20, 50),
                ("+50 to +100%", 50, 100),
                ("> +100%", 100, double.MaxValue)
            };

            var roiLabels = roiBuckets.Select(b => b.Label).ToList();
            var roiValues = roiBuckets
                .Select(b => openPositions.Count(p => p.ROI >= b.Min && p.ROI < b.Max))
                .ToList();

            // ── Open Positions Table ──────────────────────────────────────────
            var positionRows = openPositions
                .OrderBy(p => p.ROI)
                .Select(p => new OpenPositionRow
                {
                    Id = p.Id,
                    UserId = p.UserId,
                    Symbol = p.Symbol,
                    Side = p.Side,
                    ROI = Math.Round(p.ROI, 2),
                    Entry = p.Entry,
                    Time = p.Time,
                    IsTest = p.IsTest
                })
                .ToList();

            var vm = new OrderWatchdogDashboardViewModel
            {
                TotalOpenOrders = openOrders.Count,
                TotalPendingOrders = pendingOrders.Count,
                ExecutedLast24h = executed24h.Count,
                CancelledLast24h = cancelled24h.Count,
                AvgExecutionMinutes = Math.Round(avgExecMinutes, 1),

                EntryOrdersOpen = entryOrdersOpen,
                EntryOrdersExecuted24h = entryExecuted24h,
                EntryOrdersCancelled24h = entryCancelled24h,
                DcaOrdersOpen = dcaOpen,
                DcaOrdersExecuted24h = dcaExecuted24h,
                StoplossOrdersExecuted24h = slExecuted24h,
                TakeProfitOrdersExecuted24h = tpExecuted24h,
                MslOrdersExecuted24h = mslExecuted24h,

                InsufficientBalanceCancellations24h = insufficientBalance,
                MinSizeCancellations24h = minSizeCancellations,
                WatchdogErrorCount24h = watchdogErrors.Count,
                PriceFetchFailures24h = priceFetchFailures,

                TotalOpenPositions = openPositions.Count,
                PositionsClosedToday = closedPositionsToday,
                PositionsLiquidatedToday = liquidatedToday,
                AvgOpenROI = Math.Round(avgROI, 2),
                NegativeROIPositions = negativeROI,

                UniqueSymbolsTracked = openOrders.Select(o => o.Symbol).Distinct().Count(),

                TopUsersByOpenOrders = topUsersByOpen,
                TopUsersByCancelledOrders = topUsersByCancel,
                TopSymbolsByOpenOrders = topSymbols,

                ExecutedByHourLabels = execLabels,
                ExecutedByHourValues = execValues,

                StatusPieLabels = statusLabels,
                StatusPieValues = statusValues,

                ErrorByHourLabels = errorLabels,
                ErrorByHourValues = errorValues,

                RoiHistogramLabels = roiLabels,
                RoiHistogramValues = roiValues,

                OpenPositions = positionRows
            };

            ViewBag.ExecutedByHourLabelsJson = JsonSerializer.Serialize(vm.ExecutedByHourLabels);
            ViewBag.ExecutedByHourValuesJson = JsonSerializer.Serialize(vm.ExecutedByHourValues);
            ViewBag.StatusPieLabelsJson = JsonSerializer.Serialize(vm.StatusPieLabels);
            ViewBag.StatusPieValuesJson = JsonSerializer.Serialize(vm.StatusPieValues);
            ViewBag.ErrorByHourLabelsJson = JsonSerializer.Serialize(vm.ErrorByHourLabels);
            ViewBag.ErrorByHourValuesJson = JsonSerializer.Serialize(vm.ErrorByHourValues);
            ViewBag.RoiHistogramLabelsJson = JsonSerializer.Serialize(vm.RoiHistogramLabels);
            ViewBag.RoiHistogramValuesJson = JsonSerializer.Serialize(vm.RoiHistogramValues);

            return View("~/Views/Admin/OrderWatchdogDashboard.cshtml", vm);
        }
    }
}
