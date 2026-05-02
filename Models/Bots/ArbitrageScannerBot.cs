using System.ComponentModel.DataAnnotations.Schema;

namespace AutoSignals.Models.Bots
{
    /// <summary>
    /// Arbitrage scanner bot. Reads per-exchange price tables, detects spread opportunities,
    /// and fires Telegram alerts. Phase 1 is read-only (no exchange writes).
    /// Symbol is set to "MULTI" on BotBase — watched symbols stored in WatchedSymbolsJson.
    /// </summary>
    public class ArbitrageScannerBot : BotBase
    {
        // ── Configuration ──────────────────────────────────────────────────────────

        /// <summary>JSON array of symbols to watch, e.g. ["BTCUSDT","ETHUSDT"].</summary>
        public string WatchedSymbolsJson { get; set; } = "[]";

        [Column(TypeName = "decimal(18,8)")]
        public decimal MinSpreadPercent { get; set; } = 0.5m;

        public int AlertCooldownMinutes { get; set; } = 5;

        /// <summary>Phase 2 only — always false in Phase 1.</summary>
        public bool AutoExecute { get; set; } = false;

        [Column(TypeName = "decimal(18,8)")]
        public decimal? MaxPositionSizeUsd { get; set; }

        [Column(TypeName = "decimal(18,8)")]
        public decimal EstimatedFeePercent { get; set; } = 0.1m;

        // ── Runtime State ──────────────────────────────────────────────────────────

        /// <summary>Total number of opportunities detected since the bot started.</summary>
        public int TotalOpportunitiesFound { get; set; } = 0;

        /// <summary>Last time an alert was sent (used for per-bot cooldown).</summary>
        public DateTime? LastAlertAt { get; set; }

        // ── Navigation ─────────────────────────────────────────────────────────────
        public ICollection<ArbitrageOpportunity>? Opportunities { get; set; }
    }
}
