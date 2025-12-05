using System.Globalization;
using System.Text.RegularExpressions;
using AutoSignals.Models;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

public static class BinanceMasterSignalParser
{
    public static Signal? Parse(
        string message,
        ILogger logger,
        ConcurrentDictionary<string, Queue<Signal>> lastThreeEntries)
    {
        try
        {
            var takeProfits = new Dictionary<int, decimal>();

            // Parse the symbol, signal type (Long/Short), and leverage
            var symbolPattern = @"#(?<symbol>[A-Za-z0-9]+\/[A-Za-z]+)";
            var symbolMatch = Regex.Match(message, symbolPattern);
            if (!symbolMatch.Success)
                throw new ArgumentException("Could not parse the symbol from the message.");

            var symbol = symbolMatch.Groups["symbol"].Value.Replace("/", "");

            // Parse the signal type (Long/Short)
            var typePattern = @"Signal Type:\s*Regular\s*\((?<type>Long|Short)";
            var typeMatch = Regex.Match(message, typePattern);
            if (!typeMatch.Success)
                throw new ArgumentException("Could not parse the signal type from the message.");

            var side = typeMatch.Groups["type"].Value.ToLower(); // "long" or "short"

            // Parse the leverage
            var leveragePattern = @"Leverage:\s*Cross\s*\((?<leverage>\d+)х\)";
            var leverageMatch = Regex.Match(message, leveragePattern);
            if (!leverageMatch.Success)
                throw new ArgumentException("Could not parse the leverage from the message.");

            var leverage = int.Parse(leverageMatch.Groups["leverage"].Value, CultureInfo.InvariantCulture);

            // Parse the entry target
            var entryPattern = @"Entry Targets:\s*(?<entry>\d+(\.\d+)?)";
            var entryMatch = Regex.Match(message, entryPattern);
            if (!entryMatch.Success)
                throw new ArgumentException("Could not parse the entry price from the message.");

            var entry = decimal.Parse(entryMatch.Groups["entry"].Value, CultureInfo.InvariantCulture);

            // Parse the take profits (multiple values)
            var takeProfitPattern = @"\d+\)\s*(?<takeProfit>\d+(\.\d+)?)";
            var takeProfitMatches = Regex.Matches(message, takeProfitPattern);
            int i = 1;
            foreach (Match match in takeProfitMatches)
            {
                if (match.Success)
                {
                    if (match.Groups["takeProfit"].Value == "🚀🚀🚀")
                    {
                        takeProfits[i++] = 0; // Special handling for rocket emojis.
                    }
                    else
                    {
                        takeProfits[i++] = decimal.Parse(match.Groups["takeProfit"].Value, CultureInfo.InvariantCulture);
                    }
                }
            }

            if (takeProfits.Count == 0)
                throw new ArgumentException("Could not parse the take-profit targets from the message.");

            // Parse the stop targets (percentage range)
            var stopPattern = @"Stop Targets:\s*(?<stopPercent>\d+)-(?<stopMax>\d+)%";
            var stopMatch = Regex.Match(message, stopPattern);
            if (!stopMatch.Success)
                throw new ArgumentException("Could not parse the stop targets from the message.");

            var stoplossPercent = decimal.Parse(stopMatch.Groups["stopPercent"].Value, CultureInfo.InvariantCulture);

            // Calculate stop-loss value based on the entry price and percentage
            var stoploss = side == "long"
                ? entry - (entry * stoplossPercent / 100)
                : entry + (entry * stoplossPercent / 100);

            // Create the new signal
            var newSignal = new Signal
            {
                Symbol = symbol.ToUpper(),
                Side = side,
                Leverage = (int)leverage,
                Entry = (float)entry,
                Stoploss = (float)stoploss,
                TakeProfits = string.Join(",", takeProfits.Values.Select(tp => tp == 0 ? "🚀" : tp.ToString(CultureInfo.InvariantCulture))),
                Provider = "BinanceMaster",
                Time = DateTime.Now
            };

            // Check for duplicates
            if (lastThreeEntries.TryGetValue(symbol, out var queue))
            {
                if (queue.Any(s => s.Entry == newSignal.Entry && s.Stoploss == newSignal.Stoploss))
                {
                    logger.LogWarning($"Duplicate signal detected for symbol {symbol}. Ignoring.");
                    return null;
                }
            }
            else
            {
                queue = new Queue<Signal>();
                lastThreeEntries[symbol] = queue;
            }

            // Save the new signal
            queue.Enqueue(newSignal);
            if (queue.Count > 3)
            {
                queue.Dequeue();
            }

            // Return the parsed signal data
            return newSignal;
        }
        catch (Exception ex)
        {
            logger.LogError($"Error extracting trade info: {ex.Message} - BinanceMaster");
            return null;
        }
    }
}
