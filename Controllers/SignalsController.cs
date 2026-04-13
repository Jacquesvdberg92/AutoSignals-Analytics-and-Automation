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
using Microsoft.AspNetCore.Identity;

namespace AutoSignals.Controllers
{
    [Authorize]
    public class SignalsController : Controller
    {
        private readonly AutoSignalsDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly IAnalyticsService _analyticsService;
        private readonly ISubscriptionService _subscriptionService;
        private readonly UserManager<IdentityUser> _userManager;

        public SignalsController(
            AutoSignalsDbContext context,
            IConfiguration configuration,
            IAnalyticsService analyticsService,
            ISubscriptionService subscriptionService,
            UserManager<IdentityUser> userManager)
        {
            _context = context;
            _configuration = configuration;
            _analyticsService = analyticsService;
            _subscriptionService = subscriptionService;
            _userManager = userManager;
        }

        // GET: Signals
        public async Task<IActionResult> Index()
        {
            var userId = _userManager.GetUserId(User)!;
            var canRealTime = await _subscriptionService.CanAccessFeatureAsync(userId, SubscriptionFeature.RealTimeSignals);

            IQueryable<Signal> query;

            if (!canRealTime)
            {
                // Free: last 7 days, 24-hour delayed (no signals from the last 24 h)
                query = _context.Signals
                    .Where(s => s.Time >= DateTime.UtcNow.AddDays(-7)
                             && s.Time <= DateTime.UtcNow.AddHours(-24));
                ViewBag.IsDelayed = true;
            }
            else
            {
                query = _context.Signals.Where(s => s.Time >= DateTime.UtcNow.AddDays(-90));
            }

            var signals = await query
                .OrderByDescending(s => s.Time)
                .Take(1000)
                .ToListAsync();

            _analyticsService.Increment("Signals");
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

            var detailsUserId = _userManager.GetUserId(User)!;
            var canSeePrediction = await _subscriptionService.CanAccessFeatureAsync(
                detailsUserId, SubscriptionFeature.SignalPredictions);

            var prediction = canSeePrediction
                ? await _context.SignalPredictions.FirstOrDefaultAsync(p => p.SignalId == signal.Id)
                : null;

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
                Prediction = prediction,
                Provider = provider,
                TelegramGroup = telegramGroup,
                CanSeePrediction = canSeePrediction
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
    }
}
