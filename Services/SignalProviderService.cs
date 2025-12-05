using AutoSignals.Data;
using AutoSignals.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace AutoSignals.Services
{
    public class SignalProviderService
    {
        private readonly ILogger<SignalProviderService> _logger;
        private readonly AutoSignalsDbContext _context;
        private readonly ErrorLogService _errorLogService;

        public SignalProviderService(
            ILogger<SignalProviderService> logger,
            AutoSignalsDbContext context,
            ErrorLogService errorLogService)
        {
            _logger = logger;
            _context = context;
            _errorLogService = errorLogService;
        }

        // Move all calculation methods here
        // Function to calculate ratios
        public (double shortRatio, double longRatio) CalculateRatios(int shortCount, int longCount)
        {
            int total = shortCount + longCount;
            if (total == 0) return (0, 0);
            double shortRatio = Math.Round((double)shortCount / total * 100, 0);
            double longRatio = Math.Round((double)longCount / total * 100, 0);
            return (shortRatio, longRatio);
        }

        // Function that calculates take profit distribution
        public List<int> CalculateTakeProfitDistrobution(List<SignalPerformance> signalPerformances)
        {
            if (signalPerformances == null || signalPerformances.Count == 0)
            {
                return new List<int> { 0 };
            }

            Dictionary<int, int> takeProfitCounts = new Dictionary<int, int>();

            foreach (var performance in signalPerformances)
            {
                if (performance.TakeProfitsAchieved.HasValue)
                {
                    int achievedTP = performance.TakeProfitsAchieved.Value;

                    for (int i = 1; i <= achievedTP; i++)
                    {
                        if (takeProfitCounts.ContainsKey(i))
                        {
                            takeProfitCounts[i]++;
                        }
                        else
                        {
                            takeProfitCounts[i] = 1;
                        }
                    }
                }
            }

            int totalSignals = signalPerformances.Count;

            return takeProfitCounts.OrderBy(kvp => kvp.Key)
                                   .Select(kvp => (int)((kvp.Value / (double)totalSignals) * 100))
                                   .ToList();
        }

        // Function to calc RRR
        public (double overallRRR, string rrrCategory, List<(int SignalId, float TakeProfit, float RRR)>) CalculateRRR(List<Signal> providerSignals)
        {
            var rrrResults = new List<(int SignalId, float TakeProfit, float RRR)>();
            float totalReward = 0;
            float totalRisk = 0;

            foreach (var signal in providerSignals)
            {
                var takeProfits = signal.TakeProfits.Split(',')
                    .Select(t => {
                        float.TryParse(t.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float result);
                        return result;
                    })
                    .ToList();

                foreach (var takeProfit in takeProfits)
                {
                    float risk, reward, rrr;

                    if (signal.Side.ToLower() == "long")
                    {
                        reward = takeProfit - signal.Entry;
                        risk = signal.Entry - signal.Stoploss;
                    }
                    else
                    {
                        reward = signal.Entry - takeProfit;
                        risk = signal.Stoploss - signal.Entry;
                    }

                    if (risk != 0)
                    {
                        rrr = reward / risk;
                        rrrResults.Add((signal.Id, takeProfit, rrr));
                        totalReward += reward;
                        totalRisk += risk;
                    }
                }
            }

            double overallRRR = totalRisk != 0 ? totalReward / totalRisk : 0;
            overallRRR = Math.Round(overallRRR, 2);

            string rrrCategory;
            if (overallRRR > 1)
            {
                rrrCategory = "Good";
            }
            else if (overallRRR >= 0)
            {
                rrrCategory = "Neutral";
            }
            else
            {
                rrrCategory = "Bad";
            }

            return (overallRRR, rrrCategory, rrrResults);
        }

        // Function to calculate signal frequency and average trades per day
        public (Dictionary<DateTime, int> Frequency, double AverageTradesPerDay) CalculateSignalFrequency(List<Signal> signals)
        {
            var groupedByDay = signals
                .GroupBy(s => s.Time.Date)
                .ToDictionary(g => g.Key, g => g.Count());

            double averageTradesPerDay = groupedByDay.Count > 0 ? Math.Round((double)signals.Count / groupedByDay.Count, 2) : 0;

            return (groupedByDay, averageTradesPerDay);
        }

        // Function to calculate the average stoploss percentage
        public double CalculateAverageStoplossPercentage(List<Signal> signals)
        {
            if (signals == null || signals.Count == 0) return 0;

            double totalStoplossPercentage = 0;
            int signalCount = 0;

            foreach (var signal in signals)
            {
                double stoplossPercentage;
                if (signal.Side.ToLower() == "long")
                {
                    stoplossPercentage = Math.Abs((signal.Stoploss - signal.Entry) / signal.Entry) * 100;
                }
                else
                {
                    stoplossPercentage = Math.Abs((signal.Entry - signal.Stoploss) / signal.Entry) * 100;
                }
                totalStoplossPercentage += stoplossPercentage;
                signalCount++;
            }

            return signalCount > 0 ? Math.Round(totalStoplossPercentage / signalCount, 2) : 0;
        }

        public double CalculateRiskPercentage(List<SignalPerformance> signalPerformances)
        {
            if (signalPerformances == null || signalPerformances.Count == 0) return 0;

            signalPerformances = signalPerformances.Where(sp => sp.Status == "Open" || sp.Status == "Closed").ToList();

            double totalRisk = 0;
            int signalCount = 0;

            foreach (var performance in signalPerformances)
            {
                double risk = 100;

                bool stoplossHit = performance.Notes != null && performance.Notes.Contains("Stoploss Hit");

                if (performance.TakeProfitsAchieved.HasValue && performance.TakeProfitsAchieved.Value > 0)
                {
                    var weight = 100 / performance.TakeProfitCount;
                    risk -= performance.TakeProfitsAchieved.Value * weight;
                }

                totalRisk += risk;
                signalCount++;
            }

            return signalCount > 0 ? Math.Round(totalRisk / signalCount, 0) : 0;
        }

        // Function to calc average time
        public string CalculateAverageTimeframe(List<SignalPerformance> signalPerformances)
        {
            if (signalPerformances == null || signalPerformances.Count == 0) return "0 days 0 hours 0 minutes";

            var closedSignals = signalPerformances.Where(sp => sp.EndTime.HasValue).ToList();
            if (closedSignals.Count == 0) return "0 days 0 hours 0 minutes";

            double totalDuration = 0;
            foreach (var performance in closedSignals)
            {
                var duration = (performance.EndTime.Value - performance.StartTime).TotalMinutes;
                totalDuration += duration;
            }

            double averageDuration = totalDuration / closedSignals.Count;
            TimeSpan averageTimeSpan = TimeSpan.FromMinutes(averageDuration);

            return $"{averageTimeSpan.Days} days {averageTimeSpan.Hours} hours {averageTimeSpan.Minutes} minutes";
        }

        // Function to assign trade style tags based on average timeframe
        public string AssignTradeStyleTags(string averageTimeframe)
        {
            var parts = averageTimeframe.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            int days = int.Parse(parts[0]);
            int hours = int.Parse(parts[2]);
            int minutes = int.Parse(parts[4]);

            int totalMinutes = (days * 24 * 60) + (hours * 60) + minutes;

            var tags = new List<string>();

            if (totalMinutes <= 60)
            {
                tags.Add("Scalping");
            }
            if (totalMinutes <= 1440)
            {
                tags.Add("Day Trading");
            }
            if (totalMinutes <= 4320)
            {
                tags.Add("Range Trading");
            }
            if (totalMinutes <= 20160)
            {
                tags.Add("Swing Trading");
            }
            if (totalMinutes > 20160)
            {
                tags.Add("Trend Trading");
            }

            return string.Join(", ", tags);
        }

        // Function to Calc average TP %
        public double CalculateAverageTakeProfitPercentage(List<Signal> signals)
        {
            if (signals == null || signals.Count == 0) return 0;

            double totalTakeProfitPercentage = 0;
            int takeProfitCount = 0;

            foreach (var signal in signals)
            {
                var takeProfits = signal.TakeProfits.Split(',')
                    .Select(t => {
                        float.TryParse(t.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float result);
                        return result;
                    })
                    .ToList();

                foreach (var takeProfit in takeProfits)
                {
                    double takeProfitPercentage;
                    if (signal.Side.ToLower() == "long")
                    {
                        takeProfitPercentage = Math.Abs((takeProfit - signal.Entry) / signal.Entry) * 100;
                    }
                    else
                    {
                        takeProfitPercentage = Math.Abs((signal.Entry - takeProfit) / signal.Entry) * 100;
                    }
                    totalTakeProfitPercentage += takeProfitPercentage;
                    takeProfitCount++;
                }
            }

            return takeProfitCount > 0 ? Math.Round(totalTakeProfitPercentage / takeProfitCount, 2) : 0;
        }

        // Function cals average TP % per TP
        public string CalculateAverageTakeProfitPercentagePerTP(List<Signal> signals)
        {
            if (signals == null || signals.Count == 0) return string.Empty;

            var takeProfitSums = new Dictionary<int, double>();
            var takeProfitCounts = new Dictionary<int, int>();

            foreach (var signal in signals)
            {
                var takeProfits = signal.TakeProfits.Split(',')
                    .Select(t => {
                        float.TryParse(t.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float result);
                        return result;
                    })
                    .ToList();

                for (int i = 0; i < takeProfits.Count; i++)
                {
                    double takeProfitPercentage;
                    if (signal.Side.ToLower() == "long")
                    {
                        takeProfitPercentage = Math.Abs((takeProfits[i] - signal.Entry) / signal.Entry) * 100;
                    }
                    else
                    {
                        takeProfitPercentage = Math.Abs((signal.Entry - takeProfits[i]) / signal.Entry) * 100;
                    }

                    if (!takeProfitSums.ContainsKey(i))
                    {
                        takeProfitSums[i] = 0;
                        takeProfitCounts[i] = 0;
                    }

                    takeProfitSums[i] += takeProfitPercentage;
                    takeProfitCounts[i]++;
                }
            }

            var averageTakeProfitPercentages = new List<double>();
            foreach (var key in takeProfitSums.Keys.OrderBy(k => k))
            {
                double average = takeProfitSums[key] / takeProfitCounts[key];
                averageTakeProfitPercentages.Add(average);
            }

            averageTakeProfitPercentages.Sort();

            var averageTakeProfitPercentageStrings = averageTakeProfitPercentages
                .Select(avg => $"{Math.Round(avg, 1)}%")
                .ToList();

            return string.Join(", ", averageTakeProfitPercentageStrings);
        }

        // Function to save last signal date
        public DateTime? GetLastSignalDate(List<Signal> signals)
        {
            if (signals == null || signals.Count == 0) return null;
            return signals.Max(s => s.Time);
        }

        // Function to calc win rates
        public (int averageWinRate, int longWinRate, int shortWinRate) CalculateWinRates(List<SignalPerformance> signalPerformances, List<Signal> signals)
        {
            if (signalPerformances == null || signalPerformances.Count == 0 || signals == null || signals.Count == 0)
                return (0, 0, 0);

            var signalDict = signals.ToDictionary(s => s.Id, s => s.Side);

            var longSignals = signalPerformances.Where(sp => signalDict.ContainsKey(sp.SignalId) && signalDict[sp.SignalId] == "long").ToList();
            var shortSignals = signalPerformances.Where(sp => signalDict.ContainsKey(sp.SignalId) && signalDict[sp.SignalId] == "short").ToList();

            double totalWins = signalPerformances.Count(sp => sp.TakeProfitsAchieved.HasValue && sp.TakeProfitsAchieved.Value > 0);
            double totalLosses = signalPerformances.Count(sp => sp.Notes != null && sp.Notes.Contains("Stoploss Hit") && (!sp.TakeProfitsAchieved.HasValue || sp.TakeProfitsAchieved.Value == 0));

            double longWins = longSignals.Count(sp => sp.TakeProfitsAchieved.HasValue && sp.TakeProfitsAchieved.Value > 0);
            double longLosses = longSignals.Count(sp => sp.Notes != null && sp.Notes.Contains("Stoploss Hit") && (!sp.TakeProfitsAchieved.HasValue || sp.TakeProfitsAchieved.Value == 0));

            double shortWins = shortSignals.Count(sp => sp.TakeProfitsAchieved.HasValue && sp.TakeProfitsAchieved.Value > 0);
            double shortLosses = shortSignals.Count(sp => sp.Notes != null && sp.Notes.Contains("Stoploss Hit") && (!sp.TakeProfitsAchieved.HasValue || sp.TakeProfitsAchieved.Value == 0));

            int averageWinRate = (totalWins + totalLosses) > 0 ? (int)Math.Round((totalWins / (totalWins + totalLosses)) * 100) : 0;
            int longWinRate = (longWins + longLosses) > 0 ? (int)Math.Round((longWins / (longWins + longLosses)) * 100) : 0;
            int shortWinRate = (shortWins + shortLosses) > 0 ? (int)Math.Round((shortWins / (shortWins + shortLosses)) * 100) : 0;

            return (averageWinRate, longWinRate, shortWinRate);
        }

        // Function to calculate the average leverage
        public double CalculateAverageLeverage(List<Signal> signals)
        {
            if (signals == null || signals.Count == 0) return 0;

            double totalLeverage = signals.Sum(s => s.Leverage);
            return Math.Round(totalLeverage / signals.Count, 2);
        }

        public async Task CalculateAndInsertProviderDataAsync()
        {
            const int maxRetries = 3;
            int attempt = 0;
            Exception? lastException = null;

            while (attempt < maxRetries)
            {
                try
                {
                    var ninetyDaysAgo = DateTime.Now.AddDays(-90);

                    var signals = _context.Signals
                        .Where(s => s.Time >= ninetyDaysAgo)
                        .ToList();

                    var providers = signals.Select(s => s.Provider).Distinct().ToList();

                    foreach (var provider in providers)
                    {
                        var providerSignals = signals.Where(s => s.Provider == provider).ToList();
                        var providerSignalPerformances = _context.SignalPerformances
                            .Where(s => providerSignals.Select(signal => signal.Id).Contains(s.SignalId))
                            .ToList();

                        var shortSignals = providerSignals.Where(s => s.Side == "short").ToList();
                        var longSignals = providerSignals.Where(s => s.Side == "long").ToList();

                        var (shortRatio, longRatio) = CalculateRatios(shortSignals.Count, longSignals.Count);
                        var (overallRRR, rrrCategory, _) = CalculateRRR(providerSignals);
                        var (frequency, averageTradesPerDay) = CalculateSignalFrequency(providerSignals);
                        var averageStoploss = CalculateAverageStoplossPercentage(providerSignals);
                        var averageTakeProfit = CalculateAverageTakeProfitPercentage(providerSignals);
                        var averageTakeProfitPerTP = CalculateAverageTakeProfitPercentagePerTP(providerSignals);
                        var averageLeverage = CalculateAverageLeverage(providerSignals);
                        var risk = CalculateRiskPercentage(providerSignalPerformances);
                        var averageTimeFrame = CalculateAverageTimeframe(providerSignalPerformances);
                        var tradeTags = AssignTradeStyleTags(averageTimeFrame);
                        var (averageWin, longWin, shortWin) = CalculateWinRates(providerSignalPerformances, providerSignals);
                        var takeProfitDistribution = CalculateTakeProfitDistrobution(providerSignalPerformances);
                        var nulls = providerSignalPerformances.Count(s => s.Status == "Canceled");
                        var tpCount = takeProfitDistribution.Count;
                        var lastSignalDate = GetLastSignalDate(providerSignals);
                        var isActive = lastSignalDate.HasValue && (DateTime.Now - lastSignalDate.Value).TotalDays <= 60;

                        var existingProvider = _context.Provider.FirstOrDefault(p => p.Name == provider);

                        if (existingProvider != null)
                        {
                            // Update existing provider
                            existingProvider.RRR = overallRRR.ToString();
                            existingProvider.AverageProfitPerTrade = averageTakeProfit.ToString();
                            existingProvider.StoplossPersentage = averageStoploss.ToString();
                            existingProvider.SignalCount = providerSignals.Count;
                            existingProvider.AverageLeverage = averageLeverage.ToString();
                            existingProvider.TakeProfitTargets = averageTakeProfitPerTP;
                            existingProvider.SignalsNullified = nulls.ToString();
                            existingProvider.TradeStyle = tradeTags;
                            existingProvider.TradesPerDay = averageTradesPerDay.ToString();
                            existingProvider.TradeTimeframes = averageTimeFrame;
                            existingProvider.AverageWinRate = averageWin.ToString();
                            existingProvider.LongWinRate = longWin.ToString();
                            existingProvider.ShortWinRate = shortWin.ToString();
                            existingProvider.LongCount = longSignals.Count();
                            existingProvider.ShortCount = shortSignals.Count();
                            existingProvider.LongRatio = (int)longRatio;
                            existingProvider.ShortRatio = (int)shortRatio;
                            existingProvider.TpAchieved = string.Join(", ", takeProfitDistribution);
                            existingProvider.Risk = risk.ToString();
                            existingProvider.TakeProfitDistribution = string.Join(", ", takeProfitDistribution);
                            existingProvider.IsActive = isActive;
                            existingProvider.LastProvidedSignal = lastSignalDate.HasValue ? lastSignalDate.Value.ToString("yyyy-MM-dd") : null;
                        }
                        else
                        {
                            // Add new provider
                            var providerData = new Provider
                            {
                                Name = provider,
                                RRR = overallRRR.ToString(),
                                AverageProfitPerTrade = averageTakeProfit.ToString(),
                                StoplossPersentage = averageStoploss.ToString(),
                                SignalCount = providerSignals.Count,
                                AverageLeverage = averageLeverage.ToString(),
                                TakeProfitTargets = averageTakeProfitPerTP,
                                SignalsNullified = nulls.ToString(),
                                TradeStyle = tradeTags,
                                TradesPerDay = averageTradesPerDay.ToString(),
                                TradeTimeframes = averageTimeFrame,
                                AverageWinRate = averageWin.ToString(),
                                LongWinRate = longWin.ToString(),
                                ShortWinRate = shortWin.ToString(),
                                LongCount = longSignals.Count(),
                                ShortCount = shortSignals.Count(),
                                LongRatio = (int)longRatio,
                                ShortRatio = (int)shortRatio,
                                TpAchieved = string.Join(", ", takeProfitDistribution),
                                Risk = risk.ToString(),
                                TakeProfitDistribution = string.Join(", ", takeProfitDistribution),
                                Telegram = "",
                                IsActive = isActive,
                                LastProvidedSignal = lastSignalDate.HasValue ? lastSignalDate.Value.ToString("yyyy-MM-dd") : null
                            };

                            _context.Provider.Add(providerData);
                        }

                        // Update ProviderSettings for all users
                        var users = _context.UsersData.ToList();
                        foreach (var user in users)
                        {
                            var providerSetting = _context.ProvidersSettings
                                .FirstOrDefault(ps => ps.UserId == user.Id && ps.ProviderId == provider);

                            if (providerSetting != null)
                            {
                                providerSetting.TpCount = tpCount;
                                if (providerSetting.TpPercentages.Count == 0)
                                {
                                    providerSetting.TpPercentages.Clear();
                                    for (int i = 0; i < tpCount; i++)
                                    {
                                        if (i == tpCount - 1)
                                        {
                                            providerSetting.TpPercentages.Add(100);
                                        }
                                        else
                                        {
                                            providerSetting.TpPercentages.Add(25);
                                        }
                                    }
                                }
                            }
                            else
                            {
                                Console.WriteLine($"Provider setting not found for user {user.Id} and provider {provider}");
                            }
                        }
                    }
                    _context.SaveChanges();
                    return; // Success, exit method
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    await _errorLogService.LogErrorAsync(
                        $"Failed to calculate and insert provider data (attempt {attempt + 1})",
                        ex.StackTrace,
                        nameof(SignalProviderService),
                        ex.Message
                    );
                    attempt++;
                    await Task.Delay(1000); // Optional: wait before retry
                }
            }

            // If all retries failed, log a final error
            if (lastException != null)
            {
                await _errorLogService.LogErrorAsync(
                    "All attempts to calculate and insert provider data failed.",
                    lastException.StackTrace,
                    nameof(SignalProviderService),
                    lastException.Message
                );
            }
        }


        public void CreateDefaultProviderSettingsForUsers()
        {
            var users = _context.UsersData.ToList();
            var providers = _context.Provider.ToList();

            foreach (var user in users)
            {
                foreach (var provider in providers)
                {
                    var existingSetting = _context.ProvidersSettings
                        .FirstOrDefault(ps => ps.UserId == user.Id && ps.ProviderId == provider.Name);

                    if (existingSetting == null)
                    {
                        var newSetting = new ProviderSettings
                        {
                            UserId = user.Id,
                            ProviderId = provider.Name
                        };

                        _context.ProvidersSettings.Add(newSetting);
                    }
                }
            }

            _context.SaveChanges();
        }
    }
}
