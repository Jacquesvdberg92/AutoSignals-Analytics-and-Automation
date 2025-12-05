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
    [Authorize(Roles = "VIP,Admin")]
    public class SignalsController : Controller
    {
        private readonly AutoSignalsDbContext _context;
        private readonly IConfiguration _configuration;

        public SignalsController(AutoSignalsDbContext context, IConfiguration configuration)
        {
            _context = context;
            _configuration = configuration;
        }

        // GET: Signals
        public async Task<IActionResult> Index()
        {
            var cutoff = DateTime.UtcNow.AddDays(-90);

            var signals = await _context.Signals
                .Where(s => s.Time >= cutoff)
                .OrderByDescending(s => s.Time)
                .Take(1000)
                .ToListAsync();

            await TrackPageViewAsync("Signals");
            return View(signals);
        }

        // GET: Signals/Details/5
        public async Task<IActionResult> Details(int? id, int? providerId)
        {
            // Use the correct section/key from your appsettings.json
            var telegramGroup = _configuration["TelegramGroups:MessageGroupId"];
            if (id == null)
            {
                return NotFound();
            }

            var signal = await _context.Signals
                .FirstOrDefaultAsync(m => m.Id == id);
            if (signal == null)
            {
                return NotFound();
            }

            var performance = await _context.SignalPerformances
                .FirstOrDefaultAsync(p => p.SignalId == signal.Id);

            Provider? provider = null;
            if (!string.IsNullOrWhiteSpace(signal.Provider))
            {
                provider = await _context.Provider
                    .FirstOrDefaultAsync(p => p.Name == signal.Provider);
            }

            var viewModel = new SignalDetailsViewModel
            {
                Signal = signal,
                Performance = performance,
                Provider = provider,
                TelegramGroup = telegramGroup // Pass the group name or ID here
            };

            ViewBag.ProviderId = providerId;
            return View(viewModel);
        }


        // GET: Signals/Create
        [Authorize(Roles = "Admin")]
        public IActionResult Create()
        {
            return View();
        }

        // POST: Signals/Create
        // To protect from overposting attacks, enable the specific properties you want to bind to.
        // For more details, see http://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("Id,Symbol,Side,Leverage,Entry,Stoploss,TakeProfits,Provider,Time")] Signal signal)
        {
            if (ModelState.IsValid)
            {
                _context.Add(signal);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            return View(signal);
        }

        // GET: Signals/Edit/5
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var signal = await _context.Signals.FindAsync(id);
            if (signal == null)
            {
                return NotFound();
            }
            return View(signal);
        }

        // POST: Signals/Edit/5
        // To protect from overposting attacks, enable the specific properties you want to bind to.
        // For more details, see http://go.microsoft.com/fwlink/?LinkId=317598.
        [Authorize(Roles = "Admin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("Id,Symbol,Side,Leverage,Entry,Stoploss,TakeProfits,Provider,Time")] Signal signal)
        {
            if (id != signal.Id)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(signal);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!SignalExists(signal.Id))
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
            return View(signal);
        }

        // GET: Signals/Delete/5
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var signal = await _context.Signals
                .FirstOrDefaultAsync(m => m.Id == id);
            if (signal == null)
            {
                return NotFound();
            }

            return View(signal);
        }

        // POST: Signals/Delete/5
        [Authorize(Roles = "Admin")]
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var signal = await _context.Signals.FindAsync(id);
            if (signal != null)
            {
                _context.Signals.Remove(signal);
            }

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        private bool SignalExists(int id)
        {
            return _context.Signals.Any(e => e.Id == id);
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
