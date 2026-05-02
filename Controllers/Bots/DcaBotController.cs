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
    [Route("VipBots/DCA")]
    public class DcaBotController : Controller
    {
        private readonly DcaBotService _botService;
        private readonly AutoSignalsDbContext _context;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly ILogger<DcaBotController> _logger;

        public DcaBotController(DcaBotService botService, AutoSignalsDbContext context,
            UserManager<IdentityUser> userManager, ILogger<DcaBotController> logger)
        {
            _botService = botService;
            _context = context;
            _userManager = userManager;
            _logger = logger;
        }

        // GET /VipBots/DCA
        [HttpGet("")]
        public async Task<IActionResult> Index()
        {
            var userId = _userManager.GetUserId(User)!;
            var bots = await _botService.GetForUserAsync(userId);
            var connections = await _context.UserExchangeConnections
                .Where(c => c.UserId == userId && c.IsActive)
                .Include(c => c.Exchange)
                .ToListAsync();

            var futuresSymbols = await _context.GeneralAssetPrices
                .Where(g => g.Type == "swap")
                .Select(g => g.Symbol)
                .Distinct()
                .OrderBy(s => s)
                .ToListAsync();

            return View("~/Views/VipBots/DCA/Index.cshtml", new DcaBotViewModel
            {
                Bots = bots,
                Connections = connections,
                FuturesSymbols = futuresSymbols
            });
        }

        // POST /VipBots/DCA/Create
        [HttpPost("Create")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(DcaBot bot)
        {
            var userId = _userManager.GetUserId(User)!;
            bot.UserId = userId;

            try
            {
                await _botService.CreateAsync(bot);
                TempData["Success"] = "DCA bot created successfully.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create DCA bot for user {UserId}.", userId);
                TempData["Error"] = $"Failed to create bot: {ex.Message}";
            }

            return RedirectToAction(nameof(Index));
        }

        // POST /VipBots/DCA/Start/{id}
        [HttpPost("Start/{id:int}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Start(int id)
        {
            try
            {
                await _botService.StartAsync(id);
                TempData["Success"] = "Bot started.";
            }
            catch (UnauthorizedAccessException)
            {
                TempData["Error"] = "A VIP subscription is required to run bots.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start DCA bot {BotId}.", id);
                TempData["Error"] = $"Failed to start bot: {ex.Message}";
            }

            return RedirectToAction(nameof(Index));
        }

        // POST /VipBots/DCA/Stop/{id}
        [HttpPost("Stop/{id:int}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Stop(int id)
        {
            try
            {
                await _botService.StopAsync(id);
                TempData["Success"] = "Bot stopped.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to stop DCA bot {BotId}.", id);
                TempData["Error"] = $"Failed to stop bot: {ex.Message}";
            }

            return RedirectToAction(nameof(Index));
        }

        // POST /VipBots/DCA/Delete/{id}
        [HttpPost("Delete/{id:int}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            try
            {
                await _botService.DeleteAsync(id);
                TempData["Success"] = "Bot deleted.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete DCA bot {BotId}.", id);
                TempData["Error"] = $"Failed to delete bot: {ex.Message}";
            }

            return RedirectToAction(nameof(Index));
        }
    }
}
