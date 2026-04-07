namespace AutoSignals.Models
{
    public class CandleDto
    {
        public long Time { get; set; }      // Unix timestamp in seconds (UTC)
        public decimal Open { get; set; }
        public decimal High { get; set; }
        public decimal Low { get; set; }
        public decimal Close { get; set; }
        public decimal Volume { get; set; }
    }
}
