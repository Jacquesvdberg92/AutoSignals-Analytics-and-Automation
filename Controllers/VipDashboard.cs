using Microsoft.AspNetCore.Mvc;
using AutoSignals.Models;
using System.Collections.Generic;
using System.Linq;
using AutoSignals.Data;
using Microsoft.AspNetCore.Identity;
using AutoSignals.Services;
using Microsoft.EntityFrameworkCore;
using Azure.Identity;
using AutoSignals.ViewModels;

namespace AutoSignals.Controllers
{
    public class VipDashboard : Controller
    {
        private readonly AutoSignalsDbContext _context;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly ErrorLogService _errorLogService;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<VipDashboard> _logger;
        private readonly UserOrderWatchDogService _orderWatchDogService;
        private readonly AesEncryptionService _encryptionService;

        public VipDashboard(AutoSignalsDbContext context, UserManager<IdentityUser> userManager, ErrorLogService errorLogService, IServiceScopeFactory scopeFactory, ILogger<VipDashboard> logger, UserOrderWatchDogService orderWatchDogService, AesEncryptionService encryptionService)
        {
            _context = context;
            _userManager = userManager;
            _errorLogService = errorLogService;
            _scopeFactory = scopeFactory;
            _logger = logger;
            _orderWatchDogService = orderWatchDogService;
            _encryptionService = encryptionService;
        }

        public IActionResult Index(string? userId, int? timeframe)
        {
            using (var scope = _scopeFactory.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<AutoSignalsDbContext>();
                // Default to the current user's ID if no userId is provided
                userId ??= _userManager.GetUserId(User);

                // Check if the current user is authorized to access the requested user's dashboard
                if (userId != _userManager.GetUserId(User) && !User.IsInRole("Admin"))
                {
                    return Forbid();
                }

                // Determine the date range based on the timeframe (default to 30 days)
                var days = timeframe ?? 30;
                if (days > 90) days = 90; // Restrict to a maximum of 90 days
                var startDate = DateTime.UtcNow.AddDays(-days);
                var endDate = DateTime.UtcNow;

                // Retrieve user data
                var user = _userManager.FindByIdAsync(userId).Result;
                var userData = context.UsersData.FirstOrDefault(u => u.Id == userId);
                var userName = userData?.NickName ?? user?.UserName;

                // Retrieve positions and orders within the date range
                var userPositions = context.Positions
                    .Where(p => p.UserId == userId)
                    .ToList();
                var positionsInRange = userPositions
                    .Where(p => p.Time >= startDate && p.Time <= endDate)
                    .ToList();

                var userOrders = context.Orders
                    .Where(o => o.UserId == userId)
                    .ToList();
                var ordersInRange = userOrders
                    .Where(o => o.Time >= startDate && o.Time <= endDate)
                    .ToList();



                // Calculate statistics
                var openPositionsCount = userPositions.Count(p => p.Status == "OPEN");
                var closedPositionsCount = positionsInRange.Count(p => p.Status == "CLOSED");
                var totalPositionCount = positionsInRange.Count;
                var openPositionsROI = Math.Round(positionsInRange.Where(p => p.Status == "OPEN").Sum(p => p.ROI), 2);
                var closedPositionsROI = Math.Round(positionsInRange.Where(p => p.Status == "CLOSED").Sum(p => p.ROI), 2);
                var totalPositionsROI = Math.Round(positionsInRange.Sum(p => p.ROI), 2);

                var openOrdersCount = userOrders.Count(o => o.Status == "OPEN");
                var closedOrdersCount = ordersInRange.Count(o => o.Status == "CLOSED");
                var totalOrderCount = ordersInRange.Count;
                var pendingOrderCount = ordersInRange.Count(o => o.Status == "PENDING");
                var cancelledOrderCount = ordersInRange.Count(o => o.Status == "CANCELLED");

                var totalRoi = Math.Round(positionsInRange.DefaultIfEmpty().Sum(p => p?.ROI ?? 0), 2);
                var avgRoi = Math.Round(positionsInRange.Any() ? positionsInRange.Average(p => p.ROI) : 0, 2);
                var roiOverTime = positionsInRange
                    .Where(p => p.UserId == userId && p.Time >= startDate && p.Time <= endDate)
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
                    .ToList() ?? new List<RoiOverTime>();


                var winRate = positionsInRange.Any()
                    ? positionsInRange.Count(p => p.ROI > 0) * 100.0 / positionsInRange.Count
                    : 0;
                var lossRate = 100 - winRate;
                // Calculate Long and Short Win Rates
                var longPositions = positionsInRange.Where(p => p.Side == "buy").ToList();
                var shortPositions = positionsInRange.Where(p => p.Side == "sell").ToList();

                var longWinRate = longPositions.Any()
                    ? longPositions.Count(p => p.ROI > 0) * 100.0 / longPositions.Count
                    : 0;
                var shortWinRate = shortPositions.Any()
                    ? shortPositions.Count(p => p.ROI > 0) * 100.0 / shortPositions.Count
                    : 0;

                var roiBySymbol = userPositions
                    .GroupBy(p => p.Symbol)
                    .Select(g => new RoiBySymbol
                    {
                        Symbol = g.Key,
                        AvgROI = Math.Round(g.Average(p => p.ROI), 2),
                        Count = g.Count()
                    })
                    .ToList() ?? new List<RoiBySymbol>();


                var totalProfit = positionsInRange
                    .Where(p => p.ROI > 0 && p.Leverage > 0)
                    .Sum(p => p.Entry * double.Parse(p.Size) * (p.ROI / 100) / p.Leverage);

                var totalLoss = positionsInRange
                    .Where(p => p.ROI < 0 && p.Leverage > 0)
                    .Sum(p => p.Entry * double.Parse(p.Size) * (p.ROI / 100) / p.Leverage);
                var netPnL = totalProfit + totalLoss;

                var averageTradeDuration = userPositions
                    .Where(p => p.CloseTime.HasValue)
                    .Select(p => (p.CloseTime.Value - p.Time).TotalHours)
                    .DefaultIfEmpty(0) // Default to 0 if no elements exist
                    .Average();

                var mostTradedSymbol = positionsInRange
                    .GroupBy(p => p.Symbol)
                    .OrderByDescending(g => g.Count())
                    .Select(g => g.Key)
                    .DefaultIfEmpty("N/A") // Default to "N/A" if no symbols exist
                    .FirstOrDefault();

                var bestPerformingSymbol = positionsInRange
                    .GroupBy(p => p.Symbol)
                    .OrderByDescending(g => g.Average(p => p.ROI))
                    .Select(g => g.Key)
                    .DefaultIfEmpty("N/A") // Default to "N/A" if no symbols exist
                    .FirstOrDefault();

                var worstPerformingSymbol = positionsInRange
                    .GroupBy(p => p.Symbol)
                    .OrderBy(g => g.Average(p => p.ROI))
                    .Select(g => g.Key)
                    .DefaultIfEmpty("N/A") // Default to "N/A" if no symbols exist
                    .FirstOrDefault();

                var notionalSizes = positionsInRange
                    .Select(p => (p.CloseTime.HasValue ? p.Entry : p.Entry) * double.Parse(p.Size))
                    .ToList();

                var averageTradeSize = notionalSizes.Any() ? Math.Round(notionalSizes.Average(), 2) : 0;
                var largestTradeSize = notionalSizes.Any() ? Math.Round(notionalSizes.Max(), 2) : 0;
                var smallestTradeSize = notionalSizes.Any() ? Math.Round(notionalSizes.Min(), 2) : 0;
                var totalTradeVolume = notionalSizes.Any() ? Math.Round(notionalSizes.Sum(), 2) : 0;


                // Populate the ViewModel
                var viewModel = new VipDashboardViewModel
                {
                    UserId = userId,
                    UserName = userName,

                    UserPositions = userPositions,
                    AllOrders = userOrders,

                    OpenPositionsCount = openPositionsCount,
                    ClosedPositionsCount = closedPositionsCount,
                    TotalPositionCount = totalPositionCount,
                    OpenPositionsROI = openPositionsROI,
                    TotalPositionsROI = totalPositionsROI,
                    ClosedPositionsROI = closedPositionsROI,

                    OpenOrdersCount = openOrdersCount,
                    ClosedOrdersCount = closedOrdersCount,
                    TotalOrderCount = totalOrderCount,
                    PendingOrderCount = pendingOrderCount,
                    CancelledOrderCount = cancelledOrderCount,

                    TotalROI = totalRoi,
                    AverageROI = avgRoi,
                    HighestROI = positionsInRange.Any() ? Math.Round(positionsInRange.Max(p => p.ROI), 2) : 0,
                    LowestROI = positionsInRange.Any() ? Math.Round(positionsInRange.Min(p => p.ROI), 2) : 0,
                    RoiOverTime = roiOverTime,

                    WinRate = Math.Round(winRate, 2),
                    LossRate = Math.Round(lossRate, 2),
                    LongWinRate = Math.Round(longWinRate, 2),
                    ShortWinRate = Math.Round(shortWinRate, 2),

                    ROIBySymbol = roiBySymbol,

                    TotalProfit = Math.Round(totalProfit, 2),
                    TotalLoss = Math.Round(totalLoss, 2),
                    NetPNL = Math.Round(netPnL, 2),

                    MostTradedSymbol = mostTradedSymbol,
                    BestPerformingSymbol = bestPerformingSymbol,
                    WorstPerformingSymbol = worstPerformingSymbol,

                    AverageTradeDuration = averageTradeDuration > 0
                        ? TimeSpan.FromHours(averageTradeDuration).ToString(@"d\:hh\:mm")
                        : "N/A",

                    HighestLeverage = positionsInRange.Any() ? positionsInRange.Max(p => p.Leverage) : 0,
                    AverageLeverage = positionsInRange.Any() ? Math.Round(positionsInRange.Average(p => p.Leverage), 2) : 0,
                    LowestLeverage = positionsInRange.Any() ? positionsInRange.Min(p => p.Leverage) : 0,

                    AverageTradeSize = averageTradeSize,
                    LargestTradeSize = largestTradeSize,
                    SmallestTradeSize = smallestTradeSize,
                    TotalTradeVolume = totalTradeVolume,


                    StartDate = startDate,
                    EndDate = endDate
                };

                return View(viewModel);
            }
            
        }

        private bool IsJsonSuccess(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return false;
            try
            {
                var obj = System.Text.Json.JsonDocument.Parse(json);
                // If it has "info" and "orderId", and does NOT have "message" or "error", treat as success
                var root = obj.RootElement;
                if (root.TryGetProperty("info", out var info) && info.TryGetProperty("orderId", out _))
                {
                    if (!root.TryGetProperty("message", out _) && !root.TryGetProperty("error", out _))
                        return true;
                }
            }
            catch
            {
                // Not valid JSON, treat as not success
            }
            return false;
        }

        [HttpPost]
        public async Task<IActionResult> ClosePosition(int positionId)
        {
            using (var scope = _scopeFactory.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<AutoSignalsDbContext>();

                var position = context.Positions.FirstOrDefault(p => p.Id == positionId);
                var price = context.GeneralAssetPrices.FirstOrDefault(p => p.Symbol == position.Symbol);

                if (position != null && position.Status == "OPEN")
                {
                    // 1. Try to find stoploss order by PositionId
                    var stoplossOrder = context.Orders
                        .FirstOrDefault(o => o.Description != null &&
                                             o.Description.Contains("Stoploss") &&
                                             o.PositionId == position.Id.ToString());

                    // 2. If not found, fall back to any stoploss order with matching symbol and user
                    if (stoplossOrder == null)
                    {
                        stoplossOrder = context.Orders
                            .FirstOrDefault(o => o.Description != null &&
                                                 o.Description.Contains("Stoploss") &&
                                                 o.Symbol == position.Symbol &&
                                                 o.UserId == position.UserId);
                    }

                    if (stoplossOrder == null)
                    {
                        await _errorLogService.LogErrorAsync(
                            $"No stoploss order found for positionId: {positionId}, symbol: {position.Symbol}, userId: {position.UserId}",
                            null,
                            "VipDashboard.ClosePosition",
                            $"PositionId: {positionId}, Symbol: {position.Symbol}, UserId: {position.UserId}"
                        );
                        ModelState.AddModelError("", "No stoploss order found for this position or symbol.");
                        return RedirectToAction("Index");
                    }

                    // 3. Get user data
                    var userData = context.UsersData.FirstOrDefault(u => u.Id == position.UserId);
                    if (userData == null)
                    {
                        await _errorLogService.LogErrorAsync(
                            $"User data not found for userId: {position.UserId} (while closing positionId: {positionId})",
                            null,
                            "VipDashboard.ClosePosition",
                            $"PositionId: {positionId}, UserId: {position.UserId}"
                        );
                        ModelState.AddModelError("", "User data not found.");
                        return RedirectToAction("Index");
                    }

                    // 4. Call the stoploss logic in the service
                    await _orderWatchDogService.HandleExchangeStoplossOrderAsync(stoplossOrder, userData);

                    // 5. Use the robust service method for closing
                    await _orderWatchDogService.CloseOrdersAndPositionAsync(
                        position.Id,
                        price?.Price ?? 0
                    );

                    // Redirect back to the Index action
                    return RedirectToAction("Index");
                }
                // If the position is not found or already closed, redirect back to the Index action
                return RedirectToAction("Index");
            }
        }


        [HttpPost]
        public async Task<IActionResult> CloseOrder(int orderId)
        {
            using (var scope = _scopeFactory.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<AutoSignalsDbContext>();

                var order = context.Orders.FirstOrDefault(o => o.Id == orderId);
                if (order != null && order.Status == "OPEN")
                {
                    // Update the order status to CLOSED
                    order.Status = "CLOSED";
                    order.CloseTime = DateTime.UtcNow;
                    context.Orders.Update(order);

                    // Save changes to the database
                    await context.SaveChangesAsync();

                    // Redirect back to the Index action
                    return RedirectToAction("Index");
                }
                // If the order is not found or already closed, redirect back to the Index action
                return RedirectToAction("Index");
            }
        }



        

    }

}
