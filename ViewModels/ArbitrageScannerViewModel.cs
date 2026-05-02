using AutoSignals.Models;
using AutoSignals.Models.Bots;

namespace AutoSignals.ViewModels
{
    public class ArbitrageScannerViewModel
    {
        public List<ArbitrageScannerBot> Bots { get; set; } = new();
        public List<UserExchangeConnection> Connections { get; set; } = new();

        /// <summary>Opportunities for the currently selected/first scanner, pre-loaded for display.</summary>
        public List<ArbitrageOpportunity> RecentOpportunities { get; set; } = new();

        public List<string> SpotSymbols { get; set; } = new();
        public string? Error { get; set; }
    }
}
