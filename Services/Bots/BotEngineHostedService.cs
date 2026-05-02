using AutoSignals.Data;
using AutoSignals.Models.Bots;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AutoSignals.Services.Bots
{
    public class BotEngineOptions
    {
        public int TickIntervalSeconds { get; set; } = 10;
        public int ArbitrageTickIntervalSeconds { get; set; } = 5;
        public int MaxBotsPerUser { get; set; } = 5;
    }

    public class BotEngineHostedService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly BotEngineRegistry _registry;
        private readonly BotEngineOptions _options;
        private readonly ILogger<BotEngineHostedService> _logger;

        public BotEngineHostedService(
            IServiceScopeFactory scopeFactory,
            BotEngineRegistry registry,
            IOptions<BotEngineOptions> options,
            ILogger<BotEngineHostedService> logger)
        {
            _scopeFactory = scopeFactory;
            _registry = registry;
            _options = options.Value;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("BotEngineHostedService started.");

            while (!stoppingToken.IsCancellationRequested)
            {
                await TickAllBotsAsync(stoppingToken);
                await Task.Delay(TimeSpan.FromSeconds(_options.TickIntervalSeconds), stoppingToken);
            }
        }

        private async Task TickAllBotsAsync(CancellationToken ct)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AutoSignalsDbContext>();
                var errorLogService = scope.ServiceProvider.GetRequiredService<ErrorLogService>();

                var runningBots = await db.Bots
                    .Where(b => b.Status == BotStatus.Running)
                    .ToListAsync(ct);

                foreach (var bot in runningBots)
                {
                    var engine = _registry.Resolve(bot.BotType);
                    if (engine is null)
                    {
                        _logger.LogWarning("No engine registered for BotType {BotType} (BotId={BotId}).", bot.BotType, bot.Id);
                        continue;
                    }

                    try
                    {
                        await engine.TickAsync(bot, ct);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error ticking BotId={BotId}.", bot.Id);

                        bot.Status = BotStatus.Error;
                        bot.ErrorMessage = ex.Message;
                        bot.UpdatedAt = DateTime.UtcNow;
                        db.Bots.Update(bot);

                        await errorLogService.LogErrorAsync(
                            $"Bot {bot.Id} ({bot.BotType}) tick failed: {ex.Message}",
                            ex.StackTrace,
                            source: nameof(BotEngineHostedService));
                    }
                }

                await db.SaveChangesAsync(ct);
            }
            catch (OperationCanceledException)
            {
                // Graceful shutdown — expected.
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error in BotEngineHostedService tick loop.");
            }
        }
    }
}
