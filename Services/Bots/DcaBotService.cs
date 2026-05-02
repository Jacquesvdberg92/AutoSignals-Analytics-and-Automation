using AutoSignals.Data;
using AutoSignals.Models;
using AutoSignals.Models.Bots;
using Microsoft.EntityFrameworkCore;

namespace AutoSignals.Services.Bots
{
    public class DcaBotService : IBotService<DcaBot>
    {
        private readonly AutoSignalsDbContext _context;
        private readonly ITelegramNotifier _telegram;
        private readonly ILogger<DcaBotService> _logger;

        public DcaBotService(AutoSignalsDbContext context, ITelegramNotifier telegram, ILogger<DcaBotService> logger)
        {
            _context = context;
            _telegram = telegram;
            _logger = logger;
        }

        public async Task<DcaBot> CreateAsync(DcaBot bot)
        {
            bot.BotType = BotType.DCA;
            bot.Status = BotStatus.Idle;
            bot.CreatedAt = DateTime.UtcNow;
            bot.UpdatedAt = DateTime.UtcNow;
            _context.Set<DcaBot>().Add(bot);
            await _context.SaveChangesAsync();
            return bot;
        }

        public async Task StartAsync(int botId, CancellationToken ct = default)
        {
            var bot = await GetAsync(botId) ?? throw new KeyNotFoundException($"DCA bot {botId} not found.");

            // VIP gate
            var userData = await _context.UsersData.FirstOrDefaultAsync(u => u.Id == bot.UserId, ct);
            if (userData?.SubscriptionTier != SubscriptionTier.VIP)
                throw new UnauthorizedAccessException("Bots require a VIP subscription.");

            if (bot.Status == BotStatus.Running)
                throw new InvalidOperationException("Bot is already running.");

            bot.Status = BotStatus.Running;
            bot.ErrorMessage = null;
            bot.LastRunAt = DateTime.UtcNow;
            bot.UpdatedAt = DateTime.UtcNow;

            // Reset state for a fresh start
            bot.CurrentSafetyOrderCount = 0;
            bot.AverageEntryPrice = null;
            bot.TotalInvested = 0m;
            bot.CooldownUntil = null;

            await _context.SaveChangesAsync(ct);

            _logger.LogInformation("DCA bot {BotId} started by user {UserId}.", botId, bot.UserId);
            await _telegram.SendDirectMessageToUserAsync(bot.UserId,
                $"▶️ <b>DCA Bot started</b>\n<code>{bot.Label ?? bot.Symbol}</code>", ct);
        }

        public async Task StopAsync(int botId)
        {
            var bot = await GetAsync(botId) ?? throw new KeyNotFoundException($"DCA bot {botId} not found.");

            if (bot.Status == BotStatus.Idle || bot.Status == BotStatus.Completed)
                return;

            bot.Status = BotStatus.Idle;
            bot.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            _logger.LogInformation("DCA bot {BotId} stopped.", botId);
            await _telegram.SendDirectMessageToUserAsync(bot.UserId,
                $"⏹️ <b>DCA Bot stopped</b>\n<code>{bot.Label ?? bot.Symbol}</code>");
        }

        public async Task PauseAsync(int botId)
        {
            var bot = await GetAsync(botId) ?? throw new KeyNotFoundException($"DCA bot {botId} not found.");
            if (bot.Status != BotStatus.Running) return;

            bot.Status = BotStatus.Paused;
            bot.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }

        public async Task<DcaBot?> GetAsync(int botId)
        {
            return await _context.Set<DcaBot>().FirstOrDefaultAsync(b => b.Id == botId);
        }

        public async Task<List<DcaBot>> GetForUserAsync(string userId)
        {
            return await _context.Set<DcaBot>()
                .Where(b => b.UserId == userId)
                .Include(b => b.ExchangeConnection)
                    .ThenInclude(c => c!.Exchange)
                .OrderByDescending(b => b.CreatedAt)
                .ToListAsync();
        }

        public async Task DeleteAsync(int botId)
        {
            var bot = await GetAsync(botId) ?? throw new KeyNotFoundException($"DCA bot {botId} not found.");

            if (bot.Status == BotStatus.Running)
                throw new InvalidOperationException("Stop the bot before deleting it.");

            _context.Set<DcaBot>().Remove(bot);
            await _context.SaveChangesAsync();
        }
    }
}
