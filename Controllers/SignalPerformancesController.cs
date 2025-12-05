using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using AutoSignals.Data;
using AutoSignals.Models;
using Microsoft.AspNetCore.Authorization;

namespace AutoSignals.Controllers
{
    [Authorize(Roles = "Admin")]
    public class SignalPerformancesController : Controller
    {
        private readonly AutoSignalsDbContext _context;

        public SignalPerformancesController(AutoSignalsDbContext context)
        {
            _context = context;
        }

        // GET: SignalPerformances
        public async Task<IActionResult> Index()
        {
            await TrackPageViewAsync("Signal Performances");
            return View(await _context.SignalPerformances
                .OrderByDescending(sp => sp.StartTime)
                .Take(500)
                .ToListAsync());
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
        public IActionResult Create()
        {
            return View();
        }

        // POST: SignalPerformances/Create
        // To protect from overposting attacks, enable the specific properties you want to bind to.
        // For more details, see http://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
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
        [Authorize(Roles = "admin")]
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

        private async Task TrackPageViewAsync(string pageName)
        {
            var today = DateTime.UtcNow.Date;
            var analytics = await _context.Set<AutoSignals.Models.Analytics>()
                .FirstOrDefaultAsync(a => a.PageName == pageName && a.Date == today);

            if (analytics == null)
            {
                analytics = new AutoSignals.Models.Analytics
                {
                    PageName = pageName,
                    Date = today,
                    Views = 1
                };
                _context.Add(analytics);
            }
            else
            {
                analytics.Views += 1;
                _context.Update(analytics);
            }

            await _context.SaveChangesAsync();
        }
    }
}
