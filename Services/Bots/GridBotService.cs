using AutoSignals.Data;
using AutoSignals.Models;
using AutoSignals.Models.Bots;
using Microsoft.EntityFrameworkCore;

namespace AutoSignals.Services.Bots
{
    public class GridBotService : IBotService<GridBot>
    {
        private readonly AutoSignalsDbContext _context;
        private readonly ITelegramNotifier _telegram;
        private readonly ILogger<GridBotService> _logger;

        public GridBotService(AutoSignalsDbContext context, ITelegramNotifier telegram, ILogger<GridBotService> logger)
        {
            _context = context;
            _telegram = telegram;
            _logger = logger;
        }

        public async Task<GridBot> CreateAsync(GridBot bot)
        {
            if (bot.LowerPrice <= 0 || bot.UpperPrice <= bot.LowerPrice)
                throw new ArgumentException("UpperPrice must be greater than LowerPrice.");
            if (bot.GridCount < 2 || bot.GridCount > 100)
                throw new ArgumentException("GridCount must be between 2 and 100.");

            bot.BotType = BotType.Grid;
            bot.Status = BotStatus.Idle;
            bot.CreatedAt = DateTime.UtcNow;
            bot.UpdatedAt = DateTime.UtcNow;
            bot.GridInitialised = false;
            _context.Set<GridBot>().Add(bot);
            await _context.SaveChangesAsync();
            return bot;
        }

        public async Task StartAsync(int botId, CancellationToken ct = default)
        {
            var bot = await GetAsync(botId) ?? throw new KeyNotFoundException($"Grid bot {botId} not found.");

            var userData = await _context.UsersData.FirstOrDefaultAsync(u => u.Id == bot.UserId, ct);
            if (userData?.SubscriptionTier != SubscriptionTier.VIP)
                throw new UnauthorizedAccessException("Bots require a VIP subscription.");

            if (bot.Status == BotStatus.Running)
                throw new InvalidOperationException("Bot is already running.");

            bot.Status = BotStatus.Running;
            bot.ErrorMessage = null;
            bot.LastRunAt = DateTime.UtcNow;
            bot.UpdatedAt = DateTime.UtcNow;

            // Reset runtime state for a fresh start
            bot.GridInitialised = false;
            bot.FilledOrderCount = 0;
            bot.TotalInvested = 0m;
            bot.TotalProfit = 0m;

            await _context.SaveChangesAsync(ct);

            _logger.LogInformation("Grid bot {BotId} started by user {UserId}.", botId, bot.UserId);
            await _telegram.SendDirectMessageToUserAsync(bot.UserId,
                $"▶️ <b>Grid Bot started</b>\n<code>{bot.Label ?? bot.Symbol}</code>", ct);
        }

        public async Task StopAsync(int botId)
        {
            var bot = await GetAsync(botId) ?? throw new KeyNotFoundException($"Grid bot {botId} not found.");

            bot.Status = BotStatus.Idle;
            bot.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            _logger.LogInformation("Grid bot {BotId} stopped.", botId);
            await _telegram.SendDirectMessageToUserAsync(bot.UserId,
                $"⏹ <b>Grid Bot stopped</b>\n<code>{bot.Label ?? bot.Symbol}</code>");
        }

        public async Task PauseAsync(int botId)
        {
            var bot = await GetAsync(botId) ?? throw new KeyNotFoundException($"Grid bot {botId} not found.");
            bot.Status = BotStatus.Paused;
            bot.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }

        public async Task<GridBot?> GetAsync(int botId)
        {
            return await _context.Set<GridBot>()
                .Include(b => b.ExchangeConnection)
                    .ThenInclude(c => c!.Exchange)
                .FirstOrDefaultAsync(b => b.Id == botId);
        }

        public async Task<List<GridBot>> GetForUserAsync(string userId)
        {
            return await _context.Set<GridBot>()
                .Include(b => b.ExchangeConnection)
                    .ThenInclude(c => c!.Exchange)
                .Where(b => b.UserId == userId)
                .OrderByDescending(b => b.CreatedAt)
                .ToListAsync();
        }

        public async Task DeleteAsync(int botId)
        {
            var bot = await GetAsync(botId) ?? throw new KeyNotFoundException($"Grid bot {botId} not found.");
            if (bot.Status == BotStatus.Running)
                throw new InvalidOperationException("Stop the bot before deleting it.");

            _context.Set<GridBot>().Remove(bot);
            await _context.SaveChangesAsync();
        }
    }
}
