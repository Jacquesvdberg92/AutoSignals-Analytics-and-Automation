using AutoSignals.Models.Bots;

namespace AutoSignals.Services.Bots
{
    /// <summary>
    /// Executes a single tick for a running bot.
    /// Each concrete engine (DCA, Grid, etc.) implements this interface.
    /// </summary>
    public interface IBotEngine
    {
        BotType SupportedBotType { get; }
        Task TickAsync(BotBase bot, CancellationToken ct);
    }
}
