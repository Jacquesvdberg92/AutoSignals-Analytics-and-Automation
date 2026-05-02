using AutoSignals.Data;
using AutoSignals.Models;
using AutoSignals.Models.Bots;
using AutoSignals.Services.Bots;
using AutoSignals.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AutoSignals.Controllers.Bots
{
    [Authorize(Policy = "RequiresVIP")]
    [Route("VipBots/Arbitrage")]
    public class ArbitrageScannerController : Controller
    {
        private readonly ArbitrageScannerService _botService;
        private readonly AutoSignalsDbContext _context;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly ILogger<ArbitrageScannerController> _logger;

        public ArbitrageScannerController(ArbitrageScannerService botService, AutoSignalsDbContext context,
            UserManager<IdentityUser> userManager, ILogger<ArbitrageScannerController> logger)
        {
            _botService = botService;
            _context = context;
            _userManager = userManager;
            _logger = logger;
        }

        // GET /VipBots/Arbitrage
        [HttpGet("")]
        public async Task<IActionResult> Index()
        {
            var userId = _userManager.GetUserId(User)!;
            var bots = await _botService.GetForUserAsync(userId);
            var connections = await _context.UserExchangeConnections
                .Where(c => c.UserId == userId && c.IsActive)
                .Include(c => c.Exchange)
                .ToListAsync();

            // Load the 50 most recent opportunities across all scanners owned by this user
            var scannerIds = bots.Select(b => b.Id).ToList();
            var recentOpps = scannerIds.Any()
                ? await _context.ArbitrageOpportunities
                    .Where(o => scannerIds.Contains(o.ScannerId))
                    .OrderByDescending(o => o.DetectedAt)
                    .Take(50)
                    .ToListAsync()
                : new List<ArbitrageOpportunity>();

            return View("~/Views/VipBots/Arbitrage/Index.cshtml", new ArbitrageScannerViewModel
            {
                Bots = bots,
                Connections = connections,
                RecentOpportunities = recentOpps,
                SpotSymbols = await _context.GeneralAssetPrices
                    .Where(g => g.Type == "spot")
                    .Select(g => g.Symbol)
                    .Distinct()
                    .OrderBy(s => s)
                    .ToListAsync()
            });
        }

        // POST /VipBots/Arbitrage/Create
        [HttpPost("Create")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(ArbitrageScannerBot bot)
        {
            var userId = _userManager.GetUserId(User)!;
            bot.UserId = userId;

            try
            {
                await _botService.CreateAsync(bot);
                TempData["Success"] = "Arbitrage scanner created successfully.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create arbitrage scanner for user {UserId}.", userId);
                TempData["Error"] = $"Failed to create scanner: {ex.Message}";
            }

            return RedirectToAction(nameof(Index));
        }

        // POST /VipBots/Arbitrage/Start/{id}
        [HttpPost("Start/{id:int}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Start(int id)
        {
            try
            {
                await _botService.StartAsync(id);
                TempData["Success"] = "Scanner started.";
            }
            catch (UnauthorizedAccessException)
            {
                TempData["Error"] = "A VIP subscription is required to run bots.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start arbitrage scanner {BotId}.", id);
                TempData["Error"] = $"Failed to start scanner: {ex.Message}";
            }

            return RedirectToAction(nameof(Index));
        }

        // POST /VipBots/Arbitrage/Stop/{id}
        [HttpPost("Stop/{id:int}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Stop(int id)
        {
            try
            {
                await _botService.StopAsync(id);
                TempData["Success"] = "Scanner stopped.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to stop arbitrage scanner {BotId}.", id);
                TempData["Error"] = $"Failed to stop scanner: {ex.Message}";
            }

            return RedirectToAction(nameof(Index));
        }

        // POST /VipBots/Arbitrage/Delete/{id}
        [HttpPost("Delete/{id:int}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            try
            {
                await _botService.DeleteAsync(id);
                TempData["Success"] = "Scanner deleted.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete arbitrage scanner {BotId}.", id);
                TempData["Error"] = $"Failed to delete scanner: {ex.Message}";
            }

            return RedirectToAction(nameof(Index));
        }
    }
}
