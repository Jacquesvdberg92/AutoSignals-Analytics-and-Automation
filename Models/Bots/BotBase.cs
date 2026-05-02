using System.ComponentModel.DataAnnotations;

namespace AutoSignals.Models.Bots
{
    public abstract class BotBase
    {
        public int Id { get; set; }

        public string UserId { get; set; } = default!;

        public int ExchangeConnectionId { get; set; }
        public UserExchangeConnection? ExchangeConnection { get; set; }

        public BotType BotType { get; set; }

        public BotStatus Status { get; set; } = BotStatus.Idle;

        public string? Label { get; set; }

        public string Symbol { get; set; } = default!;

        public bool IsTest { get; set; } = false;

        public string? Notes { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? LastRunAt { get; set; }

        public string? ErrorMessage { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; } = default!;
    }
}
