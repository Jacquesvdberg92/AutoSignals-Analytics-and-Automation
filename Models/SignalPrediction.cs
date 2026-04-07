namespace AutoSignals.Models
{
    public class SignalPrediction
    {
        public int Id { get; set; }
        public int SignalId { get; set; }
        public float ConfidenceScore { get; set; }
        public string TpProbabilities { get; set; } = string.Empty; // Comma-separated, one entry per TP in the signal
        public float StoplossProbability { get; set; }
        public float ProviderAccuracyScore { get; set; }
        public float MarketAlignmentScore { get; set; }
        public float VolatilityFitScore { get; set; }
        public int HistoricalSampleSize { get; set; }
        public int ProviderSampleSize { get; set; }
        public string? FeatureSummary { get; set; }
        public string? NarrativeAnalysis { get; set; }
        public string ModelVersion { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
