using AutoSignals.Data;
using AutoSignals.Models;
using AutoSignals.Services;
using AutoSignals.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace AutoSignals.Controllers
{
    [Authorize]
    public class PortfolioController : Controller
    {
        private readonly AutoSignalsDbContext _context;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly ISubscriptionService _subscriptionService;

        public PortfolioController(
            AutoSignalsDbContext context,
            UserManager<IdentityUser> userManager,
            ISubscriptionService subscriptionService)
        {
            _context = context;
            _userManager = userManager;
            _subscriptionService = subscriptionService;
        }

        private string GetUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier);

        private Task<int> GetPortfolioLimitAsync(string userId)
        {
            // Use role-based check (mirrors CanAccessFeatureAsync) so legacy roles
            // (Tester = VIP-equivalent, Subscriber = Pro-equivalent) are correctly honoured.
            // UserData.SubscriptionTier cannot be used here because Tester users were migrated
            // with Tier=Freemium in UserData (KI-04) but hold full VIP access via their role.
            bool isVip = User.IsInRole("VIP") || User.IsInRole("Tester") || User.IsInRole("Admin");
            bool isPro = isVip || User.IsInRole("Pro") || User.IsInRole("Subscriber");
            int limit = isVip ? 10 : isPro ? 3 : 1;
            return Task.FromResult(limit);
        }

        private static string DisplaySymbol(string? symbol)
        {
            if (string.IsNullOrWhiteSpace(symbol))
                return string.Empty;

            symbol = symbol.Trim();

            // Handles "BTC/USDT" and "BTCUSDT" (if it ever shows up that way)
            if (symbol.EndsWith("/USDT", StringComparison.OrdinalIgnoreCase))
                return symbol[..^5];

            if (symbol.EndsWith("USDT", StringComparison.OrdinalIgnoreCase) && symbol.Length > 4)
                return symbol[..^4];

            // Generic fallback for "BASE/QUOTE" -> "BASE"
            var slash = symbol.IndexOf('/');
            return slash > 0 ? symbol[..slash] : symbol;
        }

        // GET: Dashboard
        public async Task<IActionResult> Dashboard(int? portfolioId)
        {
            var userId = GetUserId();
            var limit = await GetPortfolioLimitAsync(userId);

            // Fetch ALL portfolios — never deleted on downgrade; extra ones are just hidden.
            var allPortfolios = await _context.Portfolios
                .Where(p => p.UserId == userId)
                .Include(p => p.Holdings)
                .OrderByDescending(p => p.IsDefault)
                .ThenBy(p => p.Name)
                .ToListAsync();

            // Visibility: Free=1, Pro=3, VIP=10.
            // Hidden portfolios are preserved and reappear automatically on upgrade.
            var visiblePortfolios = allPortfolios.Take(limit).ToList();
            var hiddenCount = allPortfolios.Count - visiblePortfolios.Count;

            // If the requested portfolio is outside the visible set (e.g. a downgraded user
            // with a bookmark to a non-default portfolio), fall back to the default.
            if (portfolioId.HasValue && !visiblePortfolios.Any(p => p.Id == portfolioId.Value))
                portfolioId = null;

            Portfolio? activePortfolio = portfolioId.HasValue
                ? visiblePortfolios.FirstOrDefault(p => p.Id == portfolioId.Value)
                : null;
            activePortfolio ??= visiblePortfolios.FirstOrDefault(p => p.IsDefault) ?? visiblePortfolios.FirstOrDefault();

            if (activePortfolio?.Holdings != null)
                await CalculatePortfolioValues(activePortfolio);

            ViewBag.ActivePortfolio = activePortfolio;
            ViewBag.PortfolioLimit = limit;
            ViewBag.HiddenPortfolioCount = hiddenCount;
            return View(visiblePortfolios);
        }

        private async Task CalculatePortfolioValues(Portfolio portfolio)
        {
            if (portfolio.Holdings == null || !portfolio.Holdings.Any())
            {
                portfolio.TotalValue = 0;
                ViewBag.TotalCost = 0m;
                ViewBag.TotalPnL = 0m;
                ViewBag.TotalPnLPercentage = 0m;
                ViewBag.HoldingSummaries = new List<PortfolioHoldingSummary>();
                return;
            }

            var symbols = portfolio.Holdings
                .Select(h => h.AssetSymbol.ToUpperInvariant())
                .Distinct()
                .ToList();

            // Latest spot price per symbol
            var latestPrices = await _context.GeneralAssetPrices
                .Where(p => p.Type == "spot" && symbols.Contains(p.Symbol))
                .GroupBy(p => p.Symbol)
                .Select(g => g.OrderByDescending(p => p.Time).FirstOrDefault()!)
                .ToDictionaryAsync(p => p.Symbol, p => p.Price);

            // Per-symbol aggregation with weighted average buy price
            var summaries = portfolio.Holdings
                .GroupBy(h => h.AssetSymbol.ToUpperInvariant())
                .Select(g =>
                {
                    var totalQty = g.Sum(x => x.Quantity);
                    var weightedCost = g.Sum(x => x.Quantity * x.AverageBuyPrice);

                    return new PortfolioHoldingSummary
                    {
                        AssetSymbol = g.Key,
                        Quantity = totalQty,
                        AverageBuyPrice = totalQty > 0 ? (weightedCost / totalQty) : 0m,
                        CurrentPrice = latestPrices.TryGetValue(g.Key, out var px) ? px : 0m
                    };
                })
                .OrderByDescending(s => s.CurrentValue)
                .ToList();

            var totalValue = summaries.Sum(s => s.CurrentValue);
            var totalCost = summaries.Sum(s => s.CostBasis);

            foreach (var s in summaries)
            {
                s.PortfolioPercentage = totalValue > 0 ? (s.CurrentValue / totalValue) * 100m : 0m;
            }

            portfolio.TotalValue = totalValue;

            ViewBag.TotalCost = totalCost;
            ViewBag.TotalPnL = totalValue - totalCost;
            ViewBag.TotalPnLPercentage = totalCost > 0 ? ((totalValue - totalCost) / totalCost) * 100m : 0m;

            // Dashboard.cshtml relies on this
            ViewBag.HoldingSummaries = summaries;

            // For UI display (remove /USDT)
            ViewBag.DisplaySymbol = (Func<string?, string>)DisplaySymbol;
        }

        // GET: Create Portfolio
        public async Task<IActionResult> Create()
        {
            var userId = GetUserId();
            var limit = await GetPortfolioLimitAsync(userId);
            var portfolioCount = await _context.Portfolios.CountAsync(p => p.UserId == userId);

            if (portfolioCount >= limit)
            {
                TempData["ErrorMessage"] = limit == 1
                    ? "Freemium accounts are limited to 1 portfolio. Upgrade to Pro for up to 3 portfolios."
                    : $"You have reached the maximum of {limit} portfolios for your plan.";
                return RedirectToAction("Dashboard");
            }

            return PartialView("_Create", new Portfolio());
        }

        // POST: Create Portfolio
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("Name,IsDefault")] Portfolio portfolio)
        {
            var userId = GetUserId();
            ModelState.Remove(nameof(userId));
            if (ModelState.IsValid)
            {
                var portfolioCount = await _context.Portfolios.CountAsync(p => p.UserId == userId);
                var limit = await GetPortfolioLimitAsync(userId);

                if (portfolioCount >= limit)
                {
                    TempData["ErrorMessage"] = limit == 1
                        ? "Freemium accounts are limited to 1 portfolio. Upgrade to Pro for up to 3 portfolios."
                        : $"You have reached the maximum of {limit} portfolios for your plan.";
                    return RedirectToAction("Dashboard");
                }

                portfolio.UserId = userId;
                portfolio.CreatedDate = DateTime.UtcNow;

                // Handle default portfolio
                if (portfolio.IsDefault)
                {
                    var existingDefaults = await _context.Portfolios
                        .Where(p => p.UserId == userId && p.IsDefault)
                        .ToListAsync();

                    foreach (var p in existingDefaults)
                    {
                        p.IsDefault = false;
                        _context.Update(p);
                    }
                }
                else if (portfolioCount == 0)
                {
                    // First portfolio is always default
                    portfolio.IsDefault = true;
                }

                _context.Add(portfolio);
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = $"Portfolio '{portfolio.Name}' created successfully!";

                // If submitted via AJAX, tell the client where to go next (same as AddHolding).
                if (Request.Headers.XRequestedWith == "XMLHttpRequest")
                {
                    var redirectUrl = Url.Action("Dashboard", "Portfolio", new { portfolioId = portfolio.Id }) ?? "/Portfolio/Dashboard";
                    return Json(new { redirectUrl });
                }

                return RedirectToAction("Dashboard", new { portfolioId = portfolio.Id });
            }

            return PartialView("_Create", portfolio);
        }

        // GET: Rename Portfolio
        [HttpGet]
        public async Task<IActionResult> Rename(int id)
        {
            var userId = GetUserId();

            var portfolio = await _context.Portfolios
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId);

            if (portfolio == null)
                return NotFound();

            return PartialView("_RenamePortfolio", portfolio);
        }

        // POST: Rename Portfolio
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Rename(int id, [Bind("Name")] Portfolio input)
        {
            var userId = GetUserId();

            var portfolio = await _context.Portfolios
                .FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId);

            if (portfolio == null)
                return NotFound();

            if (string.IsNullOrWhiteSpace(input.Name))
            {
                ModelState.AddModelError(nameof(Portfolio.Name), "Name is required.");
            }
            else if (input.Name.Length > 100)
            {
                ModelState.AddModelError(nameof(Portfolio.Name), "Name must be 100 characters or less.");
            }

            ModelState.Remove(nameof(userId));
            if (!ModelState.IsValid)
            {
                // Keep Id so the modal can post back correctly
                portfolio.Name = input.Name ?? portfolio.Name;
                return PartialView("_RenamePortfolio", portfolio);
            }

            portfolio.Name = input.Name.Trim();
            _context.Update(portfolio);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"Portfolio renamed to '{portfolio.Name}'.";

            if (Request.Headers.XRequestedWith == "XMLHttpRequest")
            {
                var redirectUrl = Url.Action("Dashboard", "Portfolio", new { portfolioId = portfolio.Id }) ?? "/Portfolio/Dashboard";
                return Json(new { redirectUrl });
            }

            return RedirectToAction("Dashboard", new { portfolioId = portfolio.Id });
        }

        // GET: Delete Portfolio
        [HttpGet]
        public async Task<IActionResult> Delete(int id)
        {
            var userId = GetUserId();

            var portfolio = await _context.Portfolios
                .Include(p => p.Holdings)
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId);

            if (portfolio == null)
                return NotFound();

            return PartialView("_DeletePortfolio", portfolio);
        }

        // POST: Delete Portfolio
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var userId = GetUserId();

            var portfolio = await _context.Portfolios
                .Include(p => p.Holdings)
                .FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId);

            if (portfolio == null)
                return NotFound();

            var wasDefault = portfolio.IsDefault;

            // Remove holdings first (safe even if cascade exists; matches explicit behavior)
            if (portfolio.Holdings.Any())
                _context.PortfolioHoldings.RemoveRange(portfolio.Holdings);

            _context.Portfolios.Remove(portfolio);
            await _context.SaveChangesAsync();

            // If they deleted the default, promote another portfolio to default (if any)
            if (wasDefault)
            {
                var newDefault = await _context.Portfolios
                    .Where(p => p.UserId == userId)
                    .OrderBy(p => p.CreatedDate)
                    .FirstOrDefaultAsync();

                if (newDefault != null)
                {
                    newDefault.IsDefault = true;
                    _context.Update(newDefault);
                    await _context.SaveChangesAsync();
                }
            }

            TempData["SuccessMessage"] = $"Portfolio '{portfolio.Name}' deleted.";

            if (Request.Headers.XRequestedWith == "XMLHttpRequest")
            {
                var redirectUrl = Url.Action("Dashboard", "Portfolio") ?? "/Portfolio/Dashboard";
                return Json(new { redirectUrl });
            }

            return RedirectToAction("Dashboard");
        }

        // GET: Add Holding
        public async Task<IActionResult> AddHolding(int portfolioId)
        {
            var userId = GetUserId();
            var portfolio = await _context.Portfolios
                .FirstOrDefaultAsync(p => p.Id == portfolioId && p.UserId == userId);

            if (portfolio == null)
                return NotFound();

            var spotSymbols = await _context.GeneralAssetPrices
                .Where(p => p.Type == "spot")
                .Select(p => p.Symbol)
                .Distinct()
                .OrderBy(s => s)
                .ToListAsync();

            ViewBag.PortfolioId = portfolioId;
            ViewBag.SpotSymbols = spotSymbols;

            // For UI display (remove /USDT)
            ViewBag.DisplaySymbol = (Func<string?, string>)DisplaySymbol;

            return PartialView("_AddHolding", new PortfolioHolding());
        }

        // POST: Add Holding
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddHolding(int portfolioId, [Bind("AssetSymbol,Quantity,AverageBuyPrice,Notes")] PortfolioHolding holding)
        {
            var userId = GetUserId();
            var portfolio = await _context.Portfolios
                .FirstOrDefaultAsync(p => p.Id == portfolioId && p.UserId == userId);

            if (portfolio == null)
                return NotFound();

            var assetExists = await _context.GeneralAssetPrices
                .AnyAsync(p => p.Type == "spot" && p.Symbol.ToUpper() == holding.AssetSymbol.ToUpper());

            if (!assetExists)
            {
                ModelState.AddModelError(nameof(holding.AssetSymbol), "Asset symbol not found in spot assets.");
            }

            ModelState.Remove(nameof(portfolio));

            if (!ModelState.IsValid)
            {
                var spotSymbols = await _context.GeneralAssetPrices
                    .Where(p => p.Type == "spot")
                    .Select(p => p.Symbol)
                    .Distinct()
                    .OrderBy(s => s)
                    .ToListAsync();

                ViewBag.PortfolioId = portfolioId;
                ViewBag.SpotSymbols = spotSymbols;

                return PartialView("_AddHolding", holding);
            }

            holding.PortfolioId = portfolioId;
            holding.AssetSymbol = holding.AssetSymbol.ToUpperInvariant();
            holding.LastUpdated = DateTime.UtcNow;

            _context.Add(holding);
            await _context.SaveChangesAsync();

            // CHANGED: remove /USDT in the toast
            TempData["SuccessMessage"] = $"{DisplaySymbol(holding.AssetSymbol)} added to portfolio!";

            if (Request.Headers.XRequestedWith == "XMLHttpRequest")
            {
                var redirectUrl = Url.Action("Dashboard", "Portfolio", new { portfolioId }) ?? "/Portfolio/Dashboard";
                return Json(new { redirectUrl });
            }

            return RedirectToAction("Dashboard", new { portfolioId });
        }

        // GET: Edit Holding
        public async Task<IActionResult> EditHolding(int id)
        {
            var userId = GetUserId();
            var holding = await _context.PortfolioHoldings
                .Include(h => h.Portfolio)
                .FirstOrDefaultAsync(h => h.Id == id && h.Portfolio.UserId == userId);

            if (holding == null)
                return NotFound();

            // Get current price
            var currentPrice = await _context.GeneralAssetPrices
                .Where(p => p.Symbol.ToUpper() == holding.AssetSymbol.ToUpper())
                .OrderByDescending(p => p.Time)
                .Select(p => p.Price)
                .FirstOrDefaultAsync();

            holding.CurrentPrice = currentPrice;

            // For UI display (remove /USDT)
            ViewBag.DisplaySymbol = (Func<string?, string>)DisplaySymbol;

            return PartialView("_EditHolding", holding);
        }

        // POST: Edit Holding
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditHolding(int id, [Bind("Id,AssetSymbol,Quantity,AverageBuyPrice,Notes")] PortfolioHolding holding)
        {
            if (id != holding.Id)
                return NotFound();

            var userId = GetUserId();
            var existingHolding = await _context.PortfolioHoldings
                .Include(h => h.Portfolio)
                .FirstOrDefaultAsync(h => h.Id == id && h.Portfolio.UserId == userId);

            if (existingHolding == null)
                return NotFound();

            if (!existingHolding.AssetSymbol.Equals(holding.AssetSymbol, StringComparison.OrdinalIgnoreCase))
            {
                var assetExists = await _context.GeneralAssetPrices
                    .AnyAsync(p => p.Symbol.ToUpper() == holding.AssetSymbol.ToUpper());

                if (!assetExists)
                {
                    ModelState.AddModelError("AssetSymbol", "Asset symbol not found.");
                }
            }

            ModelState.Remove(nameof(Portfolio));
            if (ModelState.IsValid)
            {
                existingHolding.AssetSymbol = holding.AssetSymbol.ToUpper();
                existingHolding.Quantity = holding.Quantity;
                existingHolding.AverageBuyPrice = holding.AverageBuyPrice;
                existingHolding.Notes = holding.Notes;
                existingHolding.LastUpdated = DateTime.UtcNow;

                _context.Update(existingHolding);
                await _context.SaveChangesAsync();

                // CHANGED: remove /USDT in the toast
                TempData["SuccessMessage"] = $"{DisplaySymbol(existingHolding.AssetSymbol)} updated successfully!";
                return RedirectToAction("Dashboard", new { portfolioId = existingHolding.PortfolioId });
            }

            return PartialView("_EditHolding", holding);
        }

        // POST: Delete Holding
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteHolding(int id)
        {
            var userId = GetUserId();
            var holding = await _context.PortfolioHoldings
                .Include(h => h.Portfolio)
                .FirstOrDefaultAsync(h => h.Id == id && h.Portfolio.UserId == userId);

            if (holding == null)
                return NotFound();

            var portfolioId = holding.PortfolioId;
            _context.PortfolioHoldings.Remove(holding);
            await _context.SaveChangesAsync();

            // CHANGED: remove /USDT in the toast
            TempData["SuccessMessage"] = $"{DisplaySymbol(holding.AssetSymbol)} removed from portfolio.";
            return RedirectToAction("Dashboard", new { portfolioId });
        }

        // POST: Set Default Portfolio
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SetDefault(int id)
        {
            var userId = GetUserId();
            var portfolio = await _context.Portfolios
                .FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId);

            if (portfolio == null)
                return NotFound();

            // Remove default from all user portfolios
            var userPortfolios = await _context.Portfolios
                .Where(p => p.UserId == userId)
                .ToListAsync();

            foreach (var p in userPortfolios)
            {
                p.IsDefault = (p.Id == id);
                _context.Update(p);
            }

            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = $"{portfolio.Name} set as default portfolio.";
            return RedirectToAction("Dashboard", new { portfolioId = id });
        }

        // AJAX: Get current price
        [HttpGet]
        public async Task<JsonResult> GetCurrentPrice(string symbol)
        {
            if (string.IsNullOrWhiteSpace(symbol))
                return Json(new { price = 0m });

            var price = await _context.GeneralAssetPrices
                .Where(p => p.Type == "spot" && p.Symbol.ToUpper() == symbol.ToUpper())
                .OrderByDescending(p => p.Time)
                .Select(p => p.Price)
                .FirstOrDefaultAsync();

            return Json(new { price });
        }

        // GET: Manage Asset
        [HttpGet]
        public async Task<IActionResult> ManageAsset(int portfolioId, string symbol)
        {
            var userId = GetUserId();
            if (string.IsNullOrWhiteSpace(symbol))
                return BadRequest();

            symbol = symbol.ToUpperInvariant();

            var portfolio = await _context.Portfolios
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == portfolioId && p.UserId == userId);

            if (portfolio == null)
                return NotFound();

            var lots = await _context.PortfolioHoldings
                .Where(h => h.PortfolioId == portfolioId && h.AssetSymbol.ToUpper() == symbol)
                .OrderByDescending(h => h.LastUpdated)
                .ToListAsync();

            // spot
            var spotPrice = await _context.GeneralAssetPrices
                .Where(p => p.Type == "spot" && p.Symbol.ToUpper() == symbol)
                .OrderByDescending(p => p.Time)
                .Select(p => p.Price)
                .FirstOrDefaultAsync();

            // latest candle (optional, for overview)
            //var latestCandle = await _context.GeneralAssetPrices
            //    .Where(p => p.Type == "candle" && p.Symbol.ToUpper() == symbol)
            //    .OrderByDescending(p => p.Time)
            //    .FirstOrDefaultAsync();

            var vm = new AssetHoldingsModalViewModel
            {
                PortfolioId = portfolioId,
                Symbol = symbol,
                SpotPrice = spotPrice,
                //LatestCandle = latestCandle,
                Lots = lots
            };

            // Populate CurrentPrice for the per-lot computed props in the view if needed
            foreach (var lot in vm.Lots)
                lot.CurrentPrice = spotPrice;

            return PartialView("_ManageAsset", vm);
        }
    }
}