using System.ComponentModel.DataAnnotations.Schema;

namespace AutoSignals.Models
{
    /// <summary>
    /// Represents the price for an asset on the Bitget exchange.
    /// </summary>
    public class OkxAssetPrice
    {
        public int Id { get; set; }
        public string Symbol { get; set; }

        [Column(TypeName = "decimal(18, 8)")]
        public decimal Price { get; set; } //Last Price

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
