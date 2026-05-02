using AutoSignals.Models.Bots;

namespace AutoSignals.Services.Bots
{
    /// <summary>
    /// Maps BotType discriminators to their engine implementations.
    /// Engines are registered at startup and resolved here by BotEngineHostedService.
    /// </summary>
    public class BotEngineRegistry
    {
        private readonly Dictionary<BotType, IBotEngine> _engines;

        public BotEngineRegistry(IEnumerable<IBotEngine> engines)
        {
            _engines = engines.ToDictionary(e => e.SupportedBotType);
        }

        public IBotEngine? Resolve(BotType botType)
        {
            _engines.TryGetValue(botType, out var engine);
            return engine;
        }
    }
}
