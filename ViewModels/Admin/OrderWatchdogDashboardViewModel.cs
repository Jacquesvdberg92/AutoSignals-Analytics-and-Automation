namespace AutoSignals.ViewModels.Admin
{
    public class OrderWatchdogDashboardViewModel
    {
        // ── Pipeline Health ──────────────────────────────────────────────────
        public int TotalOpenOrders { get; set; }
        public int TotalPendingOrders { get; set; }
        public int ExecutedLast24h { get; set; }
        public int CancelledLast24h { get; set; }
        public double AvgExecutionMinutes { get; set; }

        // ── Execution Breakdown ──────────────────────────────────────────────
        public int EntryOrdersOpen { get; set; }
        public int EntryOrdersExecuted24h { get; set; }
        public int EntryOrdersCancelled24h { get; set; }
        public int DcaOrdersOpen { get; set; }
        public int DcaOrdersExecuted24h { get; set; }
        public int StoplossOrdersExecuted24h { get; set; }
        public int TakeProfitOrdersExecuted24h { get; set; }
        public int MslOrdersExecuted24h { get; set; }

        // ── Error & Failure ──────────────────────────────────────────────────
        public int InsufficientBalanceCancellations24h { get; set; }
        public int MinSizeCancellations24h { get; set; }
        public int WatchdogErrorCount24h { get; set; }
        public int PriceFetchFailures24h { get; set; }

        // ── Position Health ──────────────────────────────────────────────────
        public int TotalOpenPositions { get; set; }
        public int PositionsClosedToday { get; set; }
        public int PositionsLiquidatedToday { get; set; }
        public double AvgOpenROI { get; set; }
        public int NegativeROIPositions { get; set; }

        // ── Symbols ──────────────────────────────────────────────────────────
        public int UniqueSymbolsTracked { get; set; }

        // ── Per-User Top 10 ──────────────────────────────────────────────────
        public List<UserOrderStat> TopUsersByOpenOrders { get; set; } = new();
        public List<UserOrderStat> TopUsersByCancelledOrders { get; set; } = new();

        // ── Per-Symbol Top 10 ────────────────────────────────────────────────
        public List<SymbolOrderStat> TopSymbolsByOpenOrders { get; set; } = new();

        // ── Chart Data ───────────────────────────────────────────────────────
        /// <summary>Hours (0-23) label for executed orders chart</summary>
        public List<string> ExecutedByHourLabels { get; set; } = new();
        public List<int> ExecutedByHourValues { get; set; } = new();

        /// <summary>Status distribution for pie chart</summary>
        public List<string> StatusPieLabels { get; set; } = new();
        public List<int> StatusPieValues { get; set; } = new();

        /// <summary>Error count by hour (last 24 h)</summary>
        public List<string> ErrorByHourLabels { get; set; } = new();
        public List<int> ErrorByHourValues { get; set; } = new();

        /// <summary>ROI histogram buckets</summary>
        public List<string> RoiHistogramLabels { get; set; } = new();
        public List<int> RoiHistogramValues { get; set; } = new();

        // ── Open Positions Table ─────────────────────────────────────────────
        public List<OpenPositionRow> OpenPositions { get; set; } = new();
    }

    public class UserOrderStat
    {
        public string UserId { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public int Count { get; set; }
    }

    public class SymbolOrderStat
    {
        public string Symbol { get; set; } = string.Empty;
        public int Count { get; set; }
    }

    public class OpenPositionRow
    {
        public int Id { get; set; }
        public string UserId { get; set; } = string.Empty;
        public string Symbol { get; set; } = string.Empty;
        public string Side { get; set; } = string.Empty;
        public double ROI { get; set; }
        public double Entry { get; set; }
        public DateTime Time { get; set; }
        public bool IsTest { get; set; }
    }
}
