using AutoSignals.Data;
using AutoSignals.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using starterkit.Models;
using System.Diagnostics;
using System.Globalization;
using System.Linq;

[Authorize(Roles = "Admin")]
public class SignalProviderController : Controller
{
    private readonly ILogger<SignalProviderController> _logger;
    private readonly AutoSignalsDbContext _context;

    public SignalProviderController(ILogger<SignalProviderController> logger, AutoSignalsDbContext context)
    {
        _logger = logger;
        _context = context;
    }

    // Function to calculate ratios
    (double shortRatio, double longRatio) CalculateRatios(int shortCount, int longCount)
    {
        int total = shortCount + longCount;
        if (total == 0) return (0, 0);
        double shortRatio = Math.Round((double)shortCount / total * 100, 0);
        double longRatio = Math.Round((double)longCount / total * 100, 0);
        return (shortRatio, longRatio);
    }

    // Function that calculates take profit distribution
    List<int> CalculateTakeProfitDistrobution(List<SignalPerformance> signalPerformances)
    {
        // Dictionary to store the count of each take profit level
        Dictionary<int, int> takeProfitCounts = new Dictionary<int, int>();

        // Iterate through the list of signal performances
        foreach (var performance in signalPerformances)
        {
            if (performance.TakeProfitsAchieved.HasValue)
            {
                int achievedTP = performance.TakeProfitsAchieved.Value;

                // Increment counters for each achieved take profit level
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

        // Calculate total number of signals
        int totalSignals = signalPerformances.Count;

        // Convert the counts to whole number percentages
        return takeProfitCounts.OrderBy(kvp => kvp.Key)
                               .Select(kvp => (int)((kvp.Value / (double)totalSignals) * 100))
                               .ToList();
    }

    // Function to calc RRR
    (double overallRRR, string rrrCategory, List<(int SignalId, float TakeProfit, float RRR)>) CalculateRRR(List<Signal> providerSignals)
    {
        var rrrResults = new List<(int SignalId, float TakeProfit, float RRR)>();
        float totalReward = 0;
        float totalRisk = 0;

        foreach (var signal in providerSignals)
        {
            // Split TakeProfits string and convert to floats
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
                    // Long trade RRR calculation
                    reward = takeProfit - signal.Entry;
                    risk = signal.Entry - signal.Stoploss;
                }
                else
                {
                    // Short trade RRR calculation
                    reward = signal.Entry - takeProfit;
                    risk = signal.Stoploss - signal.Entry;
                }

                // Avoid division by zero
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

        // Categorize the overall RRR
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
    private (Dictionary<DateTime, int> Frequency, double AverageTradesPerDay) CalculateSignalFrequency(List<Signal> signals)
    {
        var groupedByDay = signals
            .GroupBy(s => s.Time.Date) // Group by day
            .ToDictionary(g => g.Key, g => g.Count());

        double averageTradesPerDay = groupedByDay.Count > 0 ? Math.Round((double)signals.Count / groupedByDay.Count, 2) : 0;

        return (groupedByDay, averageTradesPerDay);
    }

    // Function to calculate the average stoploss percentage
    private double CalculateAverageStoplossPercentage(List<Signal> signals)
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

    private double CalculateRiskPercentage(List<SignalPerformance> signalPerformances)
    {
        if (signalPerformances == null || signalPerformances.Count == 0) return 0;

        signalPerformances = signalPerformances.Where(sp => sp.Status == "Open" || sp.Status == "Closed").ToList();

        double totalRisk = 0;
        int signalCount = 0;

        foreach (var performance in signalPerformances)
        {
            //if (performance.Notes.Contains("Stoploss Hit") || performance.TakeProfitsAchieved.Value > 0)
            //{ 
                double risk = 100;

                // Check if stoploss was hit
                bool stoplossHit = performance.Notes != null && performance.Notes.Contains("Stoploss Hit");

                // Calculate risk based on take profits achieved
                if (performance.TakeProfitsAchieved.HasValue && performance.TakeProfitsAchieved.Value > 0)
                {
                    var weight = 100 / performance.TakeProfitCount;
                    // Assign a weighted value to each take profit achieved
                    risk -= performance.TakeProfitsAchieved.Value * weight; // Example weight: 0.1 per take profit
                }

                // Add risk for stoploss hit
                //if (stoplossHit)
                //{
                //    risk += 100; // Example weight: 1 for stoploss hit
                //}

                totalRisk += risk;
                signalCount++;
            //}
            
        }

        return signalCount > 0 ? Math.Round(totalRisk / signalCount, 0) : 0;
    }

    // Function to calc average time
    private string CalculateAverageTimeframe(List<SignalPerformance> signalPerformances)
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
    private string AssignTradeStyleTags(string averageTimeframe)
    {
        // Parse the averageTimeframe string
        var parts = averageTimeframe.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        int days = int.Parse(parts[0]);
        int hours = int.Parse(parts[2]);
        int minutes = int.Parse(parts[4]);

        // Calculate total minutes
        int totalMinutes = (days * 24 * 60) + (hours * 60) + minutes;

        // List to hold the tags
        var tags = new List<string>();

        // Determine trade style based on total minutes
        if (totalMinutes <= 60) // Scalping: Seconds to minutes
        {
            tags.Add("Scalping");
        }
        if (totalMinutes <= 1440) // Day Trading: Minutes to hours (never overnight)
        {
            tags.Add("Day Trading");
        }
        if (totalMinutes <= 4320) // Range Trading: Hours to days
        {
            tags.Add("Range Trading");
        }
        if (totalMinutes <= 20160) // Swing Trading: Days to weeks
        {
            tags.Add("Swing Trading");
        }
        if (totalMinutes > 20160) // Trend Trading: Days to months
        {
            tags.Add("Trend Trading");
        }

        // Return the tags as a comma-separated string
        return string.Join(", ", tags);
    }

    // Function to Calc average TP %
    private double CalculateAverageTakeProfitPercentage(List<Signal> signals)
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
    private string CalculateAverageTakeProfitPercentagePerTP(List<Signal> signals)
    {
        if (signals == null || signals.Count == 0) return string.Empty;

        // Dictionary to store the sum of take profit percentages for each take profit point
        var takeProfitSums = new Dictionary<int, double>();
        // Dictionary to store the count of take profit points for averaging
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

        // Calculate the average for each take profit point and format as percentage string
        var averageTakeProfitPercentages = new List<double>();
        foreach (var key in takeProfitSums.Keys.OrderBy(k => k))
        {
            double average = takeProfitSums[key] / takeProfitCounts[key];
            averageTakeProfitPercentages.Add(average);
        }

        // Sort the average percentages from smallest to biggest
        averageTakeProfitPercentages.Sort();

        // Convert to percentage strings and join the list into a single string
        var averageTakeProfitPercentageStrings = averageTakeProfitPercentages
            .Select(avg => $"{Math.Round(avg, 1)}%")
            .ToList();

        return string.Join(", ", averageTakeProfitPercentageStrings);
    }

    // Function to calc win rates
    private (int averageWinRate, int longWinRate, int shortWinRate) CalculateWinRates(List<SignalPerformance> signalPerformances, List<Signal> signals)
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
    private double CalculateAverageLeverage(List<Signal> signals)
    {
        if (signals == null || signals.Count == 0) return 0;

        double totalLeverage = signals.Sum(s => s.Leverage);
        return Math.Round(totalLeverage / signals.Count, 2);
    }

    [Route("/signalprovider/dashboard")]
    public IActionResult Dashboard()
    {
        var ninetyDaysAgo = DateTime.Now.AddDays(-90);

        var signals = _context.Signals
            .Where(s => s.Time >= ninetyDaysAgo)
            .ToList();


        // ----- BybitPro ----- //
        var bybitProSignals = signals
                              .Where(s => s.Provider == "BybitPro")
                              .ToList();

        var bybitProSignalPerformances = _context.SignalPerformances
                            .Where(s => bybitProSignals.Select(signal => signal.Id).Contains(s.SignalId))
                            .ToList();

        var bybitProCount = bybitProSignals
                              .Count(s => s.Provider == "BybitPro");

        var bybitProShorts = bybitProSignals
            .Where(s => s.Side == "short")
            .ToList();

        var bybitProLongs = bybitProSignals
            .Where(s => s.Side == "long")
            .ToList();

        var bybitProShortsCount = bybitProShorts.Count();
        var bybitProLongsCount = bybitProLongs.Count();
        var (bybitProShortRatio, bybitProLongRatio) = 
            CalculateRatios(bybitProShortsCount, bybitProLongsCount);

        var (bybitProRRR, bybitProRRRCatagory, bybitProIndevidualRRR) = CalculateRRR(bybitProSignals);

        var (bybitProDailyTrades, bybitProFrequency) = CalculateSignalFrequency(bybitProSignals);

        var bybitProAverageStoploss = CalculateAverageStoplossPercentage(bybitProSignals);
        var bybitProAverageTakeProfit = CalculateAverageTakeProfitPercentage(bybitProSignals);
        var bybitProAverageTakeProfitPerTP = CalculateAverageTakeProfitPercentagePerTP(bybitProSignals);

        var bybitProAverageLeverage = CalculateAverageLeverage(bybitProSignals);

        var bybitProRisk = CalculateRiskPercentage(bybitProSignalPerformances);

        var bybitProAverageTimeFrame = CalculateAverageTimeframe(bybitProSignalPerformances);
        var bybitProTradeTags = AssignTradeStyleTags(bybitProAverageTimeFrame);
        var (bybitProAverageWin, bybitProLongWin, bybitProShortWin) = CalculateWinRates(bybitProSignalPerformances, bybitProSignals);

        var bybitProTakeProfitDistribution = CalculateTakeProfitDistrobution(bybitProSignalPerformances);

        var bybitProNulls = bybitProSignalPerformances
            .Count(s => s.Status == "Canceled");

        // ----- Scalping300 ----- //
        var scalping300Signals = signals
                                  .Where(s => s.Provider == "Scalping300")
                                  .ToList();
        var scalping300Count = signals
                                  .Count(s => s.Provider == "Scalping300");

        var scalping300Performances = _context.SignalPerformances
                            .Where(s => scalping300Signals.Select(signal => signal.Id).Contains(s.SignalId))
                            .ToList();

        var scalping300Shorts = scalping300Signals
            .Where(s => s.Side == "short")
            .ToList();

        var scalping300Longs = scalping300Signals
            .Where(s => s.Side == "long")
            .ToList();

        var scalping300ShortsCount = scalping300Shorts.Count();
        var scalping300LongsCount = scalping300Longs.Count();
        var (scalping300ShortRatio, scalping300LongRatio) = 
            CalculateRatios(scalping300ShortsCount, scalping300LongsCount);

        var (scalping300RRR, scalping300RRRCatagory, scalping300IndevidualRRR) = CalculateRRR(scalping300Signals);

        var (scalping300DailyTrades, scalping300Frequency) = CalculateSignalFrequency(scalping300Signals);

        var scalping300AverageStoploss = CalculateAverageStoplossPercentage(scalping300Signals);
        var scalping300AverageTakeProfit = CalculateAverageTakeProfitPercentage(scalping300Signals);
        var scalping300AverageTakeProfitPerTP = CalculateAverageTakeProfitPercentagePerTP(scalping300Signals);

        var scalping300AverageLeverage = CalculateAverageLeverage(scalping300Signals);

        var scalping300Risk = CalculateRiskPercentage(scalping300Performances);

        var scalping300AverageTimeFrame = CalculateAverageTimeframe(scalping300Performances);
        var scalping300TradeTags = AssignTradeStyleTags(scalping300AverageTimeFrame);
        var (scalping300AverageWin, scalping300LongWin, scalping300ShortWin) = CalculateWinRates(scalping300Performances, scalping300Signals);

        var scalping300TakeProfitDistribution = CalculateTakeProfitDistrobution(scalping300Performances);

        var scalping300Nulls = scalping300Performances
            .Count(s => s.Status == "Canceled");


        // ----- Masters of Binance ----- //
        var mastersOfBinanceSignals = signals
                                  .Where(s => s.Provider == "BinanceMaster")
                                  .ToList();
        var mastersOfBinanceCount = signals
                                  .Count(s => s.Provider == "BinanceMaster");

        var mastersOfBinancePerformances = _context.SignalPerformances
                            .Where(s => mastersOfBinanceSignals.Select(signal => signal.Id).Contains(s.SignalId))
                            .ToList();

        var mastersOfBinanceShorts = mastersOfBinanceSignals
            .Where(s => s.Side == "short")
            .ToList();

        var mastersOfBinanceLongs = mastersOfBinanceSignals
            .Where(s => s.Side == "long")
            .ToList();

        var mastersOfBinanceShortsCount = mastersOfBinanceShorts.Count();
        var mastersOfBinanceLongsCount = mastersOfBinanceLongs.Count();
        var (mastersOfBinanceShortRatio, mastersOfBinanceLongRatio) =
            CalculateRatios(mastersOfBinanceShortsCount, mastersOfBinanceLongsCount);

        var (mastersOfBinanceRRR, mastersOfBinanceRRRCatagory, mastersOfBinanceIndevidualRRR) = CalculateRRR(mastersOfBinanceSignals);

        var (mastersOfBinanceDailyTrades, mastersOfBinanceFrequency) = CalculateSignalFrequency(mastersOfBinanceSignals);

        var mastersOfBinanceAverageStoploss = CalculateAverageStoplossPercentage(mastersOfBinanceSignals);
        var mastersOfBinanceAverageTakeProfit = CalculateAverageTakeProfitPercentage(mastersOfBinanceSignals);
        var mastersOfBinanceAverageTakeProfitPerTP = CalculateAverageTakeProfitPercentagePerTP(mastersOfBinanceSignals);

        var mastersOfBinanceAverageLeverage = CalculateAverageLeverage(mastersOfBinanceSignals);

        var mastersOfBinanceRisk = CalculateRiskPercentage(mastersOfBinancePerformances);

        var mastersOfBinanceAverageTimeFrame = CalculateAverageTimeframe(mastersOfBinancePerformances);
        var mastersOfBinanceTradeTags = AssignTradeStyleTags(mastersOfBinanceAverageTimeFrame);
        var (mastersOfBinanceAverageWin, mastersOfBinanceLongWin, mastersOfBinanceShortWin) = CalculateWinRates(mastersOfBinancePerformances, mastersOfBinanceSignals);

        var mastersOfBinanceTakeProfitDistribution = CalculateTakeProfitDistrobution(mastersOfBinancePerformances);

        var mastersOfBinanceNulls = mastersOfBinancePerformances
            .Count(s => s.Status == "Canceled");

        // ----- Alex Fredman ----- //
        var alexFredmanSignals = signals
                                  .Where(s => s.Provider == "AlexFredman")
                                  .ToList();
        var alexFredmanCount = signals
                                  .Count(s => s.Provider == "AlexFredman");

        var alexFredmanPerformances = _context.SignalPerformances
                            .Where(s => alexFredmanSignals.Select(signal => signal.Id).Contains(s.SignalId))
                            .ToList();

        var alexFredmanShorts = alexFredmanSignals
            .Where(s => s.Side == "short")
            .ToList();

        var alexFredmanLongs = alexFredmanSignals
            .Where(s => s.Side == "long")
            .ToList();

        var alexFredmanShortsCount = alexFredmanShorts.Count();
        var alexFredmanLongsCount = alexFredmanLongs.Count();
        var (alexFredmanShortRatio, alexFredmanLongRatio) =
            CalculateRatios(alexFredmanShortsCount, alexFredmanLongsCount);

        var (alexFredmanRRR, alexFredmanRRRCatagory, alexFredmanIndevidualRRR) = CalculateRRR(alexFredmanSignals);

        var (alexFredmanDailyTrades, alexFredmanFrequency) = CalculateSignalFrequency(alexFredmanSignals);

        var alexFredmanAverageStoploss = CalculateAverageStoplossPercentage(alexFredmanSignals);
        var alexFredmanAverageTakeProfit = CalculateAverageTakeProfitPercentage(alexFredmanSignals);
        var alexFredmanAverageTakeProfitPerTP = CalculateAverageTakeProfitPercentagePerTP(alexFredmanSignals);

        var alexFredmanAverageLeverage = CalculateAverageLeverage(alexFredmanSignals);

        var alexFredmanRisk = CalculateRiskPercentage(alexFredmanPerformances);

        var alexFredmanAverageTimeFrame = CalculateAverageTimeframe(alexFredmanPerformances);
        var alexFredmanTradeTags = AssignTradeStyleTags(alexFredmanAverageTimeFrame);
        var (alexFredmanAverageWin, alexFredmanLongWin, alexFredmanShortWin) = CalculateWinRates(alexFredmanPerformances, alexFredmanSignals);

        var alexFredmanTakeProfitDistribution = CalculateTakeProfitDistrobution(alexFredmanPerformances);

        var alexFredmanNulls = alexFredmanPerformances
            .Count(s => s.Status == "Canceled");

        // ----- Coin Coach ----- //
        var coinCoachSignals = signals
                                  .Where(s => s.Provider == "Coin Coach")
                                  .ToList();
        var coinCoachCount = signals
                                  .Count(s => s.Provider == "Coin Coach");

        var coinCoachPerformances = _context.SignalPerformances
                            .Where(s => coinCoachSignals.Select(signal => signal.Id).Contains(s.SignalId))
                            .ToList();

        var coinCoachShorts = coinCoachSignals
            .Where(s => s.Side == "short")
            .ToList();

        var coinCoachLongs = coinCoachSignals
            .Where(s => s.Side == "long")
            .ToList();

        var coinCoachShortsCount = coinCoachShorts.Count();
        var coinCoachLongsCount = coinCoachLongs.Count();
        var (coinCoachShortRatio, coinCoachLongRatio) =
            CalculateRatios(coinCoachShortsCount, coinCoachLongsCount);

        var (coinCoachRRR, coinCoachRRRCatagory, coinCoachIndevidualRRR) = CalculateRRR(coinCoachSignals);

        var (coinCoachDailyTrades, coinCoachFrequency) = CalculateSignalFrequency(coinCoachSignals);

        var coinCoachAverageStoploss = CalculateAverageStoplossPercentage(coinCoachSignals);
        var coinCoachAverageTakeProfit = CalculateAverageTakeProfitPercentage(coinCoachSignals);
        var coinCoachAverageTakeProfitPerTP = CalculateAverageTakeProfitPercentagePerTP(coinCoachSignals);

        var coinCoachAverageLeverage = CalculateAverageLeverage(coinCoachSignals);

        var coinCoachRisk = CalculateRiskPercentage(coinCoachPerformances);

        var coinCoachAverageTimeFrame = CalculateAverageTimeframe(coinCoachPerformances);
        var coinCoachTradeTags = AssignTradeStyleTags(coinCoachAverageTimeFrame);
        var (coinCoachAverageWin, coinCoachLongWin, coinCoachShortWin) = CalculateWinRates(coinCoachPerformances, coinCoachSignals);

        var coinCoachTakeProfitDistribution = CalculateTakeProfitDistrobution(coinCoachPerformances);

        var coinCoachNulls = coinCoachPerformances
            .Count(s => s.Status == "Canceled");

        // ----- WolfX ----- //
        var wolfXSignals = signals
                                  .Where(s => s.Provider == "WolfX")
                                  .ToList();
        var wolfXCount = signals
                                  .Count(s => s.Provider == "WolfX");

        var wolfXPerformances = _context.SignalPerformances
                            .Where(s => wolfXSignals.Select(signal => signal.Id).Contains(s.SignalId))
                            .ToList();

        var wolfXShorts = wolfXSignals
            .Where(s => s.Side == "short")
            .ToList();

        var wolfXLongs = wolfXSignals
            .Where(s => s.Side == "long")
            .ToList();

        var wolfXShortsCount = wolfXShorts.Count();
        var wolfXLongsCount = wolfXLongs.Count();
        var (wolfXShortRatio, wolfXLongRatio) =
            CalculateRatios(wolfXShortsCount, wolfXLongsCount);

        var (wolfXRRR, wolfXRRRCatagory, wolfXIndevidualRRR) = CalculateRRR(wolfXSignals);

        var (wolfXDailyTrades, wolfXFrequency) = CalculateSignalFrequency(wolfXSignals);

        var wolfXAverageStoploss = CalculateAverageStoplossPercentage(wolfXSignals);
        var wolfXAverageTakeProfit = CalculateAverageTakeProfitPercentage(wolfXSignals);
        var wolfXAverageTakeProfitPerTP = CalculateAverageTakeProfitPercentagePerTP(wolfXSignals);

        var wolfXAverageLeverage = CalculateAverageLeverage(wolfXSignals);

        var wolfXRisk = CalculateRiskPercentage(wolfXPerformances);

        var wolfXAverageTimeFrame = CalculateAverageTimeframe(wolfXPerformances);
        var wolfXTradeTags = AssignTradeStyleTags(wolfXAverageTimeFrame);
        var (wolfXAverageWin, wolfXLongWin, wolfXShortWin) = CalculateWinRates(wolfXPerformances, wolfXSignals);

        var wolfXTakeProfitDistribution = CalculateTakeProfitDistrobution(wolfXPerformances);

        var wolfXNulls = wolfXPerformances
            .Count(s => s.Status == "Canceled");

        // ----- Fed Russian Insider ----- //
        var fedRussianSignals = signals
                                  .Where(s => s.Provider == "Fed Russian Insider")
                                  .ToList();
        var fedRussianCount = signals
                                  .Count(s => s.Provider == "Fed Russian Insider");

        var fedRussianPerformances = _context.SignalPerformances
                            .Where(s => fedRussianSignals.Select(signal => signal.Id).Contains(s.SignalId))
                            .ToList();

        var fedRussianShorts = fedRussianSignals
            .Where(s => s.Side == "short")
            .ToList();

        var fedRussianLongs = fedRussianSignals
            .Where(s => s.Side == "long")
            .ToList();

        var fedRussianShortsCount = fedRussianShorts.Count();
        var fedRussianLongsCount = fedRussianLongs.Count();
        var (fedRussianShortRatio, fedRussianLongRatio) =
            CalculateRatios(fedRussianShortsCount, fedRussianLongsCount);

        var (fedRussianRRR, fedRussianRRRCatagory, fedRussianIndevidualRRR) = CalculateRRR(fedRussianSignals);

        var (fedRussianDailyTrades, fedRussianFrequency) = CalculateSignalFrequency(fedRussianSignals);

        var fedRussianAverageStoploss = CalculateAverageStoplossPercentage(fedRussianSignals);
        var fedRussianAverageTakeProfit = CalculateAverageTakeProfitPercentage(fedRussianSignals);
        var fedRussianAverageTakeProfitPerTP = CalculateAverageTakeProfitPercentagePerTP(fedRussianSignals);

        var fedRussianAverageLeverage = CalculateAverageLeverage(fedRussianSignals);

        var fedRussianRisk = CalculateRiskPercentage(fedRussianPerformances);

        var fedRussianAverageTimeFrame = CalculateAverageTimeframe(fedRussianPerformances);
        var fedRussianTradeTags = AssignTradeStyleTags(fedRussianAverageTimeFrame);
        var (fedRussianAverageWin, fedRussianLongWin, fedRussianShortWin) = CalculateWinRates(fedRussianPerformances, fedRussianSignals);

        var fedRussianTakeProfitDistribution = CalculateTakeProfitDistrobution(fedRussianPerformances);

        var fedRussianNulls = fedRussianPerformances
            .Count(s => s.Status == "Canceled");

        // ----- Andrew ----- //
        var andrewSignals = signals
                                  .Where(s => s.Provider == "CryptoAndrew")
                                  .ToList();
        var andrewCount = signals
                                  .Count(s => s.Provider == "CryptoAndrew");

        var andrewPerformances = _context.SignalPerformances
                            .Where(s => andrewSignals.Select(signal => signal.Id).Contains(s.SignalId))
                            .ToList();

        var andrewShorts = andrewSignals
            .Where(s => s.Side == "short")
            .ToList();

        var andrewLongs = andrewSignals
            .Where(s => s.Side == "long")
            .ToList();

        var andrewShortsCount = andrewShorts.Count();
        var andrewLongsCount = andrewLongs.Count();
        var (andrewShortRatio, andrewLongRatio) =
            CalculateRatios(andrewShortsCount, andrewLongsCount);

        var (andrewRRR, andrewRRRCatagory, andrewIndevidualRRR) = CalculateRRR(andrewSignals);

        var (andrewDailyTrades, andrewFrequency) = CalculateSignalFrequency(andrewSignals);

        var andrewAverageStoploss = CalculateAverageStoplossPercentage(andrewSignals);
        var andrewAverageTakeProfit = CalculateAverageTakeProfitPercentage(andrewSignals);
        var andrewAverageTakeProfitPerTP = CalculateAverageTakeProfitPercentagePerTP(andrewSignals);

        var andrewAverageLeverage = CalculateAverageLeverage(andrewSignals);

        var andrewRisk = CalculateRiskPercentage(andrewPerformances);

        var andrewAverageTimeFrame = CalculateAverageTimeframe(andrewPerformances);
        var andrewTradeTags = AssignTradeStyleTags(andrewAverageTimeFrame);
        var (andrewAverageWin, andrewLongWin, andrewShortWin) = CalculateWinRates(andrewPerformances, andrewSignals);

        var andrewTakeProfitDistribution = CalculateTakeProfitDistrobution(andrewPerformances);

        var andrewNulls = andrewPerformances
            .Count(s => s.Status == "Canceled");

        // ----- CIC ----- //
        var cicSignals = signals
                                  .Where(s => s.Provider == "CryptoInnerCircle")
                                  .ToList();
        var cicCount = signals
                                  .Count(s => s.Provider == "CryptoInnerCircle");

        var cicPerformances = _context.SignalPerformances
                            .Where(s => cicSignals.Select(signal => signal.Id).Contains(s.SignalId))
                            .ToList();

        var cicShorts = cicSignals
            .Where(s => s.Side == "short")
            .ToList();

        var cicLongs = cicSignals
            .Where(s => s.Side == "long")
            .ToList();

        var cicShortsCount = cicShorts.Count();
        var cicLongsCount = cicLongs.Count();
        var (cicShortRatio, cicLongRatio) =
            CalculateRatios(cicShortsCount, cicLongsCount);

        var (cicRRR, cicRRRCatagory, cicIndevidualRRR) = CalculateRRR(cicSignals);

        var (cicDailyTrades, cicFrequency) = CalculateSignalFrequency(cicSignals);

        var cicAverageStoploss = CalculateAverageStoplossPercentage(cicSignals);
        var cicAverageTakeProfit = CalculateAverageTakeProfitPercentage(cicSignals);
        var cicAverageTakeProfitPerTP = CalculateAverageTakeProfitPercentagePerTP(cicSignals);

        var cicAverageLeverage = CalculateAverageLeverage(cicSignals);

        var cicRisk = CalculateRiskPercentage(cicPerformances);

        var cicAverageTimeFrame = CalculateAverageTimeframe(cicPerformances);
        var cicTradeTags = AssignTradeStyleTags(cicAverageTimeFrame);
        var (cicAverageWin, cicLongWin, cicShortWin) = CalculateWinRates(cicPerformances, cicSignals);

        var cicTakeProfitDistribution = CalculateTakeProfitDistrobution(cicPerformances);

        var cicNulls = cicPerformances
            .Count(s => s.Status == "Canceled");

        // ----- Aman ----- //
        var amanSignals = signals
                                  .Where(s => s.Provider == "CryptoAman")
                                  .ToList();
        var amanCount = signals
                                  .Count(s => s.Provider == "CryptoAman");

        var amanPerformances = _context.SignalPerformances
                            .Where(s => amanSignals.Select(signal => signal.Id).Contains(s.SignalId))
                            .ToList();

        var amanShorts = amanSignals
            .Where(s => s.Side == "short")
            .ToList();

        var amanLongs = amanSignals
            .Where(s => s.Side == "long")
            .ToList();

        var amanShortsCount = amanShorts.Count();
        var amanLongsCount = amanLongs.Count();
        var (amanShortRatio, amanLongRatio) =
            CalculateRatios(amanShortsCount, amanLongsCount);

        var (amanRRR, amanRRRCatagory, amanIndevidualRRR) = CalculateRRR(amanSignals);

        var (amanDailyTrades, amanFrequency) = CalculateSignalFrequency(amanSignals);

        var amanAverageStoploss = CalculateAverageStoplossPercentage(amanSignals);
        var amanAverageTakeProfit = CalculateAverageTakeProfitPercentage(amanSignals);
        var amanAverageTakeProfitPerTP = CalculateAverageTakeProfitPercentagePerTP(amanSignals);

        var amanAverageLeverage = CalculateAverageLeverage(amanSignals);

        var amanRisk = CalculateRiskPercentage(amanPerformances);

        var amanAverageTimeFrame = CalculateAverageTimeframe(amanPerformances);
        var amanTradeTags = AssignTradeStyleTags(amanAverageTimeFrame);
        var (amanAverageWin, amanLongWin, amanShortWin) = CalculateWinRates(amanPerformances, amanSignals);

        var amanTakeProfitDistribution = CalculateTakeProfitDistrobution(amanPerformances);

        var amanNulls = amanPerformances
            .Count(s => s.Status == "Canceled");

        // You can pass these counts to the view if needed
        ViewBag.BybitProCount = bybitProCount;
        ViewBag.BybitProShorts = bybitProShorts;
        ViewBag.BybitProLongs = bybitProLongs;
        ViewBag.BybitProShortsCount = bybitProShortsCount;
        ViewBag.BybitProLongsCount = bybitProLongsCount;
        ViewBag.BybitProShortRatio = bybitProShortRatio;
        ViewBag.BybitProLongRatio = bybitProLongRatio;
        ViewBag.BybitProRRR = bybitProRRR;
        ViewBag.BybitProRRRCatagory = bybitProRRRCatagory;
        ViewBag.BybitProFrequency = bybitProFrequency;
        ViewBag.BybitProAverageStoploss = bybitProAverageStoploss;
        ViewBag.BybitProAverageTakeProfit = bybitProAverageTakeProfit;
        ViewBag.BybitProAverageTakeProfitPerTP = bybitProAverageTakeProfitPerTP;
        ViewBag.BybitProAverageLeverage = bybitProAverageLeverage;
        ViewBag.BybitProRisk = bybitProRisk;
        ViewBag.BybitProAverageTimeFrame = bybitProAverageTimeFrame;
        ViewBag.BybitProTags = bybitProTradeTags;
        ViewBag.BybitProAverageWin = bybitProAverageWin;
        ViewBag.BybitProLongWin = bybitProLongWin;
        ViewBag.BybitProShortWin = bybitProShortWin;
        ViewBag.BybitProTakeProfitDistribution = bybitProTakeProfitDistribution;
        ViewBag.BybitProNulls = bybitProNulls;

        ViewBag.Scalping300Count = scalping300Count;
        ViewBag.Scalping300Shorts = scalping300Shorts;
        ViewBag.Scalping300Longs = scalping300Longs;
        ViewBag.Scalping300ShortsCount = scalping300ShortsCount;
        ViewBag.Scalping300LongsCount = scalping300LongsCount;
        ViewBag.Scalping300ShortRatio = scalping300ShortRatio;
        ViewBag.Scalping300LongRatio = scalping300LongRatio;
        ViewBag.Scalping300RRR = scalping300RRR;
        ViewBag.Scalping300RRRCatagory = scalping300RRRCatagory;
        ViewBag.Scalping300Frequency = scalping300Frequency;
        ViewBag.Scalping300AverageStoploss = scalping300AverageStoploss;
        ViewBag.Scalping300AverageTakeProfit = scalping300AverageTakeProfit;
        ViewBag.Scalping300AverageTakeProfitPerTP = scalping300AverageTakeProfitPerTP;
        ViewBag.Scalping300AverageLeverage = scalping300AverageLeverage;
        ViewBag.Scalping300Risk = scalping300Risk;
        ViewBag.Scalping300AverageTimeFrame = scalping300AverageTimeFrame;
        ViewBag.Scalping300Tags = scalping300TradeTags;
        ViewBag.Scalping300AverageWin = scalping300AverageWin;
        ViewBag.Scalping300LongWin = scalping300LongWin;
        ViewBag.Scalping300ShortWin = scalping300ShortWin;
        ViewBag.Scalping300TakeProfitDistribution = scalping300TakeProfitDistribution;
        ViewBag.Scalping300Nulls = scalping300Nulls;

        ViewBag.MastersOfBinanceCount = mastersOfBinanceCount;
        ViewBag.MastersOfBinanceShorts = mastersOfBinanceShorts;
        ViewBag.MastersOfBinanceLongs = mastersOfBinanceLongs;
        ViewBag.MastersOfBinanceShortsCount = mastersOfBinanceShortsCount;
        ViewBag.MastersOfBinanceLongsCount = mastersOfBinanceLongsCount;
        ViewBag.MastersOfBinanceShortRatio = mastersOfBinanceShortRatio;
        ViewBag.MastersOfBinanceLongRatio = mastersOfBinanceLongRatio;
        ViewBag.MastersOfBinanceRRR = mastersOfBinanceRRR;
        ViewBag.MastersOfBinanceRRRCatagory = mastersOfBinanceRRRCatagory;
        ViewBag.MastersOfBinanceFrequency = mastersOfBinanceFrequency;
        ViewBag.MastersOfBinanceAverageStoploss = mastersOfBinanceAverageStoploss;
        ViewBag.MastersOfBinanceAverageTakeProfit = mastersOfBinanceAverageTakeProfit;
        ViewBag.MastersOfBinanceAverageTakeProfitPerTP = mastersOfBinanceAverageTakeProfitPerTP;
        ViewBag.MastersOfBinanceAverageLeverage = mastersOfBinanceAverageLeverage;
        ViewBag.MastersOfBinanceRisk = mastersOfBinanceRisk;
        ViewBag.MastersOfBinanceAverageTimeFrame = mastersOfBinanceAverageTimeFrame;
        ViewBag.MastersOfBinanceTags = mastersOfBinanceTradeTags;
        ViewBag.MastersOfBinanceAverageWin = mastersOfBinanceAverageWin;
        ViewBag.MastersOfBinanceLongWin = mastersOfBinanceLongWin;
        ViewBag.MastersOfBinanceShortWin = mastersOfBinanceShortWin;
        ViewBag.MastersOfBinanceTakeProfitDistribution = mastersOfBinanceTakeProfitDistribution;
        ViewBag.MastersOfBinanceNulls = mastersOfBinanceNulls;

        ViewBag.AlexFredmanCount = alexFredmanCount;
        ViewBag.AlexFredmanShorts = alexFredmanShorts;
        ViewBag.AlexFredmanLongs = alexFredmanLongs;
        ViewBag.AlexFredmanShortsCount = alexFredmanShortsCount;
        ViewBag.AlexFredmanLongsCount = alexFredmanLongsCount;
        ViewBag.AlexFredmanShortRatio = alexFredmanShortRatio;
        ViewBag.AlexFredmanLongRatio = alexFredmanLongRatio;
        ViewBag.AlexFredmanRRR = alexFredmanRRR;
        ViewBag.AlexFredmanRRRCatagory = alexFredmanRRRCatagory;
        ViewBag.AlexFredmanFrequency = alexFredmanFrequency;
        ViewBag.AlexFredmanAverageStoploss = alexFredmanAverageStoploss;
        ViewBag.AlexFredmanAverageTakeProfit = alexFredmanAverageTakeProfit;
        ViewBag.AlexFredmanAverageTakeProfitPerTP = alexFredmanAverageTakeProfitPerTP;
        ViewBag.AlexFredmanAverageLeverage = alexFredmanAverageLeverage;
        ViewBag.AlexFredmanRisk = alexFredmanRisk;
        ViewBag.AlexFredmanAverageTimeFrame = alexFredmanAverageTimeFrame;
        ViewBag.AlexFredmanTags = alexFredmanTradeTags;
        ViewBag.AlexFredmanAverageWin = alexFredmanAverageWin;
        ViewBag.AlexFredmanLongWin = alexFredmanLongWin;
        ViewBag.AlexFredmanShortWin = alexFredmanShortWin;
        ViewBag.AlexFredmanTakeProfitDistribution = alexFredmanTakeProfitDistribution;
        ViewBag.AlexFredmanNulls = alexFredmanNulls;

        ViewBag.CoinCoachCount = coinCoachCount;
        ViewBag.CoinCoachShorts = coinCoachShorts;
        ViewBag.CoinCoachLongs = coinCoachLongs;
        ViewBag.CoinCoachShortsCount = coinCoachShortsCount;
        ViewBag.CoinCoachLongsCount = coinCoachLongsCount;
        ViewBag.CoinCoachShortRatio = coinCoachShortRatio;
        ViewBag.CoinCoachLongRatio = coinCoachLongRatio;
        ViewBag.CoinCoachRRR = coinCoachRRR;
        ViewBag.CoinCoachRRRCatagory = coinCoachRRRCatagory;
        ViewBag.CoinCoachFrequency = coinCoachFrequency;
        ViewBag.CoinCoachAverageStoploss = coinCoachAverageStoploss;
        ViewBag.CoinCoachAverageTakeProfit = coinCoachAverageTakeProfit;
        ViewBag.CoinCoachAverageTakeProfitPerTP = coinCoachAverageTakeProfitPerTP;
        ViewBag.CoinCoachAverageLeverage = coinCoachAverageLeverage;
        ViewBag.CoinCoachRisk = coinCoachRisk;
        ViewBag.CoinCoachAverageTimeFrame = coinCoachAverageTimeFrame;
        ViewBag.CoinCoachTags = coinCoachTradeTags;
        ViewBag.CoinCoachAverageWin = coinCoachAverageWin;
        ViewBag.CoinCoachLongWin = coinCoachLongWin;
        ViewBag.CoinCoachShortWin = coinCoachShortWin;
        ViewBag.CoinCoachTakeProfitDistribution = coinCoachTakeProfitDistribution;
        ViewBag.CoinCoachNulls = coinCoachNulls;

        ViewBag.WolfXCount = wolfXCount;
        ViewBag.WolfXShorts = wolfXShorts;
        ViewBag.WolfXLongs = wolfXLongs;
        ViewBag.WolfXShortsCount = wolfXShortsCount;
        ViewBag.WolfXLongsCount = wolfXLongsCount;
        ViewBag.WolfXShortRatio = wolfXShortRatio;
        ViewBag.WolfXLongRatio = wolfXLongRatio;
        ViewBag.WolfXRRR = wolfXRRR;
        ViewBag.WolfXRRRCatagory = wolfXRRRCatagory;
        ViewBag.WolfXFrequency = wolfXFrequency;
        ViewBag.WolfXAverageStoploss = wolfXAverageStoploss;
        ViewBag.WolfXAverageTakeProfit = wolfXAverageTakeProfit;
        ViewBag.WolfXAverageTakeProfitPerTP = wolfXAverageTakeProfitPerTP;
        ViewBag.WolfXAverageLeverage = wolfXAverageLeverage;
        ViewBag.WolfXRisk = wolfXRisk;
        ViewBag.WolfXAverageTimeFrame = wolfXAverageTimeFrame;
        ViewBag.WolfXTags = wolfXTradeTags;
        ViewBag.WolfXAverageWin = wolfXAverageWin;
        ViewBag.WolfXLongWin = wolfXLongWin;
        ViewBag.WolfXShortWin = wolfXShortWin;
        ViewBag.WolfXTakeProfitDistribution = wolfXTakeProfitDistribution;
        ViewBag.WolfXNulls = wolfXNulls;

        ViewBag.FedRussianCount = fedRussianCount;
        ViewBag.FedRussianShorts = fedRussianShorts;
        ViewBag.FedRussianLongs = fedRussianLongs;
        ViewBag.FedRussianShortsCount = fedRussianShortsCount;
        ViewBag.FedRussianLongsCount = fedRussianLongsCount;
        ViewBag.FedRussianShortRatio = fedRussianShortRatio;
        ViewBag.FedRussianLongRatio = fedRussianLongRatio;
        ViewBag.FedRussianRRR = fedRussianRRR;
        ViewBag.FedRussianRRRCatagory = fedRussianRRRCatagory;
        ViewBag.FedRussianFrequency = fedRussianFrequency;
        ViewBag.FedRussianAverageStoploss = fedRussianAverageStoploss;
        ViewBag.FedRussianAverageTakeProfit = fedRussianAverageTakeProfit;
        ViewBag.FedRussianAverageTakeProfitPerTP = fedRussianAverageTakeProfitPerTP;
        ViewBag.FedRussianAverageLeverage = fedRussianAverageLeverage;
        ViewBag.FedRussianRisk = fedRussianRisk;
        ViewBag.FedRussianAverageTimeFrame = fedRussianAverageTimeFrame;
        ViewBag.FedRussianTags = fedRussianTradeTags;
        ViewBag.FedRussianAverageWin = fedRussianAverageWin;
        ViewBag.FedRussianLongWin = fedRussianLongWin;
        ViewBag.FedRussianShortWin = fedRussianShortWin;
        ViewBag.FedRussianTakeProfitDistribution = fedRussianTakeProfitDistribution;
        ViewBag.FedRussianNulls = fedRussianNulls;

        ViewBag.AndrewCount = andrewCount;
        ViewBag.AndrewShorts = andrewShorts;
        ViewBag.AndrewLongs = andrewLongs;
        ViewBag.AndrewShortsCount = andrewShortsCount;
        ViewBag.AndrewLongsCount = andrewLongsCount;
        ViewBag.AndrewShortRatio = andrewShortRatio;
        ViewBag.AndrewLongRatio = andrewLongRatio;
        ViewBag.AndrewRRR = andrewRRR;
        ViewBag.AndrewRRRCatagory = andrewRRRCatagory;
        ViewBag.AndrewFrequency = andrewFrequency;
        ViewBag.AndrewAverageStoploss = andrewAverageStoploss;
        ViewBag.AndrewAverageTakeProfit = andrewAverageTakeProfit;
        ViewBag.AndrewAverageTakeProfitPerTP = andrewAverageTakeProfitPerTP;
        ViewBag.AndrewAverageLeverage = andrewAverageLeverage;
        ViewBag.AndrewRisk = andrewRisk;
        ViewBag.AndrewAverageTimeFrame = andrewAverageTimeFrame;
        ViewBag.AndrewTags = andrewTradeTags;
        ViewBag.AndrewAverageWin = andrewAverageWin;
        ViewBag.AndrewLongWin = andrewLongWin;
        ViewBag.AndrewShortWin = andrewShortWin;
        ViewBag.AndrewTakeProfitDistribution = andrewTakeProfitDistribution;
        ViewBag.AndrewNulls = andrewNulls;

        ViewBag.CicCount = cicCount;
        ViewBag.CicShorts = cicShorts;
        ViewBag.CicLongs = cicLongs;
        ViewBag.CicShortsCount = cicShortsCount;
        ViewBag.CicLongsCount = cicLongsCount;
        ViewBag.CicShortRatio = cicShortRatio;
        ViewBag.CicLongRatio = cicLongRatio;
        ViewBag.CicRRR = cicRRR;
        ViewBag.CicRRRCatagory = cicRRRCatagory;
        ViewBag.CicFrequency = cicFrequency;
        ViewBag.CicAverageStoploss = cicAverageStoploss;
        ViewBag.CicAverageTakeProfit = cicAverageTakeProfit;
        ViewBag.CicAverageTakeProfitPerTP = cicAverageTakeProfitPerTP;
        ViewBag.CicAverageLeverage = cicAverageLeverage;
        ViewBag.CicRisk = cicRisk;
        ViewBag.CicAverageTimeFrame = cicAverageTimeFrame;
        ViewBag.CicTags = cicTradeTags;
        ViewBag.CicAverageWin = cicAverageWin;
        ViewBag.CicLongWin = cicLongWin;
        ViewBag.CicShortWin = cicShortWin;
        ViewBag.CicTakeProfitDistribution = cicTakeProfitDistribution;
        ViewBag.CicNulls = cicNulls;

        ViewBag.AmanCount = amanCount;
        ViewBag.AmanShorts = amanShorts;
        ViewBag.AmanLongs = amanLongs;
        ViewBag.AmanShortsCount = amanShortsCount;
        ViewBag.AmanLongsCount = amanLongsCount;
        ViewBag.AmanShortRatio = amanShortRatio;
        ViewBag.AmanLongRatio = amanLongRatio;
        ViewBag.AmanRRR = amanRRR;
        ViewBag.AmanRRRCatagory = amanRRRCatagory;
        ViewBag.AmanFrequency = amanFrequency;
        ViewBag.AmanAverageStoploss = amanAverageStoploss;
        ViewBag.AmanAverageTakeProfit = amanAverageTakeProfit;
        ViewBag.AmanAverageTakeProfitPerTP = amanAverageTakeProfitPerTP;
        ViewBag.AmanAverageLeverage = amanAverageLeverage;
        ViewBag.AmanRisk = amanRisk;
        ViewBag.AmanAverageTimeFrame = amanAverageTimeFrame;
        ViewBag.AmanTags = amanTradeTags;
        ViewBag.AmanAverageWin = amanAverageWin;
        ViewBag.AmanLongWin = amanLongWin;
        ViewBag.AmanShortWin = amanShortWin;
        ViewBag.AmanTakeProfitDistribution = amanTakeProfitDistribution;
        ViewBag.AmanNulls = amanNulls;

        return View(ViewBag);
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
