using System.ComponentModel.DataAnnotations.Schema;

namespace AutoSignals.Models
{
    /// <summary>
    /// One price snapshot per symbol per 5-minute tick, used to aggregate OHLCV candles.
    /// Written by AveragePriceService after every GeneralAssetPrices MERGE.
    /// </summary>
    public class KLineAssetPrice
    {
        public int Id { get; set; }
        public string Symbol { get; set; }

        /// <summary>"spot" or "swap"</summary>
        public string Type { get; set; }

        [Column(TypeName = "decimal(18, 8)")]
        public decimal Price { get; set; }

        [Column(TypeName = "decimal(18, 8)")]
        public decimal Open { get; set; }

        [Column(TypeName = "decimal(18, 8)")]
        public decimal High { get; set; }

        [Column(TypeName = "decimal(18, 8)")]
        public decimal Low { get; set; }

        [Column(TypeName = "decimal(18, 8)")]
        public decimal Close { get; set; }

        [Column(TypeName = "decimal(28, 8)")]
        public decimal Volume { get; set; }

        public DateTime Time { get; set; }
    }
}
