using System.Globalization;
using System.Text.RegularExpressions;
using AutoSignals.Models;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

public static class Scalping300SignalParser
{
    public static Signal? Parse(
        string message,
        ILogger logger,
        ConcurrentDictionary<string, Queue<Signal>> lastThreeEntries)
    {
        try
        {
            var takeProfits = new Dictionary<int, decimal>();

            // Parse the symbol (e.g., DODOUSDT)
            var symbolPattern = @"[A-Z]+USDT";
            var symbolMatch = Regex.Match(message, symbolPattern);
            if (symbolMatch.Success)
            {
                var symbol = symbolMatch.Value;

                // Parse the signal direction (Long/Short)
                var directionPattern = @"𝓓𝓲𝓻𝓮𝓬𝓽𝓲𝓸𝓷\s*:\s*(LONG|SHORT)";
                var directionMatch = Regex.Match(message, directionPattern);
                if (!directionMatch.Success)
                    throw new ArgumentException("Could not parse the direction from the message.");
                var side = directionMatch.Groups[1].Value.ToLower(); // 'long' or 'short'

                // Parse the leverage (e.g., Cross 20x)
                var leveragePattern = @"Leverage\s*:\s*Cross\s*(?<leverage>\d+(\.\d+)?)x";
                var leverageMatch = Regex.Match(message, leveragePattern);
                if (!leverageMatch.Success)
                    throw new ArgumentException("Could not parse the leverage from the message.");
                var leverage = decimal.Parse(leverageMatch.Groups["leverage"].Value, CultureInfo.InvariantCulture);

                // Parse the entry range (e.g., 0.1331 - 0.1327)
                var entryPattern = @"Entry\s*:\s*(?<entry1>\d+(\.\d+)?)\s*-\s*(?<entry2>\d+(\.\d+)?)";
                var entryMatch = Regex.Match(message, entryPattern);
                if (!entryMatch.Success)
                    throw new ArgumentException("Could not parse the entry range from the message.");
                var entry = (float.Parse(entryMatch.Groups["entry1"].Value, CultureInfo.InvariantCulture) +
                             float.Parse(entryMatch.Groups["entry2"].Value, CultureInfo.InvariantCulture)) / 2;

                // Parse the stop-loss value (e.g., 0.122451)
                var stopPattern = @"Stoploss\s*:\s*(?<stoploss>\d+(\.\d+)?)";
                var stopMatch = Regex.Match(message, stopPattern);
                if (!stopMatch.Success)
                    throw new ArgumentException("Could not parse the stoploss from the message.");
                var stoploss = float.Parse(stopMatch.Groups["stoploss"].Value, CultureInfo.InvariantCulture);

                // Parse the targets (multiple values)
                var targetPattern = @"Target\s*\d+\s*-\s*(?<target>\d+(\.\d+)?)";
                var targetMatches = Regex.Matches(message, targetPattern);
                int i = 1;
                foreach (Match match in targetMatches)
                {
                    if (match.Success)
                    {
                        takeProfits[i++] = decimal.Parse(match.Groups["target"].Value, CultureInfo.InvariantCulture);
                    }
                }

                // Concatenate take profit values as a string (comma separated) with period as the decimal separator
                var takeProfitsString = string.Join(",", takeProfits.Values.Select(tp => tp.ToString(CultureInfo.InvariantCulture).Replace(',', '.')));

                if (takeProfits.Count == 0)
                    throw new ArgumentException("Could not parse the take-profit targets from the message.");

                // Create the new signal
                var newSignal = new Signal
                {
                    Symbol = symbol.ToUpper(),
                    Side = side,
                    Leverage = (int)leverage,
                    Entry = (float)entry,
                    Stoploss = (float)stoploss,
                    TakeProfits = takeProfitsString,
                    Provider = "Scalping300",
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
            else
            {
                throw new ArgumentException("Could not parse the symbol from the message.");
            }
        }
        catch (Exception ex)
        {
            logger.LogError($"Error extracting trade info: {ex.Message} - Scalping300");
            return null;
        }
    }
}
