using AutoSignals.Models;

namespace AutoSignals.ViewModels
{
    public class ProviderRankViewModel
    {
        public Provider Provider { get; set; } = null!;
        public double WinRate { get; set; }
        public double RRR { get; set; }
        public int SignalCount { get; set; }
        public double AverageProfitPerTrade { get; set; }
        public double AverageLeverage { get; set; }
        public double StoplossPercentage { get; set; }
        public int LongRatio { get; set; }
        public int ShortRatio { get; set; }
    }
}
