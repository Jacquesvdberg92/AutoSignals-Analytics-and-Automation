using System.ComponentModel.DataAnnotations.Schema;

namespace AutoSignals.Models
{
    public class KuCoinMarket
    {
        public int Id { get; set; }
        public string Symbol { get; set; }
        public string BaseCoin { get; set; }
        public string QuoteCoin { get; set; }

        [Column(TypeName = "decimal(18, 8)")]
        public decimal MakerFeeRate { get; set; }

        [Column(TypeName = "decimal(18, 8)")]
        public decimal TakerFeeRate { get; set; }

        [Column(TypeName = "decimal(18, 8)")]
        public decimal MinTradeUSDT { get; set; }

        public int MinLever { get; set; }
        public int MaxLever { get; set; }

        [Column(TypeName = "decimal(18, 8)")]
        public decimal PricePrecision { get; set; }

        [Column(TypeName = "decimal(18, 8)")]
        public decimal AmountPrecision { get; set; }

        public DateTime Time
        {
            get; set;
        }
    }
}
