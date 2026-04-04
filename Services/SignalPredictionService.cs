using AutoSignals.Data;
using AutoSignals.Models;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace AutoSignals.Services
{
    public class SignalPredictionService
    {
        private const string ModelVersion = "baseline-v1";
        private const float NeutralProbability = 0.5f;
        private const int MaxHistoricalSamples = 600;
        private const int MaxMarketCandles = 48;

        private readonly AutoSignalsDbContext _context;
        private readonly ILogger<SignalPredictionService> _logger;

        public SignalPredictionService(AutoSignalsDbContext context, ILogger<SignalPredictionService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<SignalPrediction?> GeneratePredictionAsync(Signal signal, CancellationToken cancellationToken = default)
        {
            try
            {
                var existingPrediction = await _context.SignalPredictions
                    .AsNoTracking()
                    .FirstOrDefaultAsync(p => p.SignalId == signal.Id, cancellationToken);

                if (existingPrediction != null)
                {
                    return existingPrediction;
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

            var providerBaseRate = GetProviderBaseRate(provider, signal.Side);
            var globalTp1Rate = CalculateHitRate(resolvedHistory, 1, providerBaseRate, 12);
            var globalTp2Rate = CalculateHitRate(resolvedHistory, 2, Math.Clamp(globalTp1Rate * 0.75f, 0.05f, 0.95f), 12);
            var globalTp3Rate = CalculateHitRate(resolvedHistory, 3, Math.Clamp(globalTp2Rate * 0.75f, 0.03f, 0.90f), 12);

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

            var providerTp1Rate = CalculateHitRate(providerHistory, 1, providerBaseRate, 10);
            var providerTp2Rate = CalculateHitRate(providerHistory, 2, Math.Clamp(providerTp1Rate * 0.72f, 0.05f, 0.95f), 10);
            var providerTp3Rate = CalculateHitRate(providerHistory, 3, Math.Clamp(providerTp2Rate * 0.72f, 0.03f, 0.90f), 10);
            var symbolTp1Rate = CalculateHitRate(symbolHistory, 1, globalTp1Rate, 10);
            var symbolTp2Rate = CalculateHitRate(symbolHistory, 2, globalTp2Rate, 10);
            var sideTp1Rate = CalculateHitRate(sideHistory, 1, globalTp1Rate, 8);

            var takeProfitLevels = ParseTakeProfitLevels(signal.TakeProfits);
            var riskRewardScore = CalculateRiskRewardScore(signal, takeProfitLevels);
            var leverageScore = CalculateLeverageScore(signal.Leverage);

            var marketCandles = await _context.KLineAssetPrices
                .AsNoTracking()
                .Where(candle => candle.Symbol == signal.Symbol && candle.Time <= signalTime)
                .OrderByDescending(candle => candle.Time)
                .Take(MaxMarketCandles)
                .ToListAsync(cancellationToken);

            var (marketAlignmentScore, volatilityFitScore) = CalculateMarketScores(marketCandles, signal, takeProfitLevels);

            var tp1Probability = WeightedAverage(
                (providerTp1Rate, 0.30f),
                (symbolTp1Rate, 0.15f),
                (sideTp1Rate, 0.10f),
                (riskRewardScore, 0.15f),
                (leverageScore, 0.10f),
                (marketAlignmentScore, 0.10f),
                (volatilityFitScore, 0.10f));

            var tp2Probability = Math.Min(tp1Probability, WeightedAverage(
                (providerTp2Rate, 0.35f),
                (symbolTp2Rate, 0.20f),
                (tp1Probability, 0.20f),
                (riskRewardScore, 0.15f),
                (marketAlignmentScore, 0.10f)));

            var tp3Probability = Math.Min(tp2Probability, WeightedAverage(
                (providerTp3Rate, 0.35f),
                (globalTp3Rate, 0.20f),
                (tp2Probability, 0.20f),
                (riskRewardScore, 0.15f),
                (marketAlignmentScore, 0.10f)));

            var stoplossProbability = Math.Clamp(1f - WeightedAverage(
                (tp1Probability, 0.50f),
                (riskRewardScore, 0.15f),
                (marketAlignmentScore, 0.15f),
                (volatilityFitScore, 0.10f),
                (leverageScore, 0.10f)), 0.05f, 0.95f);

            var confidenceScore = WeightedAverage(
                (tp1Probability, 0.40f),
                (tp2Probability, 0.20f),
                (providerTp1Rate, 0.20f),
                (marketAlignmentScore, 0.10f),
                (volatilityFitScore, 0.10f));

            return new SignalPrediction
            {
                SignalId = signal.Id,
                ConfidenceScore = ToPercentage(confidenceScore),
                Tp1Probability = ToPercentage(tp1Probability),
                Tp2Probability = ToPercentage(tp2Probability),
                Tp3Probability = ToPercentage(tp3Probability),
                StoplossProbability = ToPercentage(stoplossProbability),
                ProviderAccuracyScore = ToPercentage(providerTp1Rate),
                MarketAlignmentScore = ToPercentage(marketAlignmentScore),
                VolatilityFitScore = ToPercentage(volatilityFitScore),
                HistoricalSampleSize = resolvedHistory.Count,
                ProviderSampleSize = providerHistory.Count,
                FeatureSummary = BuildFeatureSummary(signal, providerTp1Rate, symbolTp1Rate, riskRewardScore, marketAlignmentScore, volatilityFitScore, providerHistory.Count, resolvedHistory.Count),
                ModelVersion = ModelVersion,
                CreatedAt = DateTime.UtcNow
            };
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
