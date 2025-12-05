using AutoSignals.Models;

namespace AutoSignals.ViewModels
{
    public class VipDashboardViewModel
    {
        public string UserId { get; set; }
        public string UserName { get; set; }
        public List<Position> UserPositions { get; set; }
        public List<Order> AllOrders { get; set; }

        public int OpenPositionsCount { get; set; }
        public int ClosedPositionsCount { get; set; }
        public int TotalPositionCount { get; set; }
        public double OpenPositionsROI { get; set; }
        public double TotalPositionsROI { get; set; }
        public double ClosedPositionsROI { get; set; }


        public int TotalOrderCount { get; set; }
        public int OpenOrdersCount { get; set; }
        public int ClosedOrdersCount { get; set; }
        public int PendingOrderCount { get; set; }
        public int CancelledOrderCount { get; set; }

        public double TotalROI { get; set; }
        public double AverageROI { get; set; }
        public double HighestROI { get; set; }
        public double LowestROI { get; set; }
        public List<RoiOverTime> RoiOverTime { get; set; }

        public double WinRate { get; set; }
        public double LossRate { get; set; }
        public double LongWinRate { get; set; }
        public double LongLossRate { get; set; }
        public double ShortWinRate { get; set; }
        public double ShortLossRate { get; set; }

        public List<RoiBySymbol> ROIBySymbol { get; set; }

        public double TotalProfit { get; set; }
        public double TotalLoss { get; set; }
        public double NetPNL { get; set; }

        public string MostTradedSymbol { get; set; }
        public string BestPerformingSymbol { get; set; }
        public string WorstPerformingSymbol { get; set; }

        public string AverageTradeDuration { get; set; }

        public int HighestLeverage { get; set; }
        public double AverageLeverage { get; set; }
        public int LowestLeverage { get; set; }

        public double AverageTradeSize { get; set; }
        public double LargestTradeSize { get; set; }
        public double SmallestTradeSize { get; set; }

        public double TotalTradeVolume { get; set; }

        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
    }

    public class RoiBySymbol
    {
        public string Symbol { get; set; }
        public double AvgROI { get; set; }
        public int Count { get; set; }
    }

    public class RoiOverTime
    {
        public DateTime Date { get; set; }
        public double TotalROI { get; set; }
        public double AverageROI { get; set; }
        public double OpenROI { get; set; }
        public double ClosedROI { get; set; }
    }
}
