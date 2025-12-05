namespace AutoSignals.Models
{
    public class SignalPerformance
    {
        public int Id { get; set; }
        public string? Status { get; set; } // Nullable string
        public int SignalId { get; set; } // Foreign key to Signal
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; } // Nullable DateTime
        public float HighPrice { get; set; }
        public float LowPrice { get; set; }
        public float? ProfitLoss { get; set; } // Nullable float
        public int TakeProfitCount { get; set; }
        public int? TakeProfitsAchieved { get; set; } // Nullable int
        public string? AchievedTakeProfits { get; set; } // Nullable string
        public string? Notes { get; set; } // Nullable string
        public string? NotifiedTakeProfits { get; set; }
        public string? TelegramMessageId { get; set; } // Nullable string
    }
}

