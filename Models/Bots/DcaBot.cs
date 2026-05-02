using System.ComponentModel.DataAnnotations.Schema;

namespace AutoSignals.Models.Bots
{
    public class DcaBot : BotBase
    {
        // ── Configuration ──────────────────────────────────────────────────────────
        [Column(TypeName = "decimal(18,8)")]
        public decimal BaseOrderSizeUsd { get; set; }

        [Column(TypeName = "decimal(18,8)")]
        public decimal SafetyOrderSizeUsd { get; set; }

        public int MaxSafetyOrders { get; set; } = 3;

        [Column(TypeName = "decimal(18,8)")]
        public decimal SafetyOrderPriceDeviation { get; set; } = 2.0m;

        /// <summary>Multiplier applied to each successive safety order size (e.g. 1.5 = 50% bigger each step).</summary>
        [Column(TypeName = "decimal(18,8)")]
        public decimal SafetyOrderVolumeScale { get; set; } = 1.0m;

        /// <summary>Multiplier applied to each successive price deviation step.</summary>
        [Column(TypeName = "decimal(18,8)")]
        public decimal SafetyOrderStepScale { get; set; } = 1.0m;

        [Column(TypeName = "decimal(18,8)")]
        public decimal TakeProfitPercent { get; set; } = 3.0m;

        [Column(TypeName = "decimal(18,8)")]
        public decimal? StoplossPercent { get; set; }

        public int Leverage { get; set; } = 1;

        public bool IsIsolated { get; set; } = true;

        public int CooldownMinutes { get; set; } = 0;

        public bool AutoRestart { get; set; } = false;

        // ── Runtime State ──────────────────────────────────────────────────────────
        public int CurrentSafetyOrderCount { get; set; } = 0;

        [Column(TypeName = "decimal(18,8)")]
        public decimal? AverageEntryPrice { get; set; }

        [Column(TypeName = "decimal(18,8)")]
        public decimal TotalInvested { get; set; } = 0m;

        public DateTime? CooldownUntil { get; set; }
    }
}
