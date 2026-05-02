using AutoSignals.Data;
using AutoSignals.Models;
using AutoSignals.ViewModels.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text.Json;

namespace AutoSignals.Controllers
{
    [Authorize(Roles = "Admin")]
    public class SignalPerformanceDashboardController : Controller
    {
        private readonly AutoSignalsDbContext _context;

        public SignalPerformanceDashboardController(AutoSignalsDbContext context)
        {
            _context = context;
        }

        [HttpGet("/Admin/SignalPerformanceDashboard")]
        [ResponseCache(Duration = 30)]
        public async Task<IActionResult> Index()
        {
            var now = DateTime.UtcNow;
            var since24h = now.AddHours(-24);
            var today = now.Date;
            var since30d = now.AddDays(-30);

            // ── Raw data pulls ────────────────────────────────────────────────
            var performances = await _context.SignalPerformances
                .AsNoTracking()
                .ToListAsync();

            var signals = await _context.Signals
                .AsNoTracking()
                .Select(s => new { s.Id, s.Symbol, s.Provider, s.TakeProfits })
                .ToListAsync();

            var signalMap = signals.ToDictionary(s => s.Id);

            var serviceErrors = await _context.ErrorLogs
                .AsNoTracking()
                .Where(e => e.Timestamp >= since24h
                    && e.Source != null
                    && e.Source.Contains("SignalPerformanceService"))
                .CountAsync();

            // ── Tracking Health ───────────────────────────────────────────────
            var pending = performances.Count(p => p.Status == "Pending");
            var open = performances.Count(p => p.Status == "Open");
            var closedToday = performances.Count(p => p.Status == "Closed" && p.EndTime >= today);
            var cancelledToday = performances.Count(p => p.Status == "Canceled" && p.EndTime >= today);

            // ── Win / Loss ────────────────────────────────────────────────────
            var closed = performances.Where(p => p.Status == "Closed").ToList();
            var wins = closed.Where(p => p.Notes == "All Take Profits Achieved").ToList();
            var losses = closed.Where(p => p.Notes == "Stoploss Hit").ToList();
            var partialWins = closed.Where(p =>
                p.TakeProfitsAchieved.HasValue && p.TakeProfitsAchieved > 0
                && p.Notes != "All Take Profits Achieved").ToList();

            double winRate = closed.Count > 0 ? Math.Round(100.0 * wins.Count / closed.Count, 1) : 0;
            double lossRate = closed.Count > 0 ? Math.Round(100.0 * losses.Count / closed.Count, 1) : 0;
            double partialWinRate = closed.Count > 0 ? Math.Round(100.0 * partialWins.Count / closed.Count, 1) : 0;

            double avgTps = closed.Any()
                ? Math.Round(closed.Average(p => p.TakeProfitCount > 0
                    ? (double)(p.TakeProfitsAchieved ?? 0) / p.TakeProfitCount
                    : 0), 2)
                : 0;

            double avgProfit = wins.Any()
                ? Math.Round((double)wins.Where(p => p.ProfitLoss.HasValue).Average(p => p.ProfitLoss!.Value), 2)
                : 0;

            double avgLoss = losses.Any()
                ? Math.Round((double)losses.Where(p => p.ProfitLoss.HasValue).Average(p => p.ProfitLoss!.Value), 2)
                : 0;

            // ── TP Hit Rates ──────────────────────────────────────────────────
            int openOrClosed = performances.Count(p => p.Status == "Open" || p.Status == "Closed");

            double TpHitRate(int tpNumber)
            {
                if (openOrClosed == 0) return 0;
                int hit = performances.Count(p =>
                    (p.Status == "Open" || p.Status == "Closed")
                    && p.TakeProfitsAchieved.HasValue
                    && p.TakeProfitsAchieved >= tpNumber
                    && p.TakeProfitCount >= tpNumber);
                return Math.Round(100.0 * hit / openOrClosed, 1);
            }

            double avgDurationHours = closed
                .Where(p => p.EndTime.HasValue)
                .Select(p => (p.EndTime!.Value - p.StartTime).TotalHours)
                .DefaultIfEmpty(0)
                .Average();

            // ── Provider Breakdown ────────────────────────────────────────────
            var providerStats = performances
                .Where(p => signalMap.ContainsKey(p.SignalId))
                .GroupBy(p => signalMap[p.SignalId].Provider)
                .Select(g =>
                {
                    var gclosed = g.Where(p => p.Status == "Closed").ToList();
                    var gwins = gclosed.Where(p => p.Notes == "All Take Profits Achieved").ToList();
                    var glosses = gclosed.Where(p => p.Notes == "Stoploss Hit").ToList();
                    return new ProviderPerformanceStat
                    {
                        Provider = g.Key ?? "Unknown",
                        Total = g.Count(),
                        Wins = gwins.Count,
                        Losses = glosses.Count,
                        Open = g.Count(p => p.Status == "Open"),
                        Cancelled = g.Count(p => p.Status == "Canceled"),
                        WinRate = gclosed.Count > 0 ? Math.Round(100.0 * gwins.Count / gclosed.Count, 1) : 0,
                        AvgProfit = gwins.Any()
                            ? Math.Round((double)gwins.Where(p => p.ProfitLoss.HasValue).DefaultIfEmpty().Average(p => p?.ProfitLoss ?? 0), 2)
                            : 0,
                        AvgLoss = glosses.Any()
                            ? Math.Round((double)glosses.Where(p => p.ProfitLoss.HasValue).DefaultIfEmpty().Average(p => p?.ProfitLoss ?? 0), 2)
                            : 0,
                        AvgTpsAchieved = gclosed.Any()
                            ? Math.Round(gclosed.Average(p => p.TakeProfitCount > 0
                                ? (double)(p.TakeProfitsAchieved ?? 0) / p.TakeProfitCount : 0), 2)
                            : 0
                    };
                })
                .OrderByDescending(x => x.Total)
                .ToList();

            // ── Symbol Breakdown ──────────────────────────────────────────────
            var symbolStats = performances
                .Where(p => signalMap.ContainsKey(p.SignalId))
                .GroupBy(p => signalMap[p.SignalId].Symbol)
                .Select(g =>
                {
                    var gclosed = g.Where(p => p.Status == "Closed").ToList();
                    var gwins = gclosed.Where(p => p.Notes == "All Take Profits Achieved").ToList();
                    return new SymbolPerformanceStat
                    {
                        Symbol = g.Key ?? "Unknown",
                        Total = g.Count(),
                        Wins = gwins.Count,
                        WinRate = gclosed.Count > 0 ? Math.Round(100.0 * gwins.Count / gclosed.Count, 1) : 0,
                        AvgPl = gclosed.Any()
                            ? Math.Round((double)gclosed.Where(p => p.ProfitLoss.HasValue).DefaultIfEmpty().Average(p => p?.ProfitLoss ?? 0), 2)
                            : 0
                    };
                })
                .OrderByDescending(x => x.Total)
                .Take(20)
                .ToList();

            // ── Chart: Outcome Pie ────────────────────────────────────────────
            var outcomePieLabels = new List<string> { "All TPs Hit", "Stoploss Hit", "Partial Win", "Canceled", "Open" };
            var outcomePieValues = new List<int>
            {
                wins.Count,
                losses.Count,
                partialWins.Count,
                performances.Count(p => p.Status == "Canceled"),
                open
            };

            // ── Chart: Daily opened vs closed (30 days) ───────────────────────
            var daily30 = performances
                .Where(p => p.StartTime >= since30d || (p.EndTime.HasValue && p.EndTime >= since30d))
                .ToList();

            var dailyLabels = new List<string>();
            var dailyOpened = new List<int>();
            var dailyClosed = new List<int>();

            for (int d = 29; d >= 0; d--)
            {
                var dayStart = today.AddDays(-d);
                var dayEnd = dayStart.AddDays(1);
                dailyLabels.Add(dayStart.ToString("MMM dd"));
                dailyOpened.Add(daily30.Count(p => p.StartTime >= dayStart && p.StartTime < dayEnd));
                dailyClosed.Add(daily30.Count(p => p.EndTime.HasValue && p.EndTime >= dayStart && p.EndTime < dayEnd));
            }

            // ── Chart: P/L Histogram ──────────────────────────────────────────
            var plBuckets = new (string Label, double Min, double Max)[]
            {
                ("< -20%", double.MinValue, -20),
                ("-20 to -10%", -20, -10),
                ("-10 to 0%", -10, 0),
                ("0 to +10%", 0, 10),
                ("+10 to +20%", 10, 20),
                ("+20 to +50%", 20, 50),
                ("> +50%", 50, double.MaxValue)
            };

            var plLabels = plBuckets.Select(b => b.Label).ToList();
            var plValues = plBuckets
                .Select(b => closed.Count(p =>
                    p.ProfitLoss.HasValue &&
                    (double)p.ProfitLoss.Value >= b.Min &&
                    (double)p.ProfitLoss.Value < b.Max))
                .ToList();

            // ── Chart: Provider Win Rate (top 15) ─────────────────────────────
            var top15Providers = providerStats
                .Where(p => p.Wins + p.Losses >= 3)
                .OrderByDescending(p => p.WinRate)
                .Take(15)
                .ToList();

            var vm = new SignalPerformanceDashboardViewModel
            {
                TotalPending = pending,
                TotalOpen = open,
                ClosedToday = closedToday,
                CancelledToday = cancelledToday,
                ServiceErrorCount24h = serviceErrors,

                TotalClosed = closed.Count,
                TotalWins = wins.Count,
                TotalLosses = losses.Count,
                TotalPartialWins = partialWins.Count,
                WinRate = winRate,
                LossRate = lossRate,
                PartialWinRate = partialWinRate,
                AvgTpsAchieved = avgTps,
                AvgProfitOnWins = avgProfit,
                AvgLossOnLosses = avgLoss,

                Tp1HitRate = TpHitRate(1),
                Tp2HitRate = TpHitRate(2),
                Tp3HitRate = TpHitRate(3),
                Tp4HitRate = TpHitRate(4),
                AvgDurationToCloseHours = Math.Round(avgDurationHours, 1),

                ProviderStats = providerStats,
                SymbolStats = symbolStats,

                OutcomePieLabels = outcomePieLabels,
                OutcomePieValues = outcomePieValues,

                DailyLabels = dailyLabels,
                DailyOpenedValues = dailyOpened,
                DailyClosedValues = dailyClosed,

                PlHistogramLabels = plLabels,
                PlHistogramValues = plValues,

                ProviderWinRateLabels = top15Providers.Select(p => p.Provider).ToList(),
                ProviderWinRateValues = top15Providers.Select(p => p.WinRate).ToList()
            };

            ViewBag.OutcomePieLabelsJson = JsonSerializer.Serialize(vm.OutcomePieLabels);
            ViewBag.OutcomePieValuesJson = JsonSerializer.Serialize(vm.OutcomePieValues);
            ViewBag.DailyLabelsJson = JsonSerializer.Serialize(vm.DailyLabels);
            ViewBag.DailyOpenedJson = JsonSerializer.Serialize(vm.DailyOpenedValues);
            ViewBag.DailyClosedJson = JsonSerializer.Serialize(vm.DailyClosedValues);
            ViewBag.PlHistogramLabelsJson = JsonSerializer.Serialize(vm.PlHistogramLabels);
            ViewBag.PlHistogramValuesJson = JsonSerializer.Serialize(vm.PlHistogramValues);
            ViewBag.ProviderWinRateLabelsJson = JsonSerializer.Serialize(vm.ProviderWinRateLabels);
            ViewBag.ProviderWinRateValuesJson = JsonSerializer.Serialize(vm.ProviderWinRateValues);

            return View("~/Views/Admin/SignalPerformanceDashboard.cshtml", vm);
        }
    }
}
