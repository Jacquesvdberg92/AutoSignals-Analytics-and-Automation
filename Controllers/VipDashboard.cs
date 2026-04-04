using Microsoft.AspNetCore.Mvc;
using AutoSignals.Models;
using System.Collections.Generic;
using System.Linq;
using AutoSignals.Data;
using Microsoft.AspNetCore.Identity;
using AutoSignals.Services;
using Microsoft.EntityFrameworkCore;
using AutoSignals.ViewModels;
using Microsoft.AspNetCore.Authorization;
using System.Text.Json;

namespace AutoSignals.Controllers
{
    [Authorize]
    public class VipDashboard : Controller
    {
        private readonly AutoSignalsDbContext _context;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly ErrorLogService _errorLogService;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<VipDashboard> _logger;
        private readonly UserOrderWatchDogService _orderWatchDogService;
        private readonly AesEncryptionService _encryptionService;

        public VipDashboard(AutoSignalsDbContext context, UserManager<IdentityUser> userManager,
                          ErrorLogService errorLogService, IServiceScopeFactory scopeFactory,
                          ILogger<VipDashboard> logger, UserOrderWatchDogService orderWatchDogService,
                          AesEncryptionService encryptionService)
        {
            _context = context;
            _userManager = userManager;
            _errorLogService = errorLogService;
            _scopeFactory = scopeFactory;
            _logger = logger;
            _orderWatchDogService = orderWatchDogService;
            _encryptionService = encryptionService;
        }

        public async Task<IActionResult> Index(string? userId, int? timeframe, DateTime? startDate, DateTime? endDate)
        {
            using (var scope = _scopeFactory.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<AutoSignalsDbContext>();

                userId ??= _userManager.GetUserId(User);

                if (userId != _userManager.GetUserId(User) && !User.IsInRole("Admin"))
                {
                    return Forbid();
                }

                var (start, end) = ResolveDateRange(timeframe, startDate, endDate);

                // Get user data
                var user = await _userManager.FindByIdAsync(userId);
                var userData = await context.UsersData.FirstOrDefaultAsync(u => u.Id == userId);
                var userName = userData?.NickName ?? user?.UserName;

                // Get positions and orders — date filter pushed to SQL
                var positionsInRange = await context.Positions
                    .Where(p => p.UserId == userId && p.Time >= start && p.Time <= end)
                    .ToListAsync();

                var ordersInRange = await context.Orders
                    .Where(o => o.UserId == userId && o.Time >= start && o.Time <= end)
                    .ToListAsync();

                // Open positions and open order count queried directly — avoids loading full history
                var openPositions = await context.Positions
                    .Where(p => p.UserId == userId && p.Status == "OPEN")
                    .ToListAsync();

                var openOrdersCount = await context.Orders
                    .CountAsync(o => o.UserId == userId && o.Status == "OPEN");

                // Get current prices for open positions
                var positionSymbols = openPositions.Select(p => p.Symbol).Distinct().ToList();
                var currentPrices = await GetLatestPricesBySymbolAsync(context, positionSymbols);

                // Calculate current P&L for open positions
                var openPositionsWithPnL = openPositions.Select(p =>
                {
                    decimal currentPrice = 0;
                    if (currentPrices.TryGetValue(p.Symbol, out var price))
                    {
                        currentPrice = price;
                    }

                    var positionPnL = CalculatePositionPnL(p, currentPrice);
                    return new OpenPositionViewModel
                    {
                        Position = p,
                        CurrentPrice = currentPrice,
                        CurrentPnL = positionPnL.CurrentPnL,
                        CurrentROI = positionPnL.CurrentROI
                    };
                }).ToList();

                // Calculate statistics
                var stats = CalculateDashboardStatistics(positionsInRange, ordersInRange, openPositions, openOrdersCount);

                // Calculate additional metrics
                var (bestDay, worstDay, avgWin, avgLoss, profitFactor) = CalculateAdvancedMetrics(positionsInRange);

                // Populate ViewModel
                var viewModel = new VipDashboardViewModel
                {
                    UserId = userId,
                    UserName = userName,
                    UserPositions = positionsInRange,
                    AllOrders = ordersInRange,

                    // Basic counts
                    OpenPositionsCount = stats.OpenPositionsCount,
                    ClosedPositionsCount = stats.ClosedPositionsCount,
                    TotalPositionCount = stats.TotalPositionCount,
                    OpenOrdersCount = stats.OpenOrdersCount,
                    ClosedOrdersCount = stats.ClosedOrdersCount,
                    TotalOrderCount = stats.TotalOrderCount,
                    PendingOrderCount = stats.PendingOrderCount,
                    CancelledOrderCount = stats.CancelledOrderCount,

                    // ROI metrics
                    TotalROI = stats.TotalROI,
                    AverageROI = stats.AverageROI,
                    HighestROI = stats.HighestROI,
                    LowestROI = stats.LowestROI,
                    OpenPositionsROI = stats.OpenPositionsROI,
                    ClosedPositionsROI = stats.ClosedPositionsROI,
                    TotalPositionsROI = stats.TotalPositionsROI,

                    // Win rates
                    WinRate = stats.WinRate,
                    LossRate = stats.LossRate,
                    LongWinRate = stats.LongWinRate,
                    ShortWinRate = stats.ShortWinRate,

                    // P&L
                    TotalProfit = stats.TotalProfit,
                    TotalLoss = stats.TotalLoss,
                    NetPNL = stats.NetPNL,

                    // Symbol performance
                    MostTradedSymbol = stats.MostTradedSymbol,
                    BestPerformingSymbol = stats.BestPerformingSymbol,
                    WorstPerformingSymbol = stats.WorstPerformingSymbol,

                    // Trade characteristics
                    AverageTradeDuration = stats.AverageTradeDuration.ToString(),
                    HighestLeverage = (int)stats.HighestLeverage,
                    AverageLeverage = (int)stats.AverageLeverage,
                    LowestLeverage = (int)stats.LowestLeverage,

                    // Size metrics
                    AverageTradeSize = stats.AverageTradeSize,
                    LargestTradeSize = stats.LargestTradeSize,
                    SmallestTradeSize = stats.SmallestTradeSize,
                    TotalTradeVolume = stats.TotalTradeVolume,

                    // ROI data for charts
                    RoiOverTime = stats.RoiOverTime,
                    ROIBySymbol = stats.ROIBySymbol,

                    // Advanced metrics
                    BestDay = bestDay,
                    WorstDay = worstDay,
                    AverageWin = avgWin,
                    AverageLoss = avgLoss,
                    ProfitFactor = profitFactor,

                    // Real-time data
                    OpenPositionsWithPnL = openPositionsWithPnL,

                    // Date range
                    StartDate = start,
                    EndDate = end
                };

                return View(viewModel);
            }
        }

        // New AJAX endpoint for getting dashboard data
        [HttpGet]
        public async Task<IActionResult> GetDashboardData(string? userId = null, int? timeframe = null, DateTime? startDate = null, DateTime? endDate = null)
        {
            using (var scope = _scopeFactory.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<AutoSignalsDbContext>();

                userId ??= _userManager.GetUserId(User);

                // Authorization check
                if (userId != _userManager.GetUserId(User) && !User.IsInRole("Admin"))
                {
                    return Json(new { success = false, message = "Unauthorized" });
                }

                var (start, end) = ResolveDateRange(timeframe, startDate, endDate);

                // Get positions — date filter pushed to SQL
                var positionsInRange = await context.Positions
                    .Where(p => p.UserId == userId && p.Time >= start && p.Time <= end)
                    .ToListAsync();

                var openPositions = await context.Positions
                    .Where(p => p.UserId == userId && p.Status == "OPEN")
                    .ToListAsync();

                var openOrdersCount = await context.Orders
                    .CountAsync(o => o.UserId == userId && o.Status == "OPEN");

                // Get current prices for open positions
                var positionSymbols = openPositions.Select(p => p.Symbol).Distinct().ToList();
                var currentPrices = await GetLatestPricesBySymbolAsync(context, positionSymbols);

                // Calculate real-time P&L
                var realTimePnL = new List<object>();
                decimal totalOpenPnL = 0;

                foreach (var position in openPositions)
                {
                    decimal currentPrice = 0;
                    if (currentPrices.TryGetValue(position.Symbol, out var price))
                    {
                        currentPrice = price;
                    }

                    var pnl = CalculatePositionPnL(position, currentPrice);
                    totalOpenPnL += pnl.CurrentPnL;

                    realTimePnL.Add(new
                    {
                        PositionId = position.Id,
                        Symbol = position.Symbol,
                        CurrentPrice = currentPrice,
                        CurrentPnL = pnl.CurrentPnL,
                        CurrentROI = pnl.CurrentROI,
                        Side = position.Side,
                        Size = position.Size,
                        Entry = position.Entry
                    });
                }

                // Get quick stats
                var stats = CalculateQuickStats(positionsInRange, openPositions.Count);

                return Json(new
                {
                    success = true,
                    realTimePnL,
                    totalOpenPnL,
                    openPositionsCount = openPositions.Count,
                    openOrdersCount,
                    stats
                });
            }
        }

        private static (DateTime Start, DateTime End) ResolveDateRange(int? timeframe, DateTime? startDate, DateTime? endDate)
        {
            if (startDate.HasValue && endDate.HasValue)
            {
                var start = startDate.Value.Date;
                var end = endDate.Value.Date.AddDays(1).AddTicks(-1); // inclusive end-of-day
                return (start, end);
            }

            var days = timeframe ?? 30;
            if (days > 90) days = 90;
            var fallbackStart = DateTime.UtcNow.AddDays(-days);
            var fallbackEnd = DateTime.UtcNow;
            return (fallbackStart, fallbackEnd);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ClosePosition(int positionId)
        {
            try
            {
                using (var scope = _scopeFactory.CreateScope())
                {
                    var context = scope.ServiceProvider.GetRequiredService<AutoSignalsDbContext>();

                    var position = await context.Positions.FirstOrDefaultAsync(p => p.Id == positionId);
                    if (position == null)
                    {
                        return Json(new { success = false, message = "Position not found" });
                    }

                    // Check authorization
                    if (position.UserId != _userManager.GetUserId(User) && !User.IsInRole("Admin"))
                    {
                        return Json(new { success = false, message = "Unauthorized" });
                    }

                    if (position.Status != "OPEN")
                    {
                        return Json(new { success = false, message = "Position is not open" });
                    }

                    var price = await context.GeneralAssetPrices
                        .FirstOrDefaultAsync(p => p.Symbol == position.Symbol);

                    var stoplossOrder = await context.Orders
                        .FirstOrDefaultAsync(o => o.Description != null &&
                                                 o.Description.Contains("Stoploss") &&
                                                 (o.PositionId == position.Id.ToString() ||
                                                  (o.Symbol == position.Symbol && o.UserId == position.UserId)));

                    if (stoplossOrder == null)
                    {
                        return Json(new { success = false, message = "No stoploss order found" });
                    }

                    var userData = await context.UsersData.FirstOrDefaultAsync(u => u.Id == position.UserId);
                    if (userData == null)
                    {
                        return Json(new { success = false, message = "User data not found" });
                    }

                    // Execute the stoploss
                    if (!stoplossOrder.IsTest)
                    {
                        await _orderWatchDogService.HandleExchangeStoplossOrderAsync(stoplossOrder, userData);
                    }
                    

                    // Close the position
                    await _orderWatchDogService.CloseOrdersAndPositionAsync(position.Id, price?.Price ?? 0);

                    return Json(new { success = true, message = "Position closed successfully" });
                }
            }
            catch (Exception ex)
            {
                await _errorLogService.LogErrorAsync(ex.Message, ex.StackTrace, "VipDashboard.ClosePosition", $"PositionId: {positionId}");
                return Json(new { success = false, message = "Error closing position: " + ex.Message });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CloseAllPositions()
        {
            try
            {
                using (var scope = _scopeFactory.CreateScope())
                {
                    var context = scope.ServiceProvider.GetRequiredService<AutoSignalsDbContext>();
                    var userId = _userManager.GetUserId(User);

                    var openPositions = await context.Positions
                        .Where(p => p.UserId == userId && p.Status == "OPEN")
                        .ToListAsync();

                    if (!openPositions.Any())
                    {
                        return Json(new { success = false, message = "No open positions found" });
                    }

                    var closedCount = 0;
                    foreach (var position in openPositions)
                    {
                        try
                        {
                            var price = await context.GeneralAssetPrices
                                .FirstOrDefaultAsync(p => p.Symbol == position.Symbol);

                            var stoplossOrder = await context.Orders
                                .FirstOrDefaultAsync(o => o.Description != null &&
                                                         o.Description.Contains("Stoploss") &&
                                                         (o.PositionId == position.Id.ToString() ||
                                                          (o.Symbol == position.Symbol && o.UserId == position.UserId)));

                            if (stoplossOrder != null)
                            {
                                var userData = await context.UsersData.FirstOrDefaultAsync(u => u.Id == position.UserId);
                                if (userData != null)
                                {
                                    if (!stoplossOrder.IsTest)
                                    {
                                        await _orderWatchDogService.HandleExchangeStoplossOrderAsync(stoplossOrder, userData);
                                    }
                                   
                                    await _orderWatchDogService.CloseOrdersAndPositionAsync(position.Id, price?.Price ?? 0);
                                    closedCount++;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error closing position {PositionId}", position.Id);
                        }
                    }

                    return Json(new
                    {
                        success = true,
                        message = $"Closed {closedCount} of {openPositions.Count} positions"
                    });
                }
            }
            catch (Exception ex)
            {
                await _errorLogService.LogErrorAsync(ex.Message, ex.StackTrace, "VipDashboard.CloseAllPositions", null);
                return Json(new { success = false, message = "Error closing positions: " + ex.Message });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CloseOrder(int orderId)
        {
            try
            {
                using (var scope = _scopeFactory.CreateScope())
                {
                    var context = scope.ServiceProvider.GetRequiredService<AutoSignalsDbContext>();

                    var order = await context.Orders.FirstOrDefaultAsync(o => o.Id == orderId);
                    if (order == null)
                    {
                        return Json(new { success = false, message = "Order not found" });
                    }

                    // Check authorization
                    if (order.UserId != _userManager.GetUserId(User) && !User.IsInRole("Admin"))
                    {
                        return Json(new { success = false, message = "Unauthorized" });
                    }

                    if (order.Status != "OPEN")
                    {
                        return Json(new { success = false, message = "Order is not open" });
                    }

                    order.Status = "CLOSED";
                    order.CloseTime = DateTime.UtcNow;
                    context.Orders.Update(order);
                    await context.SaveChangesAsync();

                    return Json(new { success = true, message = "Order closed successfully" });
                }
            }
            catch (Exception ex)
            {
                await _errorLogService.LogErrorAsync(ex.Message, ex.StackTrace, "VipDashboard.CloseOrder", $"OrderId: {orderId}");
                return Json(new { success = false, message = "Error closing order: " + ex.Message });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CancelOrder(int orderId)
        {
            try
            {
                using (var scope = _scopeFactory.CreateScope())
                {
                    var context = scope.ServiceProvider.GetRequiredService<AutoSignalsDbContext>();

                    var order = await context.Orders.FirstOrDefaultAsync(o => o.Id == orderId);
                    if (order == null)
                    {
                        return Json(new { success = false, message = "Order not found" });
                    }

                    if (order.UserId != _userManager.GetUserId(User) && !User.IsInRole("Admin"))
                    {
                        return Json(new { success = false, message = "Unauthorized" });
                    }

                    if (order.Status == "CLOSED" || order.Status == "CANCELLED")
                    {
                        return Json(new { success = false, message = "Order is already closed or cancelled" });
                    }

                    order.Status = "CANCELLED";
                    order.CloseTime = DateTime.UtcNow;
                    context.Orders.Update(order);
                    await context.SaveChangesAsync();

                    return Json(new { success = true, message = "Order cancelled successfully" });
                }
            }
            catch (Exception ex)
            {
                await _errorLogService.LogErrorAsync(ex.Message, ex.StackTrace, "VipDashboard.CancelOrder", $"OrderId: {orderId}");
                return Json(new { success = false, message = "Error cancelling order: " + ex.Message });
            }
        }

        #region Helper Methods

        private (decimal CurrentPnL, decimal CurrentROI) CalculatePositionPnL(Position position, decimal currentPrice)
        {
            if (currentPrice == 0 || position.Leverage == 0 || !TryGetPositionSize(position, out var size))
                return (0, 0);

            decimal entryValue = (decimal)position.Entry * size;
            decimal currentValue = currentPrice * size;

            if (entryValue == 0)
                return (0, 0);

            decimal pnl = position.Side == "buy"
                ? (currentValue - entryValue) / position.Leverage
                : (entryValue - currentValue) / position.Leverage;

            decimal roi = (pnl / entryValue) * 100 * position.Leverage;

            return (pnl, roi);
        }

        private DashboardStatistics CalculateDashboardStatistics(
            List<Position> positionsInRange,
            List<Order> ordersInRange,
            List<Position> openPositions,
            int openOrdersCount)
        {
            var stats = new DashboardStatistics();

            // Basic counts
            stats.OpenPositionsCount = openPositions.Count;
            stats.ClosedPositionsCount = positionsInRange.Count(p => p.Status == "CLOSED");
            stats.TotalPositionCount = positionsInRange.Count;

            stats.OpenOrdersCount = openOrdersCount;
            stats.ClosedOrdersCount = ordersInRange.Count(o => o.Status == "CLOSED");
            stats.TotalOrderCount = ordersInRange.Count;
            stats.PendingOrderCount = ordersInRange.Count(o => o.Status == "PENDING");
            stats.CancelledOrderCount = ordersInRange.Count(o => o.Status == "CANCELLED");

            // ROI calculations
            stats.TotalROI = Math.Round(positionsInRange.DefaultIfEmpty().Sum(p => p?.ROI ?? 0), 2);
            stats.AverageROI = Math.Round(positionsInRange.Any() ? positionsInRange.Average(p => p.ROI) : 0, 2);
            stats.HighestROI = positionsInRange.Any() ? Math.Round(positionsInRange.Max(p => p.ROI), 2) : 0;
            stats.LowestROI = positionsInRange.Any() ? Math.Round(positionsInRange.Min(p => p.ROI), 2) : 0;
            stats.OpenPositionsROI = Math.Round(positionsInRange.Where(p => p.Status == "OPEN").Sum(p => p.ROI), 2);
            stats.ClosedPositionsROI = Math.Round(positionsInRange.Where(p => p.Status == "CLOSED").Sum(p => p.ROI), 2);
            stats.TotalPositionsROI = Math.Round(positionsInRange.Sum(p => p.ROI), 2);

            // ROI over time
            stats.RoiOverTime = positionsInRange
                .GroupBy(p => p.Time.Date)
                .Select(g => new RoiOverTime
                {
                    Date = g.Key,
                    TotalROI = Math.Round(g.Sum(p => p.ROI), 2),
                    AverageROI = Math.Round(g.Average(p => p.ROI), 2),
                    OpenROI = Math.Round(g.Where(p => p.Status == "OPEN").Select(p => p.ROI).DefaultIfEmpty(0).Sum(), 2),
                    ClosedROI = Math.Round(g.Where(p => p.Status == "CLOSED").Select(p => p.ROI).DefaultIfEmpty(0).Sum(), 2)
                })
                .OrderBy(r => r.Date)
                .ToList();

            // Win rates
            stats.WinRate = positionsInRange.Any()
                ? Math.Round(positionsInRange.Count(p => p.ROI > 0) * 100.0 / positionsInRange.Count, 2)
                : 0;
            stats.LossRate = 100 - stats.WinRate;

            var longPositions = positionsInRange.Where(p => p.Side == "buy").ToList();
            var shortPositions = positionsInRange.Where(p => p.Side == "sell").ToList();

            stats.LongWinRate = longPositions.Any()
                ? Math.Round(longPositions.Count(p => p.ROI > 0) * 100.0 / longPositions.Count, 2)
                : 0;
            stats.ShortWinRate = shortPositions.Any()
                ? Math.Round(shortPositions.Count(p => p.ROI > 0) * 100.0 / shortPositions.Count, 2)
                : 0;

            // ROI by symbol
            stats.ROIBySymbol = positionsInRange
                .GroupBy(p => p.Symbol)
                .Select(g => new RoiBySymbol
                {
                    Symbol = g.Key,
                    AvgROI = Math.Round(g.Average(p => p.ROI), 2),
                    Count = g.Count()
                })
                .ToList();

            // P&L calculations
            var profitablePositions = positionsInRange.Where(p => p.ROI > 0 && p.Leverage > 0);
            var losingPositions = positionsInRange.Where(p => p.ROI < 0 && p.Leverage > 0);

            stats.TotalProfit = Math.Round(profitablePositions
                .Sum(CalculatePositionProfitLoss), 2);

            stats.TotalLoss = Math.Round(losingPositions
                .Sum(CalculatePositionProfitLoss), 2);

            stats.NetPNL = Math.Round(stats.TotalProfit + stats.TotalLoss, 2);

            // Symbol performance
            stats.MostTradedSymbol = positionsInRange
                .GroupBy(p => p.Symbol)
                .OrderByDescending(g => g.Count())
                .Select(g => g.Key)
                .FirstOrDefault() ?? "N/A";

            stats.BestPerformingSymbol = positionsInRange
                .GroupBy(p => p.Symbol)
                .Where(g => g.Any())
                .OrderByDescending(g => g.Average(p => p.ROI))
                .Select(g => g.Key)
                .FirstOrDefault() ?? "N/A";

            stats.WorstPerformingSymbol = positionsInRange
                .GroupBy(p => p.Symbol)
                .Where(g => g.Any())
                .OrderBy(g => g.Average(p => p.ROI))
                .Select(g => g.Key)
                .FirstOrDefault() ?? "N/A";

            // Trade characteristics
            var closedPositions = positionsInRange.Where(p => p.CloseTime.HasValue).ToList();
            stats.AverageTradeDuration = closedPositions.Any()
                ? closedPositions.Average(p => (p.CloseTime.Value - p.Time).TotalHours)
                : 0;

            stats.HighestLeverage = positionsInRange.Any() ? positionsInRange.Max(p => p.Leverage) : 0;
            stats.AverageLeverage = positionsInRange.Any() ? Math.Round(positionsInRange.Average(p => p.Leverage), 2) : 0;
            stats.LowestLeverage = positionsInRange.Any() ? positionsInRange.Min(p => p.Leverage) : 0;

            // Size metrics
            var notionalSizes = positionsInRange
                .Select(TryCalculateNotionalSize)
                .Where(size => size.HasValue)
                .Select(size => size!.Value)
                .ToList();

            stats.AverageTradeSize = notionalSizes.Any() ? Math.Round(notionalSizes.Average(), 2) : 0;
            stats.LargestTradeSize = notionalSizes.Any() ? Math.Round(notionalSizes.Max(), 2) : 0;
            stats.SmallestTradeSize = notionalSizes.Any() ? Math.Round(notionalSizes.Min(), 2) : 0;
            stats.TotalTradeVolume = notionalSizes.Any() ? Math.Round(notionalSizes.Sum(), 2) : 0;

            return stats;
        }

        private (string BestDay, string WorstDay, double AverageWin, double AverageLoss, double ProfitFactor)
            CalculateAdvancedMetrics(List<Position> positions)
        {
            if (!positions.Any())
                return ("N/A", "N/A", 0, 0, 0);

            // Best/Worst day
            var dailyPerformance = positions
                .GroupBy(p => p.Time.Date)
                .Select(g => new
                {
                    Date = g.Key,
                    TotalROI = g.Sum(p => p.ROI),
                    TotalPnL = g.Sum(CalculatePositionProfitLoss)
                })
                .OrderByDescending(x => x.TotalROI)
                .ToList();

            var bestDay = dailyPerformance.FirstOrDefault();
            var worstDay = dailyPerformance.LastOrDefault();

            // Win/Loss metrics
            var wins = positions.Where(p => p.ROI > 0).ToList();
            var losses = positions.Where(p => p.ROI < 0).ToList();

            var avgWin = wins.Any() ? Math.Round(wins.Average(CalculatePositionProfitLoss), 2) : 0;

            var avgLoss = losses.Any() ? Math.Round(losses.Average(CalculatePositionProfitLoss), 2) : 0;

            // Profit factor
            var totalProfit = Math.Abs(wins.Sum(CalculatePositionProfitLoss));

            var totalLoss = Math.Abs(losses.Sum(CalculatePositionProfitLoss));

            var profitFactor = totalLoss > 0 ? Math.Round(totalProfit / totalLoss, 2) : totalProfit > 0 ? 99.99 : 0;

            return (
                BestDay: bestDay != null ? $"{bestDay.Date:yyyy-MM-dd} ({bestDay.TotalROI:F2}%)" : "N/A",
                WorstDay: worstDay != null ? $"{worstDay.Date:yyyy-MM-dd} ({worstDay.TotalROI:F2}%)" : "N/A",
                avgWin,
                avgLoss,
                profitFactor
            );
        }

        private object CalculateQuickStats(List<Position> positionsInRange, int openPositionsCount)
        {
            if (!positionsInRange.Any())
            {
                return new
                {
                    totalTrades = 0,
                    winRate = 0,
                    avgROI = 0,
                    profitFactor = 0,
                    bestTrade = 0,
                    worstTrade = 0
                };
            }

            var wins = positionsInRange.Where(p => p.ROI > 0).ToList();
            var losses = positionsInRange.Where(p => p.ROI < 0).ToList();

            var totalProfit = Math.Abs(wins.Sum(CalculatePositionProfitLoss));

            var totalLoss = Math.Abs(losses.Sum(CalculatePositionProfitLoss));

            return new
            {
                totalTrades = positionsInRange.Count,
                winRate = Math.Round(wins.Count * 100.0 / positionsInRange.Count, 1),
                avgROI = Math.Round(positionsInRange.Average(p => p.ROI), 2),
                profitFactor = totalLoss > 0 ? Math.Round(totalProfit / totalLoss, 2) : totalProfit > 0 ? 99.99 : 0,
                bestTrade = Math.Round(positionsInRange.Max(p => p.ROI), 2),
                worstTrade = Math.Round(positionsInRange.Min(p => p.ROI), 2),
                openPositions = openPositionsCount
            };
        }

        private async Task<Dictionary<string, decimal>> GetLatestPricesBySymbolAsync(
            AutoSignalsDbContext context,
            IEnumerable<string> symbols)
        {
            var symbolList = symbols
                .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (!symbolList.Any())
                return new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

            var prices = await context.GeneralAssetPrices
                .Where(p => symbolList.Contains(p.Symbol))
                .AsNoTracking()
                .ToListAsync();

            return prices
                .GroupBy(p => p.Symbol, StringComparer.OrdinalIgnoreCase)
                .Select(g => g
                    .OrderByDescending(p => p.Time)
                    .ThenByDescending(p => p.Id)
                    .First())
                .ToDictionary(p => p.Symbol, p => p.Price, StringComparer.OrdinalIgnoreCase);
        }

        private static bool TryGetPositionSize(Position position, out decimal size)
        {
            size = (decimal)position.Size;
            return size > 0;
        }

        private double CalculatePositionProfitLoss(Position position)
        {
            if (!TryGetPositionSize(position, out var size))
                return 0;

            return position.Entry * (double)size * (position.ROI / 100) / Math.Max(position.Leverage, 1);
        }

        private double? TryCalculateNotionalSize(Position position)
        {
            if (!TryGetPositionSize(position, out var size))
                return null;

            return position.Entry * (double)size;
        }

        #endregion

        #region Helper Classes

        private class DashboardStatistics
        {
            public int OpenPositionsCount { get; set; }
            public int ClosedPositionsCount { get; set; }
            public int TotalPositionCount { get; set; }
            public int OpenOrdersCount { get; set; }
            public int ClosedOrdersCount { get; set; }
            public int TotalOrderCount { get; set; }
            public int PendingOrderCount { get; set; }
            public int CancelledOrderCount { get; set; }

            public double TotalROI { get; set; }
            public double AverageROI { get; set; }
            public double HighestROI { get; set; }
            public double LowestROI { get; set; }
            public double OpenPositionsROI { get; set; }
            public double ClosedPositionsROI { get; set; }
            public double TotalPositionsROI { get; set; }

            public double WinRate { get; set; }
            public double LossRate { get; set; }
            public double LongWinRate { get; set; }
            public double ShortWinRate { get; set; }

            public double TotalProfit { get; set; }
            public double TotalLoss { get; set; }
            public double NetPNL { get; set; }

            public string MostTradedSymbol { get; set; }
            public string BestPerformingSymbol { get; set; }
            public string WorstPerformingSymbol { get; set; }

            public double AverageTradeDuration { get; set; }
            public double HighestLeverage { get; set; }
            public double AverageLeverage { get; set; }
            public double LowestLeverage { get; set; }

            public double AverageTradeSize { get; set; }
            public double LargestTradeSize { get; set; }
            public double SmallestTradeSize { get; set; }
            public double TotalTradeVolume { get; set; }

            public List<RoiOverTime> RoiOverTime { get; set; }
            public List<RoiBySymbol> ROIBySymbol { get; set; }
        }

        #endregion
    }
}
