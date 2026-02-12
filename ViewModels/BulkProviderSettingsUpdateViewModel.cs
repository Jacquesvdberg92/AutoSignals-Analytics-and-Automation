using AutoSignals.Models;

namespace AutoSignals.ViewModels
{
    public class BulkProviderSettingsUpdateViewModel
    {
        public int Id { get; set; }
        public string UserId { get; set; }
        public List<int> ProviderId { get; set; }
        public bool IsEnabled { get; set; }
        public bool Testing { get; set; }

        public bool OverideLeverage { get; set; }
        public int Leverage { get; set; }

        public bool IgnorLong { get; set; }
        public bool IgnorShort { get; set; }

        public bool IgnoreStoploss { get; set; }
        public bool UseStoploss { get; set; }
        public double StoplossPercentage { get; set; }
        public bool MoveStoploss { get; set; }
        public int MoveStoplossOn { get; set; }

        public int TpCount { get; set; }
        public List<double> TpPercentages { get; set; } = new List<double>();

        public double RiskPercentage { get; set; }
        public double MaxTradeSizeUsd { get; set; }
        public double MinTradeSizeUsd { get; set; }

        public bool IsIsolated { get; set; }

        public bool UseMoonbag { get; set; }
        public int MoonbagPercentage { get; set; }
        public string MoonbagSize { get; set; }

        public DateTime Time { get; set; }
    }
}