// Models/Portfolio.cs
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace AutoSignals.Models
{
    public class Portfolio
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string UserId { get; set; } = string.Empty;

        [Required]
        [StringLength(100)]
        public string Name { get; set; } = string.Empty;

        public bool IsDefault { get; set; } = false;
        public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

        // Navigation
        public virtual ICollection<PortfolioHolding> Holdings { get; set; } = new List<PortfolioHolding>();

        [NotMapped]
        public decimal TotalValue { get; set; }
    }

    public class PortfolioHolding
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int PortfolioId { get; set; }

        [Required]
        [StringLength(20)]
        public string AssetSymbol { get; set; } = string.Empty;

        [Precision(18, 8)]
        [Range(typeof(decimal), "0.00000001", "79228162514264337593543950335")]
        public decimal Quantity { get; set; }

        // If you want cents only, use (18,2). If you want more, increase scale.
        [Precision(18, 2)]
        [Range(0, double.MaxValue)]
        public decimal AverageBuyPrice { get; set; }

        public string? Notes { get; set; }

        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

        // Navigation
        public virtual Portfolio Portfolio { get; set; } = null!;

        // Computed properties (not stored in DB)
        [NotMapped]
        public decimal CurrentPrice { get; set; }

        [NotMapped]
        public decimal CurrentValue => Quantity * CurrentPrice;

        [NotMapped]
        public decimal CostBasis => Quantity * AverageBuyPrice;

        [NotMapped]
        public decimal PnL => CurrentValue - CostBasis;

        [NotMapped]
        public decimal PnLPercentage => CostBasis > 0 ? (PnL / CostBasis) * 100 : 0;

        [NotMapped]
        public decimal PortfolioPercentage { get; set; }
    }

    public class PortfolioHoldingSummary
    {
        public string AssetSymbol { get; set; } = string.Empty;

        public decimal Quantity { get; set; }

        // Weighted average buy price = sum(qty * buyPrice) / sum(qty)
        public decimal AverageBuyPrice { get; set; }

        public decimal CurrentPrice { get; set; }

        public decimal CurrentValue => Quantity * CurrentPrice;

        public decimal CostBasis => Quantity * AverageBuyPrice;

        public decimal PnL => CurrentValue - CostBasis;

        public decimal PnLPercentage => CostBasis > 0 ? (PnL / CostBasis) * 100 : 0;

        public decimal PortfolioPercentage { get; set; }
    }
}