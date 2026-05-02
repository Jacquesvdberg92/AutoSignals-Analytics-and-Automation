using AutoSignals.Data;
using AutoSignals.Models;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace AutoSignals.Services
{
    public class SignalPredictionService
    {
        private const string ModelVersion = "baseline-v2";
        private const float NeutralProbability = 0.5f;
        private const int MaxHistoricalSamples = 600;
        private const int MaxMarketCandles = 48;

        private readonly AutoSignalsDbContext _context;
        private readonly ILogger<SignalPredictionService> _logger;
        private readonly RegexGeneratorService _aiService;

        public SignalPredictionService(
            AutoSignalsDbContext context,
            ILogger<SignalPredictionService> logger,
            RegexGeneratorService aiService)
        {
            _context = context;
            _logger = logger;
            _aiService = aiService;
        }

        public async Task<SignalPrediction?> GeneratePredictionAsync(Signal signal, CancellationToken cancellationToken = default)
        {
            try
            {
                var existingPrediction = await _context.SignalPredictions
                    .AsNoTracking()
                    .FirstOrDefaultAsync(p => p.SignalId == signal.Id, cancellationToken);

                if (existingPrediction != null && !string.IsNullOrWhiteSpace(existingPrediction.TpProbabilities))
                {
                    return existingPrediction;
                }

                // Stale prediction (pre-dates dynamic TP support) — delete and regenerate
                if (existingPrediction != null)
                {
                    var tracked = await _context.SignalPredictions.FindAsync(new object[] { existingPrediction.Id }, cancellationToken);
                    if (tracked != null)
                        _context.SignalPredictions.Remove(tracked);
                }

                var prediction = await BuildPredictionAsync(signal, cancellationToken);
                _context.SignalPredictions.Add(prediction);
                await _context.SaveChangesAsync(cancellationToken);

                return prediction;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate prediction for signal {SignalId}.", signal.Id);
                return null;
            }
        }

        private async Task<SignalPrediction> BuildPredictionAsync(Signal signal, CancellationToken cancellationToken)
        {
            var signalTime = signal.Time == default ? DateTime.UtcNow : signal.Time;
            var historicalSignals = await (
                from historicalSignal in _context.Signals.AsNoTracking()
                join performance in _context.SignalPerformances.AsNoTracking() on historicalSignal.Id equals performance.SignalId
                where historicalSignal.Id != signal.Id && historicalSignal.Time < signalTime
                orderby historicalSignal.Time descending
                select new HistoricalSignalSnapshot
                {
                    Symbol = historicalSignal.Symbol,
                    Side = historicalSignal.Side,
                    Provider = historicalSignal.Provider,
                    Time = historicalSignal.Time,
                    Status = performance.Status,
                    TakeProfitsAchieved = performance.TakeProfitsAchieved ?? 0,
                    ProfitLoss = performance.ProfitLoss
                })
                .Take(MaxHistoricalSamples)
                .ToListAsync(cancellationToken);

            var resolvedHistory = historicalSignals
                .Where(sample => IsResolvedStatus(sample.Status))
                .ToList();

            var provider = await _context.Provider
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Name == signal.Provider, cancellationToken);

            var takeProfitLevels = ParseTakeProfitLevels(signal.TakeProfits);
            var tpCount = Math.Max(1, Math.Min(takeProfitLevels.Count, 10));

            var providerBaseRate = GetProviderBaseRate(provider, signal.Side);

            // Global hit rates for each TP level (1-indexed)
            var globalRates = new float[tpCount + 1];
            globalRates[1] = CalculateHitRate(resolvedHistory, 1, providerBaseRate, 12);
            for (var i = 2; i <= tpCount; i++)
                globalRates[i] = CalculateHitRate(resolvedHistory, i, Math.Clamp(globalRates[i - 1] * 0.75f, 0.03f, 0.90f), 12);

            var providerHistory = resolvedHistory
                .Where(sample => string.Equals(sample.Provider, signal.Provider, StringComparison.OrdinalIgnoreCase))
                .Take(120)
                .ToList();

            var symbolHistory = resolvedHistory
                .Where(sample => string.Equals(sample.Symbol, signal.Symbol, StringComparison.OrdinalIgnoreCase))
                .Take(120)
                .ToList();

            var sideHistory = resolvedHistory
                .Where(sample => string.Equals(sample.Side, signal.Side, StringComparison.OrdinalIgnoreCase))
                .Take(180)
                .ToList();

            // Provider and symbol hit rates for each TP level (1-indexed)
            var providerRates = new float[tpCount + 1];
            providerRates[1] = CalculateHitRate(providerHistory, 1, providerBaseRate, 10);
            for (var i = 2; i <= tpCount; i++)
                providerRates[i] = CalculateHitRate(providerHistory, i, Math.Clamp(providerRates[i - 1] * 0.72f, 0.03f, 0.90f), 10);

            var symbolRates = new float[tpCount + 1];
            symbolRates[1] = CalculateHitRate(symbolHistory, 1, globalRates[1], 10);
            for (var i = 2; i <= tpCount; i++)
                symbolRates[i] = CalculateHitRate(symbolHistory, i, globalRates[i], 10);

            var sideTp1Rate = CalculateHitRate(sideHistory, 1, globalRates[1], 8);

            var riskRewardScore = CalculateRiskRewardScore(signal, takeProfitLevels);
            var leverageScore = CalculateLeverageScore(signal.Leverage);

            var marketCandles = await _context.KLineAssetPrices
                .AsNoTracking()
                .Where(candle => candle.Symbol == signal.Symbol && candle.Time <= signalTime)
                .OrderByDescending(candle => candle.Time)
                .Take(MaxMarketCandles)
                .ToListAsync(cancellationToken);

            var (marketAlignmentScore, volatilityFitScore) = CalculateMarketScores(marketCandles, signal, takeProfitLevels);

            // TP1 uses all available signals; each subsequent TP is capped at the previous TP probability
            var tpProbabilities = new float[tpCount];
            tpProbabilities[0] = WeightedAverage(
                (providerRates[1], 0.30f),
                (symbolRates[1], 0.15f),
                (sideTp1Rate, 0.10f),
                (riskRewardScore, 0.15f),
                (leverageScore, 0.10f),
                (marketAlignmentScore, 0.10f),
                (volatilityFitScore, 0.10f));

            for (var i = 1; i < tpCount; i++)
            {
                tpProbabilities[i] = Math.Min(tpProbabilities[i - 1], WeightedAverage(
                    (providerRates[i + 1], 0.35f),
                    (symbolRates[i + 1], 0.20f),
                    (tpProbabilities[i - 1], 0.20f),
                    (riskRewardScore, 0.15f),
                    (marketAlignmentScore, 0.10f)));
            }

            var stoplossProbability = Math.Clamp(1f - WeightedAverage(
                (tpProbabilities[0], 0.50f),
                (riskRewardScore, 0.15f),
                (marketAlignmentScore, 0.15f),
                (volatilityFitScore, 0.10f),
                (leverageScore, 0.10f)), 0.05f, 0.95f);

            var confidenceScore = WeightedAverage(
                (tpProbabilities[0], 0.40f),
                (tpCount > 1 ? tpProbabilities[1] : tpProbabilities[0], 0.20f),
                (providerRates[1], 0.20f),
                (marketAlignmentScore, 0.10f),
                (volatilityFitScore, 0.10f));

            var prediction = new SignalPrediction
            {
                SignalId = signal.Id,
                ConfidenceScore = ToPercentage(confidenceScore),
                TpProbabilities = string.Join(",", tpProbabilities.Select(p => ToPercentage(p).ToString(CultureInfo.InvariantCulture))),
                StoplossProbability = ToPercentage(stoplossProbability),
                ProviderAccuracyScore = ToPercentage(providerRates[1]),
                MarketAlignmentScore = ToPercentage(marketAlignmentScore),
                VolatilityFitScore = ToPercentage(volatilityFitScore),
                HistoricalSampleSize = resolvedHistory.Count,
                ProviderSampleSize = providerHistory.Count,
                FeatureSummary = BuildFeatureSummary(signal, providerRates[1], symbolRates[1], riskRewardScore, marketAlignmentScore, volatilityFitScore, providerHistory.Count, resolvedHistory.Count),
                ModelVersion = ModelVersion,
                CreatedAt = DateTime.UtcNow
            };

            prediction.NarrativeAnalysis = await _aiService.GenerateSignalNarrativeAsync(
                signal, prediction, cancellationToken);

            return prediction;
        }

        private static float CalculateHitRate(List<HistoricalSignalSnapshot> samples, int targetNumber, float priorRate, int priorStrength)
        {
            if (samples.Count == 0)
            {
                return priorRate;
            }

            var hits = samples.Count(sample => sample.TakeProfitsAchieved >= targetNumber);
            return Math.Clamp((hits + (priorRate * priorStrength)) / (samples.Count + priorStrength), 0.01f, 0.99f);
        }

        private static bool IsResolvedStatus(string? status)
        {
            return string.Equals(status, "Closed", StringComparison.OrdinalIgnoreCase)
                || string.Equals(status, "Canceled", StringComparison.OrdinalIgnoreCase);
        }

        private static float GetProviderBaseRate(Provider? provider, string? side)
        {
            var sideRate = string.Equals(side, "long", StringComparison.OrdinalIgnoreCase)
                ? ParseRate(provider?.LongWinRate)
                : ParseRate(provider?.ShortWinRate);

            return sideRate
                ?? ParseRate(provider?.AverageWinRate)
                ?? NeutralProbability;
        }

        private static float? ParseRate(string? rawValue)
        {
            if (string.IsNullOrWhiteSpace(rawValue))
            {
                return null;
            }

            var normalized = rawValue.Replace("%", string.Empty).Trim();
            if (!float.TryParse(normalized, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedValue)
                && !float.TryParse(normalized, NumberStyles.Any, CultureInfo.CurrentCulture, out parsedValue))
            {
                return null;
            }

            if (parsedValue > 1f)
            {
                parsedValue /= 100f;
            }

            return Math.Clamp(parsedValue, 0f, 1f);
        }

        private static List<decimal> ParseTakeProfitLevels(string takeProfits)
        {
            return takeProfits
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(value => decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedValue)
                    ? parsedValue
                    : (decimal?)null)
                .Where(value => value.HasValue)
                .Select(value => value!.Value)
                .ToList();
        }

        private static float CalculateRiskRewardScore(Signal signal, List<decimal> takeProfitLevels)
        {
            var entry = (decimal)signal.Entry;
            var stoploss = (decimal)signal.Stoploss;
            var firstTarget = takeProfitLevels.FirstOrDefault();

            if (entry <= 0 || stoploss <= 0 || firstTarget <= 0)
            {
                return NeutralProbability;
            }

            var riskDistance = Math.Abs(entry - stoploss) / entry;
            if (riskDistance <= 0)
            {
                return NeutralProbability;
            }

            var rewardDistance = Math.Abs(firstTarget - entry) / entry;
            var riskRewardRatio = rewardDistance / riskDistance;
            return Math.Clamp((float)(riskRewardRatio / 3m), 0.10f, 0.95f);
        }

        private static float CalculateLeverageScore(int leverage)
        {
            if (leverage <= 0)
            {
                return NeutralProbability;
            }

            if (leverage <= 5)
            {
                return 0.90f;
            }

            if (leverage <= 10)
            {
                return 0.80f;
            }

            if (leverage <= 20)
            {
                return 0.65f;
            }

            if (leverage <= 30)
            {
                return 0.50f;
            }

            return 0.35f;
        }

        private static (float MarketAlignmentScore, float VolatilityFitScore) CalculateMarketScores(List<KLineAssetPrice> candles, Signal signal, List<decimal> takeProfitLevels)
        {
            if (candles.Count < 2)
            {
                return (NeutralProbability, NeutralProbability);
            }

            var orderedCandles = candles
                .OrderBy(candle => candle.Time)
                .ToList();

            var closes = orderedCandles
                .Select(candle => candle.Close)
                .Where(close => close > 0)
                .ToList();

            if (closes.Count < 2)
            {
                return (NeutralProbability, NeutralProbability);
            }

            var fastWindow = closes.TakeLast(Math.Min(5, closes.Count)).ToList();
            var slowAverage = closes.Average();
            var fastAverage = fastWindow.Average();
            var firstClose = closes.First();
            var lastClose = closes.Last();

            var movingAverageMomentum = slowAverage == 0 ? 0m : (fastAverage - slowAverage) / slowAverage;
            var candleMomentum = firstClose == 0 ? 0m : (lastClose - firstClose) / firstClose;
            var combinedMomentum = (movingAverageMomentum + candleMomentum) / 2m;

            var marketAlignmentScore = string.Equals(signal.Side, "long", StringComparison.OrdinalIgnoreCase)
                ? 0.5f + (float)(combinedMomentum * 40m)
                : 0.5f - (float)(combinedMomentum * 40m);

            var averageRange = orderedCandles.Average(candle =>
                candle.Close == 0
                    ? 0m
                    : Math.Abs((candle.High - candle.Low) / candle.Close));

            var entry = (decimal)signal.Entry;
            var stoploss = (decimal)signal.Stoploss;
            var firstTarget = takeProfitLevels.FirstOrDefault();
            var stopDistance = entry <= 0 ? 0m : Math.Abs(entry - stoploss) / entry;
            var targetDistance = entry <= 0 || firstTarget <= 0 ? 0m : Math.Abs(firstTarget - entry) / entry;
            var normalizedAverageRange = averageRange <= 0 ? 0.005m : averageRange;
            var stopCoverage = stopDistance / normalizedAverageRange;
            var targetCoverage = targetDistance / normalizedAverageRange;
            var volatilityFitScore = Math.Clamp((float)(((stopCoverage * 0.60m) + (targetCoverage * 0.40m)) / 4m), 0.10f, 0.95f);

            return (Math.Clamp(marketAlignmentScore, 0.10f, 0.95f), volatilityFitScore);
        }

        private static float WeightedAverage(params (float Value, float Weight)[] values)
        {
            if (values.Length == 0)
            {
                return NeutralProbability;
            }

            var totalWeight = values.Sum(value => value.Weight);
            if (totalWeight <= 0)
            {
                return NeutralProbability;
            }

            var weightedSum = values.Sum(value => value.Value * value.Weight);
            return Math.Clamp(weightedSum / totalWeight, 0.01f, 0.99f);
        }

        private static float ToPercentage(float value)
        {
            return MathF.Round(value * 100f, 2);
        }

        private static string BuildFeatureSummary(
            Signal signal,
            float providerTp1Rate,
            float symbolTp1Rate,
            float riskRewardScore,
            float marketAlignmentScore,
            float volatilityFitScore,
            int providerSamples,
            int historicalSamples)
        {
            return string.Join(" | ",
                $"Provider TP1 rate {ToPercentage(providerTp1Rate)}% ({providerSamples} samples)",
                $"Symbol TP1 rate {ToPercentage(symbolTp1Rate)}%",
                $"R/R score {ToPercentage(riskRewardScore)}%",
                $"Market alignment {ToPercentage(marketAlignmentScore)}%",
                $"Volatility fit {ToPercentage(volatilityFitScore)}%",
                $"Side {signal.Side}",
                $"History {historicalSamples} signals");
        }

        private sealed class HistoricalSignalSnapshot
        {
            public string Symbol { get; init; } = string.Empty;
            public string Side { get; init; } = string.Empty;
            public string Provider { get; init; } = string.Empty;
            public DateTime Time { get; init; }
            public string? Status { get; init; }
            public int TakeProfitsAchieved { get; init; }
            public float? ProfitLoss { get; init; }
        }
    }
}
