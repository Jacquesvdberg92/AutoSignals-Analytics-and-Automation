namespace AutoSignals.Models
{
    public class Provider
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public string? RRR { get; set; }
        public string? AverageProfitPerTrade { get; set; }
        public string? StoplossPersentage { get; set; }
        public int? SignalCount { get; set; }
        public string? AverageLeverage { get; set; }
        public string? TakeProfitTargets { get; set; }
        public string? SignalsNullified { get; set; }
        public string? TradeStyle { get; set; }
        public string? TradesPerDay { get; set; }
        public string? TradeTimeframes { get; set; }
        public string? AverageWinRate { get; set; }
        public string? LongWinRate { get; set; }
        public string? ShortWinRate { get; set; }
        public int? LongRatio { get; set; }
        public int? ShortRatio { get; set; }
        public int? LongCount { get; set; }
        public int? ShortCount { get; set; }
        public string? TpAchieved { get; set; }
        public string? Risk { get; set; }
        public string? TakeProfitDistribution { get; set; }
        public string? LastProvidedSignal { get; set; }
        public bool? IsActive { get; set; }

        public string? Telegram { get; set; }
        public byte[]? Picture { get; set; }
    }
}

