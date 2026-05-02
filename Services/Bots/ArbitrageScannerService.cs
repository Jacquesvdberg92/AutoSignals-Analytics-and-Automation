using AutoSignals.Data;
using AutoSignals.Models;
using AutoSignals.Models.Bots;
using Microsoft.EntityFrameworkCore;

namespace AutoSignals.Services.Bots
{
    public class ArbitrageScannerService : IBotService<ArbitrageScannerBot>
    {
        private readonly AutoSignalsDbContext _context;
        private readonly ITelegramNotifier _telegram;
        private readonly ILogger<ArbitrageScannerService> _logger;

        public ArbitrageScannerService(AutoSignalsDbContext context, ITelegramNotifier telegram, ILogger<ArbitrageScannerService> logger)
        {
            _context = context;
            _telegram = telegram;
            _logger = logger;
        }

        public async Task<ArbitrageScannerBot> CreateAsync(ArbitrageScannerBot bot)
        {
            bot.BotType = BotType.ArbitrageScanner;
            bot.Status = BotStatus.Idle;
            bot.Symbol = "MULTI";
            bot.CreatedAt = DateTime.UtcNow;
            bot.UpdatedAt = DateTime.UtcNow;
            bot.TotalOpportunitiesFound = 0;
            bot.LastAlertAt = null;
            _context.Set<ArbitrageScannerBot>().Add(bot);
            await _context.SaveChangesAsync();
            return bot;
        }

        public async Task StartAsync(int botId, CancellationToken ct = default)
        {
            var bot = await GetAsync(botId) ?? throw new KeyNotFoundException($"Arbitrage scanner bot {botId} not found.");

            var userData = await _context.UsersData.FirstOrDefaultAsync(u => u.Id == bot.UserId, ct);
            if (userData?.SubscriptionTier != SubscriptionTier.VIP)
                throw new UnauthorizedAccessException("Bots require a VIP subscription.");

            if (bot.Status == BotStatus.Running)
                throw new InvalidOperationException("Bot is already running.");

            bot.Status = BotStatus.Running;
            bot.ErrorMessage = null;
            bot.LastRunAt = DateTime.UtcNow;
            bot.UpdatedAt = DateTime.UtcNow;
            bot.LastAlertAt = null;

            await _context.SaveChangesAsync(ct);

            _logger.LogInformation("Arbitrage scanner bot {BotId} started by user {UserId}.", botId, bot.UserId);
            await _telegram.SendDirectMessageToUserAsync(bot.UserId,
                $"▶️ <b>Arbitrage Scanner started</b>\n<code>{bot.Label ?? "Scanner"}</code>", ct);
        }

        public async Task StopAsync(int botId)
        {
            var bot = await GetAsync(botId) ?? throw new KeyNotFoundException($"Arbitrage scanner bot {botId} not found.");
            bot.Status = BotStatus.Idle;
            bot.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            _logger.LogInformation("Arbitrage scanner bot {BotId} stopped.", botId);
            await _telegram.SendDirectMessageToUserAsync(bot.UserId,
                $"⏹ <b>Arbitrage Scanner stopped</b>\n<code>{bot.Label ?? "Scanner"}</code>");
        }

        public async Task PauseAsync(int botId)
        {
            var bot = await GetAsync(botId) ?? throw new KeyNotFoundException($"Arbitrage scanner bot {botId} not found.");
            bot.Status = BotStatus.Paused;
            bot.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }

        public async Task<ArbitrageScannerBot?> GetAsync(int botId)
        {
            return await _context.Set<ArbitrageScannerBot>()
                .Include(b => b.ExchangeConnection)
                    .ThenInclude(c => c!.Exchange)
                .FirstOrDefaultAsync(b => b.Id == botId);
        }

        public async Task<List<ArbitrageScannerBot>> GetForUserAsync(string userId)
        {
            return await _context.Set<ArbitrageScannerBot>()
                .Include(b => b.ExchangeConnection)
                    .ThenInclude(c => c!.Exchange)
                .Where(b => b.UserId == userId)
                .OrderByDescending(b => b.CreatedAt)
                .ToListAsync();
        }

        public async Task DeleteAsync(int botId)
        {
            var bot = await GetAsync(botId) ?? throw new KeyNotFoundException($"Arbitrage scanner bot {botId} not found.");
            if (bot.Status == BotStatus.Running)
                throw new InvalidOperationException("Stop the bot before deleting it.");

            _context.Set<ArbitrageScannerBot>().Remove(bot);
            await _context.SaveChangesAsync();
        }
    }
}
