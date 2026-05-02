namespace AutoSignals.Services.ExchangeAdapters
{
    public sealed class OpenOrderResult
    {
        public string ExternalOrderId { get; set; } = default!;
        public string Symbol           { get; set; } = default!;
        public string Side             { get; set; } = default!;  // "Buy" | "Sell"
        public string Type             { get; set; } = default!;  // "Limit" | "Market"
        public decimal Price           { get; set; }
        public decimal Qty             { get; set; }
        public decimal FilledQty       { get; set; }
        public string Status           { get; set; } = default!;
        public DateTime CreatedAt      { get; set; }
    }
}
