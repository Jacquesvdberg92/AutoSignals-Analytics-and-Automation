using AutoSignals.Data;
using AutoSignals.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using AutoSignals.Models;

namespace AutoSignals.Controllers
{
    public class AnalyticsController : Controller
    {
        private readonly AutoSignalsDbContext _context;
        private readonly UserManager<IdentityUser> _userManager;

        public AnalyticsController(
            AutoSignalsDbContext context,
            UserManager<IdentityUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        // GET: Analytics
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Index()
        {
            // Get all users
            var users = _userManager.Users.ToList();

            // Role counts
            var freeCount = 0;
            var subscriberCount = 0;
            var vipCount = 0;
            var testCount = 0;
            var adminCount = 0;

            foreach (var user in users)
            {
                var roles = await _userManager.GetRolesAsync(user);
                if (roles.Contains("Free User")) freeCount++;
                if (roles.Contains("Subscriber")) subscriberCount++;
                if (roles.Contains("VIP")) vipCount++;
                if (roles.Contains("Tester")) testCount++;
                if (roles.Contains("Admin")) adminCount++;
            }

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
