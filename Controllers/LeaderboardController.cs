using AutoSignals.Data;
using AutoSignals.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace AutoSignals.Controllers
{
    [Authorize]
    public class LeaderboardController : Controller
    {
        private readonly AutoSignalsDbContext _context;
        private readonly ILogger<LeaderboardController> _logger;

        public LeaderboardController(AutoSignalsDbContext context, ILogger<LeaderboardController> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<IActionResult> Index(string sortBy = "winrate", string order = "desc")
        {
            var providers = await _context.Provider
                .Where(p => p.IsActive == true)
                .ToListAsync();

            var ranked = providers.Select(p => new ProviderRankViewModel
            {
                Provider = p,
                WinRate = ParseDouble(p.AverageWinRate),
                RRR = ParseDouble(p.RRR),
                SignalCount = p.SignalCount ?? 0,
                AverageProfitPerTrade = ParseDouble(p.AverageProfitPerTrade),
                AverageLeverage = ParseDouble(p.AverageLeverage),
                StoplossPercentage = ParseDouble(p.StoplossPersentage),
                LongRatio = p.LongRatio ?? 0,
                ShortRatio = p.ShortRatio ?? 0
            }).ToList();

            bool descending = order != "asc";

            ranked = (sortBy?.ToLower()) switch
            {
                "rrr" => descending
                    ? ranked.OrderByDescending(r => r.RRR).ToList()
                    : ranked.OrderBy(r => r.RRR).ToList(),
                "signals" => descending
                    ? ranked.OrderByDescending(r => r.SignalCount).ToList()
                    : ranked.OrderBy(r => r.SignalCount).ToList(),
                "profit" => descending
                    ? ranked.OrderByDescending(r => r.AverageProfitPerTrade).ToList()
                    : ranked.OrderBy(r => r.AverageProfitPerTrade).ToList(),
                "leverage" => descending
                    ? ranked.OrderByDescending(r => r.AverageLeverage).ToList()
                    : ranked.OrderBy(r => r.AverageLeverage).ToList(),
                _ => descending
                    ? ranked.OrderByDescending(r => r.WinRate).ToList()
                    : ranked.OrderBy(r => r.WinRate).ToList()
            };

            ViewBag.SortBy = sortBy ?? "winrate";
            ViewBag.Order = order;

            return View(ranked);
        }

        private static double ParseDouble(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return 0;
            var cleaned = value.TrimEnd('%', ' ');
            return double.TryParse(cleaned, NumberStyles.Any, CultureInfo.InvariantCulture, out var result) ? result : 0;
        }
    }

    public class ProviderRankViewModel
    {
        public Provider Provider { get; set; } = null!;
        public double WinRate { get; set; }
        public double RRR { get; set; }
        public int SignalCount { get; set; }
        public double AverageProfitPerTrade { get; set; }
        public double AverageLeverage { get; set; }
        public double StoplossPercentage { get; set; }
        public int LongRatio { get; set; }
        public int ShortRatio { get; set; }
    }
}
