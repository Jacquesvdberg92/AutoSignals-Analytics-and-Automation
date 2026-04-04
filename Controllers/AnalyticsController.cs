using AutoSignals.Data;
using AutoSignals.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;

namespace AutoSignals.Controllers
{
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
            var vipCount = rolesByUser.Values.Count(r => r.Contains("VIP"));
            var testCount = rolesByUser.Values.Count(r => r.Contains("Tester"));
            var adminCount = rolesByUser.Values.Count(r => r.Contains("Admin"));

            // Active subscriptions
            var activeSubscriptionCount = _context.UsersData
                .Count(u => u.SubscriptionActive == "1");

            // Total user count
            var totalUserCount = users.Count;

            ViewBag.UserCounts = new
            {
                Total = totalUserCount,
                Free = freeCount,
                Subscriber = subscriberCount,
                VIP = vipCount,
                Test = testCount,
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
