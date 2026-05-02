using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AutoSignals.Models.Bots
{
    /// <summary>
    /// Rolling log of arbitrage opportunities detected by an ArbitrageScannerBot.
    /// Kept to a maximum of 500 rows per scanner (older rows are pruned each tick).
    /// </summary>
    public class ArbitrageOpportunity
    {
        [Key]
        public int Id { get; set; }

        public int ScannerId { get; set; }

        [Required]
        public string Symbol { get; set; } = default!;

        [Required]
        public string BuyExchange { get; set; } = default!;

        [Required]
        public string SellExchange { get; set; } = default!;

        [Column(TypeName = "decimal(18,8)")]
        public decimal BuyPrice { get; set; }

        [Column(TypeName = "decimal(18,8)")]
        public decimal SellPrice { get; set; }

        [Column(TypeName = "decimal(18,8)")]
        public decimal SpreadPercent { get; set; }

        [Column(TypeName = "decimal(18,8)")]
        public decimal NetSpreadPercent { get; set; }

        public DateTime DetectedAt { get; set; } = DateTime.UtcNow;

        public bool Alerted { get; set; } = false;

        // ── Navigation ─────────────────────────────────────────────────────────────
        public ArbitrageScannerBot? Scanner { get; set; }
    }
}
