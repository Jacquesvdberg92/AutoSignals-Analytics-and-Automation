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
    
    public class ExchangesController : Controller
    {
        private readonly AutoSignalsDbContext _context;

        public ExchangesController(AutoSignalsDbContext context)
        {
            _context = context;
        }

        // GET: Exchanges
        public async Task<IActionResult> Index()
        {
            await TrackPageViewAsync("Exchanges");
            return View(await _context.Exchanges.ToListAsync());
        }

        // GET: Exchanges/Details/5
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var exchange = await _context.Exchanges
                .FirstOrDefaultAsync(m => m.Id == id);
            if (exchange == null)
            {
                return NotFound();
            }

            return View(exchange);
        }

        // GET: Exchanges/Create
        [Authorize(Roles = "Admin")]
        public IActionResult Create()
        {
            return View();
        }

        // POST: Exchanges/Create
        // To protect from overposting attacks, enable the specific properties you want to bind to.
        // For more details, see http://go.microsoft.com/fwlink/?LinkId=317598.
        [Authorize(Roles = "Admin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("Id,Name,Description,Referal,Url,ReferalClicked,ReferralBonus,Type,LogoUrl,IsEnabled")] Exchange exchange)
        {
            if (ModelState.IsValid)
            {
                _context.Add(exchange);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            return View(exchange);
        }

        // GET: Exchanges/Edit/5
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var exchange = await _context.Exchanges.FindAsync(id);
            if (exchange == null)
            {
                return NotFound();
            }
            return View(exchange);
        }

        // POST: Exchanges/Edit/5
        // To protect from overposting attacks, enable the specific properties you want to bind to.
        // For more details, see http://go.microsoft.com/fwlink/?LinkId=317598.
        [Authorize(Roles = "Admin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("Id,Name,Description,Referal,Url,ReferalClicked,ReferralBonus,Type,LogoUrl,IsEnabled")] Exchange exchange)
        {
            if (id != exchange.Id)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(exchange);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!ExchangeExists(exchange.Id))
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
            return View(exchange);
        }

        // GET: Exchanges/Delete/5
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var exchange = await _context.Exchanges
                .FirstOrDefaultAsync(m => m.Id == id);
            if (exchange == null)
            {
                return NotFound();
            }

            return View(exchange);
        }

        // POST: Exchanges/Delete/5
        [Authorize(Roles = "Admin")]
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var exchange = await _context.Exchanges.FindAsync(id);
            if (exchange != null)
            {
                _context.Exchanges.Remove(exchange);
            }

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> Referral(int id)
        {
            var exchange = await _context.Exchanges.FindAsync(id);
            if (exchange == null || string.IsNullOrEmpty(exchange.Referal))
                return NotFound();

            exchange.ReferalClicked++;
            await _context.SaveChangesAsync();

            return Redirect(exchange.Referal);
        }

        private bool ExchangeExists(int id)
        {
            return _context.Exchanges.Any(e => e.Id == id);
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
