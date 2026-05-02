using AutoSignals.Models.Bots;

namespace AutoSignals.Services.Bots
{
    public interface IBotService<TBot> where TBot : BotBase
    {
        Task<TBot> CreateAsync(TBot bot);
        Task StartAsync(int botId, CancellationToken ct = default);
        Task StopAsync(int botId);
        Task PauseAsync(int botId);
        Task<TBot?> GetAsync(int botId);
        Task<List<TBot>> GetForUserAsync(string userId);
        Task DeleteAsync(int botId);
    }
}
