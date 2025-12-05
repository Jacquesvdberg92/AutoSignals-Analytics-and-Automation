using System.ComponentModel.DataAnnotations;

namespace AutoSignals.Models
{
    public class Position
    {
        public int Id { get; set; }
        public string UserId { get; set; }
        public string ExchangeId { get; set; }
        public string TelegramId { get; set; }
        public string Side { get; set; }
        public string Size { get; set; }
        public int Leverage { get; set; }
        public string Symbol { get; set; }
        public double Entry { get; set; }
        public double Stoploss { get; set; }
        public double ROI { get; set; }
        public bool IsIsolated { get; set; }
        public double EstLiquidation { get; set; }
        public string Status { get; set; }
        public bool IsTest { get; set; }
        public DateTime Time { get; set; }
        public DateTime? CloseTime { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }

        // New fields for more accurate reporting
        //public double? ClosePrice { get; set; }      // Price at which the position was closed
        //public double? RealizedPnl { get; set; }     // Realized profit or loss in quote currency
        //public double? Fees { get; set; }            // (Optional) Trading fees for this position
        //public string? CloseReason { get; set; }     // (Optional) Reason for closing the position
    }
}