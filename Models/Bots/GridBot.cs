using System.ComponentModel.DataAnnotations.Schema;

namespace AutoSignals.Models.Bots
{
    public class GridBot : BotBase
    {
        // ── Configuration ──────────────────────────────────────────────────────────
        [Column(TypeName = "decimal(18,8)")]
        public decimal LowerPrice { get; set; }

        [Column(TypeName = "decimal(18,8)")]
        public decimal UpperPrice { get; set; }

        /// <summary>Number of grid intervals (min 2, max 100).</summary>
        public int GridCount { get; set; } = 10;

        /// <summary>USD size of each individual grid order.</summary>
        [Column(TypeName = "decimal(18,8)")]
        public decimal OrderSizeUsd { get; set; }

        public GridMode GridMode { get; set; } = GridMode.Arithmetic;

        public int Leverage { get; set; } = 1;

        public bool IsIsolated { get; set; } = true;

        /// <summary>If true, stop the bot when current price drops below LowerPrice.</summary>
        public bool StopOnLowerBreakout { get; set; } = false;

        /// <summary>If true, stop the bot when current price rises above UpperPrice.</summary>
        public bool StopOnUpperBreakout { get; set; } = false;

        // ── Runtime State ──────────────────────────────────────────────────────────
        [Column(TypeName = "decimal(18,8)")]
        public decimal TotalInvested { get; set; } = 0m;

        [Column(TypeName = "decimal(18,8)")]
        public decimal TotalProfit { get; set; } = 0m;

        public int FilledOrderCount { get; set; } = 0;

        /// <summary>True once the initial grid of limit orders has been placed.</summary>
        public bool GridInitialised { get; set; } = false;
    }
}
