using System.ComponentModel.DataAnnotations;

namespace AutoSignals.Models
{
    public class Order
    {
        public int Id { get; set; }
        public int SignalId { get; set; }
        public string UserId { get; set; }
        public string ExchangeId { get; set; }
        public string? TelegramId { get; set; }
        public string? PositionId { get; set; }
        public string? UserName { get; set; }
        public string Symbol { get; set; }
        public string Side { get; set; }
        public double? Price { get; set; }
        public double? Stoploss { get; set; }
        public double Size { get; set; }
        public double Leverage { get; set; }
        public bool IsIsolated { get; set; }
        public bool IsTest { get; set; }
        public string Status { get; set; }
        public string Description { get; set; }
        public DateTime Time { get; set; }
        public DateTime? CloseTime { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }

        public Order()
        {
            IsIsolated = true;
            IsTest = false;
            Time = DateTime.UtcNow;
        }
    }
}

