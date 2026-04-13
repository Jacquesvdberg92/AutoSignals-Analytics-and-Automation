using AutoSignals.Data;
using AutoSignals.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using System.Data;
using System.Text.Json;

namespace AutoSignals.Controllers
{
    [Authorize(Policy = "RequiresPro")]
    public class AnalyticsController : Controller
    {
        private readonly AutoSignalsDbContext _context;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly ApplicationDbContext _appContext;

        public AnalyticsController(
            AutoSignalsDbContext context,
            UserManager<IdentityUser> userManager,
            ApplicationDbContext appContext)
        {
            _context = context;
            _userManager = userManager;
            _appContext = appContext;
        }

        // GET: Analytics
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Index()
        {
            // Get all users
            var users = await _userManager.Users.ToListAsync();
            var userIds = users.Select(u => u.Id).ToList();

            // Batch role lookup — 1 join query instead of N individual GetRolesAsync calls
            var userRoles = await (
                from ur in _appContext.Set<IdentityUserRole<string>>()
                join r in _appContext.Set<IdentityRole>() on ur.RoleId equals r.Id
                where userIds.Contains(ur.UserId)
                select new { ur.UserId, r.Name }
            ).ToListAsync();

            var rolesByUser = userRoles
                .GroupBy(x => x.UserId)
                .ToDictionary(g => g.Key, g => g.Select(x => x.Name).ToList());

            // Role counts
            var freeCount = rolesByUser.Values.Count(r => r.Contains("Free User"));
            var subscriberCount = rolesByUser.Values.Count(r => r.Contains("Subscriber"));
            var proCount = rolesByUser.Values.Count(r => r.Contains("Pro"));
            var vipCount = rolesByUser.Values.Count(r => r.Contains("VIP"));
            var testCount = rolesByUser.Values.Count(r => r.Contains("Tester"));
            var adminCount = rolesByUser.Values.Count(r => r.Contains("Admin"));

            // Active subscriptions (Pro/VIP users on active or trial status)
            var activeSubscriptionCount = _context.UsersData
                .Count(u => u.SubscriptionTier != SubscriptionTier.Freemium
                         && (u.SubscriptionStatus == SubscriptionStatus.Active
                          || u.SubscriptionStatus == SubscriptionStatus.Trial));

            // Total user count
            var totalUserCount = users.Count;

            ViewBag.UserCounts = new
            {
                Total = totalUserCount,
                Free = freeCount,
                Subscriber = subscriberCount,
                Pro = proCount,
                VIP = vipCount,
                Test = testCount,
                Admin = adminCount,
                ActiveSubscriptions = activeSubscriptionCount
            };

            var thirtyDaysAgo = DateTime.UtcNow.Date.AddDays(-30);

            // Get recent analytics
            var recentAnalytics = await _context.Analytics
                .Where(a => a.Date >= thirtyDaysAgo)
                .ToListAsync();

            // Prepare daily page views for line chart
            var dailyViews = recentAnalytics
                .GroupBy(a => a.Date.Date)
                .OrderBy(g => g.Key)
                .Select(g => new
                {
                    Date = g.Key,
                    Views = g.Sum(a => a.Views)
                })
                .ToList();

            ViewBag.DailyViews = dailyViews;

            // Get all exchanges for referral clicks
            var exchanges = await _context.Exchanges.ToListAsync();
            ViewBag.Exchanges = exchanges;

            // Get all providers and their page views
            var providers = await _context.Provider.ToListAsync();
            var providerViews = providers
                .Select(p => new
                {
                    Name = p.Name,
                    Views = recentAnalytics.Where(a => a.PageName == p.Name).Sum(a => a.Views)
                })
                .ToList();
            var providersLastSignalDate = providers
                .Select(p => new
                {
                    Name = p.Name,
                    LastSignalDate = p.LastProvidedSignal
                })
                .ToList();

            ViewBag.ProviderViews = providerViews;
            ViewBag.ProvidersLastSignalDate = providersLastSignalDate;

            var pageBreakdown = recentAnalytics
                .GroupBy(a => a.PageName)
                .Select(g => new { PageName = g.Key, TotalViews = g.Sum(a => a.Views) })
                .OrderByDescending(x => x.TotalViews)
                .ToList();
            ViewBag.PageBreakdown = pageBreakdown;

            // Serialize existing Analytics daily page-views for ApexCharts
            ViewBag.DailyViewsDatesJson = JsonSerializer.Serialize(
                dailyViews.Select(d => d.Date.ToString("MMM dd")));
            ViewBag.DailyViewsCountsJson = JsonSerializer.Serialize(
                dailyViews.Select(d => d.Views));

            // DB table sizes via raw ADO.NET
            var tableRows = new List<object>();
            decimal totalDbMb = 0m;
            try
            {
                var conn = _context.Database.GetDbConnection();
                var wasOpen = conn.State == ConnectionState.Open;
                if (!wasOpen) await conn.OpenAsync();
                try
                {
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = @"
                        SELECT t.name, CAST(SUM(a.total_pages) * 8 / 1024.0 AS DECIMAL(18,2))
                        FROM sys.tables t
                        JOIN sys.indexes i ON t.object_id = i.object_id
                        JOIN sys.partitions p ON i.object_id = p.object_id AND i.index_id = p.index_id
                        JOIN sys.allocation_units a ON p.partition_id = a.container_id
                        GROUP BY t.name
                        ORDER BY 2 DESC";
                    using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        var sizeMb = reader.GetDecimal(1);
                        tableRows.Add(new { TableName = reader.GetString(0), SizeMb = sizeMb });
                        totalDbMb += sizeMb;
                    }
                }
                finally
                {
                    if (!wasOpen) await conn.CloseAsync();
                }
            }
            catch { /* DB size is non-critical */ }
            ViewBag.DbTableSizes = tableRows;
            ViewBag.TotalDbMb = totalDbMb;

            // Visit tracking stats from UserVisits table
            var sevenDaysAgo = DateTime.UtcNow.AddDays(-7);
            try
            {
                ViewBag.VisitCount7Days = await _context.UserVisits.CountAsync(v => v.Timestamp >= sevenDaysAgo);

                var dailyVisitCounts = await _context.UserVisits
                    .Where(v => v.Timestamp >= thirtyDaysAgo)
                    .GroupBy(v => v.Timestamp.Date)
                    .Select(g => new { Date = g.Key, Count = g.Count() })
                    .OrderBy(x => x.Date)
                    .ToListAsync();

                ViewBag.DailyVisitDatesJson = JsonSerializer.Serialize(
                    dailyVisitCounts.Select(d => d.Date.ToString("MMM dd")));
                ViewBag.DailyVisitCountsJson = JsonSerializer.Serialize(
                    dailyVisitCounts.Select(d => d.Count));

                ViewBag.TopIps = await _context.UserVisits
                    .Where(v => v.Timestamp >= thirtyDaysAgo && v.IpAddress != null)
                    .GroupBy(v => v.IpAddress)
                    .Select(g => new { Ip = g.Key, Count = g.Count() })
                    .OrderByDescending(x => x.Count)
                    .Take(10)
                    .ToListAsync();

                ViewBag.TopPages = await _context.UserVisits
                    .Where(v => v.Timestamp >= thirtyDaysAgo && v.PagePath != null)
                    .GroupBy(v => v.PagePath)
                    .Select(g => new { Page = g.Key, Count = g.Count() })
                    .OrderByDescending(x => x.Count)
                    .Take(10)
                    .ToListAsync();

                var totalBytes = await _context.UserVisits
                    .Where(v => v.Timestamp >= thirtyDaysAgo)
                    .SumAsync(v => (long?)v.BytesSent) ?? 0L;
                ViewBag.BandwidthMb30Days = Math.Round((decimal)totalBytes / 1_048_576m, 2);

                ViewBag.RecentVisits = await _context.UserVisits
                    .OrderByDescending(v => v.Timestamp)
                    .Take(50)
                    .ToListAsync();
            }
            catch
            {
                ViewBag.VisitCount7Days = 0;
                ViewBag.DailyVisitDatesJson = "[]";
                ViewBag.DailyVisitCountsJson = "[]";
                ViewBag.TopIps = new List<object>();
                ViewBag.TopPages = new List<object>();
                ViewBag.BandwidthMb30Days = 0m;
                ViewBag.RecentVisits = new List<UserVisit>();
            }

            ViewBag.ErrorCount7Days = await _context.ErrorLogs.CountAsync(e => e.Timestamp >= sevenDaysAgo);

            return View();
        }

        // GET: Analytics/Details/5
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var analytics = await _context.Analytics
                .FirstOrDefaultAsync(m => m.Id == id);
            if (analytics == null)
            {
                return NotFound();
            }

            return View(analytics);
        }

        // GET: Analytics/Create
        [Authorize(Roles = "Admin")]
        public IActionResult Create()
        {
            return View();
        }

        // POST: Analytics/Create
        // To protect from overposting attacks, enable the specific properties you want to bind to.
        // For more details, see http://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Create([Bind("Id,PageName,Date,Views")] Analytics analytics)
        {
            if (ModelState.IsValid)
            {
                _context.Add(analytics);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            return View(analytics);
        }

        // GET: Analytics/Edit/5
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var analytics = await _context.Analytics.FindAsync(id);
            if (analytics == null)
            {
                return NotFound();
            }
            return View(analytics);
        }

        // POST: Analytics/Edit/5
        // To protect from overposting attacks, enable the specific properties you want to bind to.
        // For more details, see http://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Edit(int id, [Bind("Id,PageName,Date,Views")] Analytics analytics)
        {
            if (id != analytics.Id)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(analytics);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!AnalyticsExists(analytics.Id))
                    {
                        return NotFound();
                    }
                    else
                    {
                        throw;
                    }
                }
                return RedirectToAction(nameof(Index));
            }
            return View(analytics);
        }

        // GET: Analytics/Delete/5
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var analytics = await _context.Analytics
                .FirstOrDefaultAsync(m => m.Id == id);
            if (analytics == null)
            {
                return NotFound();
            }

            return View(analytics);
        }

        // POST: Analytics/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var analytics = await _context.Analytics.FindAsync(id);
            if (analytics != null)
            {
                _context.Analytics.Remove(analytics);
            }

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        private bool AnalyticsExists(int id)
        {
            return _context.Analytics.Any(e => e.Id == id);
        }
    }
}
