namespace AutoSignals.Models
{
    public class ProviderSettings
    {
        public int Id { get; set; }
        public string UserId { get; set; }
        public string ProviderId { get; set; }
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

        public ProviderSettings()
        {
            IsEnabled = false;
            Testing = false;
            OverideLeverage = true;
            Leverage = 3;
            IgnorLong = false;
            IgnorShort = false;
            IgnoreStoploss = false;
            UseStoploss = true;
            StoplossPercentage = 10;
            MoveStoploss = true;
            MoveStoplossOn = 1;
            TpCount = 1;
            TpPercentages.Add(100);
            RiskPercentage = 3;
            MaxTradeSizeUsd = 100;
            MinTradeSizeUsd = 10;
            IsIsolated = true;
            UseMoonbag = true;
            MoonbagPercentage = 10;
            MoonbagSize = "25";
            Time = DateTime.Now;
        }
    }
}

