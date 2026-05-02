namespace AutoSignals.ViewModels.Admin
{
    public class SignalPerformanceDashboardViewModel
    {
        // ── Tracking Health ──────────────────────────────────────────────────
        public int TotalPending { get; set; }
        public int TotalOpen { get; set; }
        public int ClosedToday { get; set; }
        public int CancelledToday { get; set; }
        public int ServiceErrorCount24h { get; set; }

        // ── Win / Loss Rates ─────────────────────────────────────────────────
        public int TotalClosed { get; set; }
        public int TotalWins { get; set; }
        public int TotalLosses { get; set; }
        public int TotalPartialWins { get; set; }
        public double WinRate { get; set; }
        public double LossRate { get; set; }
        public double PartialWinRate { get; set; }
        public double AvgTpsAchieved { get; set; }
        public double AvgProfitOnWins { get; set; }
        public double AvgLossOnLosses { get; set; }

        // ── TP Hit Rates ─────────────────────────────────────────────────────
        public double Tp1HitRate { get; set; }
        public double Tp2HitRate { get; set; }
        public double Tp3HitRate { get; set; }
        public double Tp4HitRate { get; set; }
        public double AvgDurationToCloseHours { get; set; }

        // ── Provider Breakdown ───────────────────────────────────────────────
        public List<ProviderPerformanceStat> ProviderStats { get; set; } = new();

        // ── Symbol Breakdown (top 20) ────────────────────────────────────────
        public List<SymbolPerformanceStat> SymbolStats { get; set; } = new();

        // ── Chart Data ───────────────────────────────────────────────────────
        /// <summary>Win / Loss / Partial / Cancelled pie</summary>
        public List<string> OutcomePieLabels { get; set; } = new();
        public List<int> OutcomePieValues { get; set; } = new();

        /// <summary>Signals opened vs closed last 30 days</summary>
        public List<string> DailyLabels { get; set; } = new();
        public List<int> DailyOpenedValues { get; set; } = new();
        public List<int> DailyClosedValues { get; set; } = new();

        /// <summary>P/L histogram</summary>
        public List<string> PlHistogramLabels { get; set; } = new();
        public List<int> PlHistogramValues { get; set; } = new();

        /// <summary>Provider win-rate bar (top 15)</summary>
        public List<string> ProviderWinRateLabels { get; set; } = new();
        public List<double> ProviderWinRateValues { get; set; } = new();
    }

    public class ProviderPerformanceStat
    {
        public string Provider { get; set; } = string.Empty;
        public int Total { get; set; }
        public int Wins { get; set; }
        public int Losses { get; set; }
        public int Open { get; set; }
        public int Cancelled { get; set; }
        public double WinRate { get; set; }
        public double AvgProfit { get; set; }
        public double AvgLoss { get; set; }
        public double AvgTpsAchieved { get; set; }
    }

    public class SymbolPerformanceStat
    {
        public string Symbol { get; set; } = string.Empty;
        public int Total { get; set; }
        public int Wins { get; set; }
        public double WinRate { get; set; }
        public double AvgPl { get; set; }
    }
}
