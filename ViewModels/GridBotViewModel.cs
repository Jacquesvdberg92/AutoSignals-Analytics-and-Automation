using AutoSignals.Models;
using AutoSignals.Models.Bots;

namespace AutoSignals.ViewModels
{
    public class GridBotViewModel
    {
        public List<GridBot> Bots { get; set; } = new();
        public List<UserExchangeConnection> Connections { get; set; } = new();
        public List<string> FuturesSymbols { get; set; } = new();
        public string? Error { get; set; }
    }
}
