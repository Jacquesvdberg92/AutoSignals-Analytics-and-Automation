namespace AutoSignals.Services.ExchangeAdapters
{
    public sealed class ExchangeOrderSyncResult
    {
        public bool Success { get; set; }
        public string? ExternalOrderId { get; set; }
        public string? ClientOrderId { get; set; }
        public string? ExchangeStatus { get; set; }
        public string? NormalizedStatus { get; set; }
        public decimal? AveragePrice { get; set; }
        public decimal? FilledQuantity { get; set; }
        public string? ResponseJson { get; set; }
        public string? ErrorCode { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime SyncedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
