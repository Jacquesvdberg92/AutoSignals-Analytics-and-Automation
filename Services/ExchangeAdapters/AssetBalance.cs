namespace AutoSignals.Services.ExchangeAdapters
{
    public sealed class AssetBalance
    {
        public string Asset      { get; set; } = default!;  // e.g. "BTC", "USDT"
        public decimal Available { get; set; }
        public decimal Locked    { get; set; }
        public decimal Total => Available + Locked;
    }
}
