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
    public class ProvidersController : Controller
    {
        private readonly AutoSignalsDbContext _context;
        private readonly ILogger<ProvidersController> _logger;

        public ProvidersController(AutoSignalsDbContext context, ILogger<ProvidersController> logger)
        {
            _context = context;
            _logger = logger;
        }

        // GET: Providers
        public async Task<IActionResult> Index()
        {
            var providers = await _context.Provider
            .OrderBy(p => p.Name)
            .ToListAsync();

            await TrackPageViewAsync("Signal Providers");
            return View(providers);
        }

        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var provider = await _context.Provider
                .FirstOrDefaultAsync(m => m.Id == id);
            if (provider == null)
            {
                return NotFound();
            }

            var since = DateTime.UtcNow.AddDays(-90);
            var signals = await _context.Signals
                .Where(s => s.Provider == provider.Name && s.Time >= since)
                .ToListAsync();

            int tpCount = provider.TakeProfitDistribution.Split(",").Count();

            // This is needed for the bar to show the TP distro correct, its a String list in the DB but we need a Int list here
            var tpDistro = provider.TakeProfitDistribution?
                .Split(",", StringSplitOptions.RemoveEmptyEntries) // Remove empty entries
                .Select(x => int.TryParse(x, out var result) ? result : 0) // Safely parse or default to 0
                .ToList();

            if (tpDistro == null || tpDistro.Count == 0)
                tpDistro = new List<int> { 0 };

            ViewBag.Id = provider.Id;
            ViewBag.Name = provider.Name;
            ViewBag.RRR = provider.RRR;
            ViewBag.AverageProfitPerTrade = provider.AverageProfitPerTrade;
            ViewBag.StoplossPersentage = provider.StoplossPersentage;
            ViewBag.SignalCount = provider.SignalCount;
            ViewBag.AverageLeverage = provider.AverageLeverage;
            ViewBag.TakeProfitTargets = provider.TakeProfitTargets;
            ViewBag.SignalsNullified = provider.SignalsNullified;
            ViewBag.TradeStyle = provider.TradeStyle;
            ViewBag.TradesPerDay = provider.TradesPerDay;
            ViewBag.TradeTimeframes = provider.TradeTimeframes;
            ViewBag.AverageWinRate = provider.AverageWinRate;
            ViewBag.LongWinRate = provider.LongWinRate;
            ViewBag.ShortWinRate = provider.ShortWinRate;
            ViewBag.LongCount = provider.LongCount;
            ViewBag.ShortCount = provider.ShortCount;
            ViewBag.LongRatio = provider.LongRatio;
            ViewBag.ShortRatio = provider.ShortRatio;
            ViewBag.TpAchieved = provider.TpAchieved;
            ViewBag.Risk = provider.Risk;
            ViewBag.TpCount = tpCount;
            ViewBag.TakeProfitDistribution = tpDistro;
            ViewBag.Telegram = provider.Telegram;
            ViewBag.Picture = provider.Picture;
            ViewBag.Signals = signals;

            await TrackPageViewAsync(provider.Name);
            return View();
        }

        // GET: Providers/Create
        public IActionResult Create()
        {
            return View();
        }

        // POST: Providers/Create
        // To protect from overposting attacks, enable the specific properties you want to bind to.
        // For more details, see http://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("Id,Name,RRR,AverageProfitPerTrade,StoplossPersentage,SignalCount,AverageLeverage,TakeProfitTargets,SignalsNullified,TradeStyle,TradesPerDay,TradeTimeframes,AverageWinRate,LongWinRate,ShortWinRate,LongCount,ShortCount,TpAchieved,Telegram")] Provider provider)
        {
            if (ModelState.IsValid)
            {
                _context.Add(provider);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            return View(provider);
        }

        // GET: Providers/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var provider = await _context.Provider.FindAsync(id);
            if (provider == null)
            {
                return NotFound();
            }
            return View(provider);
        }

        // POST: Providers/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("Id,Name,RRR,AverageProfitPerTrade,StoplossPersentage,SignalCount,AverageLeverage,TakeProfitTargets,SignalsNullified,TradeStyle,TradesPerDay,TradeTimeframes,AverageWinRate,LongWinRate,ShortWinRate,LongCount,ShortCount,TpAchieved,Telegram,Risk")] Provider provider, IFormFile picture)
        {
            if (id != provider.Id)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                try
                {
                    if (picture != null && picture.Length > 0)
                    {
                        using (var memoryStream = new MemoryStream())
                        {
                            await picture.CopyToAsync(memoryStream);
                            provider.Picture = memoryStream.ToArray();
                        }
                    }

                    _context.Update(provider);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!ProviderExists(provider.Id))
                    {
                        return NotFound();
                    }
                    else
                    {
                        throw;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "An error occurred while uploading the image.");
                    ModelState.AddModelError(string.Empty, "An error occurred while uploading the image.");
                    return View(provider);
                }
                return RedirectToAction(nameof(Index));
            }
            return View(provider);
        }

        // GET: Providers/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var provider = await _context.Provider
                .FirstOrDefaultAsync(m => m.Id == id);
            if (provider == null)
            {
                return NotFound();
            }

            return View(provider);
        }

        // POST: Providers/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var provider = await _context.Provider.FindAsync(id);
            if (provider != null)
            {
                _context.Provider.Remove(provider);
            }

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        private bool ProviderExists(int id)
        {
            return _context.Provider.Any(e => e.Id == id);
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
