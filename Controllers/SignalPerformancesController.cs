using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using AutoSignals.Data;
using AutoSignals.Models;
using AutoSignals.Services;
using Microsoft.AspNetCore.Authorization;

namespace AutoSignals.Controllers
{
    [Authorize]
    public class SignalPerformancesController : Controller
    {
        private readonly AutoSignalsDbContext _context;
        private readonly IAnalyticsService _analyticsService;

        public SignalPerformancesController(
            AutoSignalsDbContext context,
            IAnalyticsService analyticsService)
        {
            _context = context;
            _analyticsService = analyticsService;
        }

        // GET: SignalPerformances
        public async Task<IActionResult> Index()
        {
            _analyticsService.Increment("Signal Performances");

            IQueryable<SignalPerformance> query = _context.SignalPerformances.OrderByDescending(sp => sp.StartTime);

            if (!User.IsInRole("Admin") && !User.IsInRole("VIP") && !User.IsInRole("Tester"))
            {
                // Free and Pro both see 30-day history.
                // VIP / Tester / Admin see the full range.
                query = query.Where(sp => sp.StartTime >= DateTime.UtcNow.AddDays(-30));
                ViewBag.DateRangeLabel = "30-day history";
            }

            return View(await query.Take(500).ToListAsync());
        }


        // GET: SignalPerformances/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var signalPerformance = await _context.SignalPerformances
                .FirstOrDefaultAsync(m => m.Id == id);
            if (signalPerformance == null)
            {
                return NotFound();
            }

            return View(signalPerformance);
        }

        // GET: SignalPerformances/Create
        [Authorize(Roles = "Admin")]
        public IActionResult Create()
        {
            return View();
        }

        // POST: SignalPerformances/Create
        // To protect from overposting attacks, enable the specific properties you want to bind to.
        // For more details, see http://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Create([Bind("Id,Status,SignalId,StartTime,EndTime,HighPrice,LowPrice,ProfitLoss,TakeProfitCount,TakeProfitsAchieved,AchievedTakeProfits,Notes")] SignalPerformance signalPerformance)
        {
            if (ModelState.IsValid)
            {
                _context.Add(signalPerformance);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            return View(signalPerformance);
        }

        // GET: SignalPerformances/Edit/5
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var signalPerformance = await _context.SignalPerformances.FindAsync(id);
            if (signalPerformance == null)
            {
                return NotFound();
            }
            return View(signalPerformance);
        }

        // POST: SignalPerformances/Edit/5
        // To protect from overposting attacks, enable the specific properties you want to bind to.
        // For more details, see http://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Edit(int id, [Bind("Id,Status,SignalId,StartTime,EndTime,HighPrice,LowPrice,ProfitLoss,TakeProfitCount,TakeProfitsAchieved,AchievedTakeProfits,Notes")] SignalPerformance signalPerformance)
        {
            if (id != signalPerformance.Id)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(signalPerformance);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!SignalPerformanceExists(signalPerformance.Id))
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
            return View(signalPerformance);
        }

        // GET: SignalPerformances/Delete/5
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var signalPerformance = await _context.SignalPerformances
                .FirstOrDefaultAsync(m => m.Id == id);
            if (signalPerformance == null)
            {
                return NotFound();
            }

            return View(signalPerformance);
        }

        // POST: SignalPerformances/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var signalPerformance = await _context.SignalPerformances.FindAsync(id);
            if (signalPerformance != null)
            {
                _context.SignalPerformances.Remove(signalPerformance);
            }

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        private bool SignalPerformanceExists(int id)
        {
            return _context.SignalPerformances.Any(e => e.Id == id);
        }
    }
}
