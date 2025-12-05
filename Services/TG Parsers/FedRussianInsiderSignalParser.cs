using System.Globalization;
using System.Text.RegularExpressions;
using AutoSignals.Models;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

public static class FedRussianInsiderSignalParser
{
    public static Signal? Parse(
        string message,
        ILogger logger,
        ConcurrentDictionary<string, Queue<Signal>> lastThreeEntries)
    {
        try
        {
            var takeProfits = new Dictionary<int, decimal>();

            // Trade ID
            var tradeIdPattern = @"#(?<tradeId>[A-Za-z0-9]+)";
            var tradeIdMatch = Regex.Match(message, tradeIdPattern);
            //if (!tradeIdMatch.Success)
            //    throw new ArgumentException("Could not parse the Trade ID from the message.");

            // Pair (e.g., $ETH/USDT)
            var pairPattern = @"\$(?<pair>[A-Za-z0-9]+\/USDT)";
            var pairMatch = Regex.Match(message, pairPattern);
            if (!pairMatch.Success)
                throw new ArgumentException("Could not parse the pair from the message.");
            var symbol = pairMatch.Groups["pair"].Value;
            var pair = symbol.Replace("/", ""); // e.g., ETHUSDT

            // Direction: SHORT or LONG
            var directionPattern = @"Direction\s*:\s*(?<dir>Short|Long)|⬇️SHORT|⬆️LONG";
            var directionMatch = Regex.Match(message, directionPattern, RegexOptions.IgnoreCase);
            if (!directionMatch.Success)
                throw new ArgumentException("Could not parse the direction from the message.");
            var side = directionMatch.Value.ToLower().Contains("short") ? "short" : "long";

            // Leverage
            var leveragePattern = @"Leverage\s*:\s*(?<leverage1>\d+)(\s*-\s*(?<leverage2>\d+))?X";
            var leverageMatch = Regex.Match(message, leveragePattern);
            if (!leverageMatch.Success)
                throw new ArgumentException("Could not parse the leverage from the message.");

            var leverage1 = int.Parse(leverageMatch.Groups["leverage1"].Value);
            var leverage2 = leverageMatch.Groups["leverage2"].Success ? int.Parse(leverageMatch.Groups["leverage2"].Value) : leverage1;
            var leverage = Math.Max(leverage1, leverage2);

            // Entry (single or range)
            var entryPattern = @"Entry\s*:\s*(?<entry1>\d+(\.\d+)?)(\s*-\s*(?<entry2>\d+(\.\d+)?))?";
            var entryMatch = Regex.Match(message, entryPattern, RegexOptions.IgnoreCase);
            if (!entryMatch.Success)
                throw new ArgumentException("Could not parse the entry from the message.");
            var entry = entryMatch.Groups["entry2"].Success
                ? decimal.Parse(entryMatch.Groups["entry2"].Value, CultureInfo.InvariantCulture)
                : decimal.Parse(entryMatch.Groups["entry1"].Value, CultureInfo.InvariantCulture);

            // Stop-loss (SL or STOP LOSS)
            var stopPattern = @"(?:SL|STOP\s*LOSS)\s*:\s*(?<stoploss>\d+(\.\d+)?)";
            var stopMatch = Regex.Match(message, stopPattern, RegexOptions.IgnoreCase);
            if (!stopMatch.Success)
                throw new ArgumentException("Could not parse the stop-loss from the message.");
            var stoploss = decimal.Parse(stopMatch.Groups["stoploss"].Value, CultureInfo.InvariantCulture);

            // Look for full "Targets: x - y - z" pattern
            var inlineTargetPattern = @"Targets\s*:\s*(?<targets>(\d+(\.\d+)?\s*-\s*)+\d+(\.\d+)?)";
            var inlineMatch = Regex.Match(message, inlineTargetPattern, RegexOptions.IgnoreCase);

            if (inlineMatch.Success)
            {
                var targetValues = inlineMatch.Groups["targets"].Value
                    .Split('-', StringSplitOptions.RemoveEmptyEntries)
                    .Select(t => decimal.Parse(t.Trim(), CultureInfo.InvariantCulture))
                    .ToList();

                int i = 1;
                foreach (var tp in targetValues)
                    takeProfits[i++] = tp;
            }
            else
            {
                // Fallback: parse individual "Target x - value" lines
                var targetPattern = @"Target\s*\d+\s*[-:]?\s*(?<target>\d+(\.\d+)?)";
                var targetMatches = Regex.Matches(message, targetPattern, RegexOptions.IgnoreCase);
                int i = 1;
                foreach (Match match in targetMatches)
                {
                    if (match.Success)
                    {
                        takeProfits[i++] = decimal.Parse(match.Groups["target"].Value, CultureInfo.InvariantCulture);
                    }
                }
            }

            if (takeProfits.Count == 0)
                throw new ArgumentException("Could not parse the take-profit targets from the message.");

            // Risk
            var riskPattern = @"RISK\s*:\s*(?<risk>[A-Za-z\/]+)";
            var riskMatch = Regex.Match(message, riskPattern, RegexOptions.IgnoreCase);
            if (!riskMatch.Success)
                throw new ArgumentException("Could not parse the risk level from the message.");

            // Join TP values
            var takeProfitsString = string.Join(",", takeProfits.Values.Select(tp => tp.ToString(CultureInfo.InvariantCulture).Replace(',', '.')));

            // Create signal
            var newSignal = new Signal
            {
                Symbol = pair.ToUpper(),
                Side = side,
                Leverage = leverage,
                Entry = (float)entry,
                Stoploss = (float)stoploss,
                TakeProfits = takeProfitsString,
                Provider = "Fed Russian Insider",
                Time = DateTime.Now
            };

            // Deduplication check
            if (lastThreeEntries.TryGetValue(pair, out var queue))
            {
                if (queue.Any(s => s.Entry == newSignal.Entry && s.Stoploss == newSignal.Stoploss))
                {
                    logger.LogWarning($"Duplicate signal detected for symbol {pair}. Ignoring.");
                    return null;
                }
            }
            else
            {
                queue = new Queue<Signal>();
                lastThreeEntries[pair] = queue;
            }

            // Save new signal
            queue.Enqueue(newSignal);
            if (queue.Count > 3)
            {
                queue.Dequeue();
            }

            return newSignal;
        }
        catch (Exception ex)
        {
            logger.LogError($"Error extracting trade info: {ex.Message} - Fed Russian Insider");
            return null;
        }
    }
}
