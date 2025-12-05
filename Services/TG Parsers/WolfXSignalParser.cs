using System.Globalization;
using System.Text.RegularExpressions;
using AutoSignals.Models;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

public static class WolfXSignalParser
{
    public static Signal? Parse(
        string message,
        ILogger logger,
        ConcurrentDictionary<string, Queue<Signal>> lastThreeEntries)
    {
        try
        {
            var takeProfits = new Dictionary<int, decimal>();

            // Updated symbol pattern to account for possible emojis or spaces before/after the symbol
            var symbolPattern = @"(?<symbol>[A-Za-z0-9]+)\/USDT";
            var symbolMatch = Regex.Match(message, symbolPattern);
            if (!symbolMatch.Success)
                throw new ArgumentException("Could not parse the symbol from the message.");
            var symbol = symbolMatch.Groups["symbol"].Value + "USDT";

            // Parse the signal direction (BUY for long, SELL for short)
            var directionPattern = @"(BUY|SELL)";
            var directionMatch = Regex.Match(message, directionPattern);
            if (!directionMatch.Success)
                throw new ArgumentException("Could not parse the direction from the message.");
            var side = directionMatch.Value.Contains("BUY") ? "long" : "short";

            // Parse the leverage (e.g., Leverage 10x)
            var leveragePattern = @"Leverage\s*(?<leverage>\d+)x";
            var leverageMatch = Regex.Match(message, leveragePattern);
            if (!leverageMatch.Success)
                throw new ArgumentException("Could not parse the leverage from the message.");
            var leverage = int.Parse(leverageMatch.Groups["leverage"].Value, CultureInfo.InvariantCulture);

            // Adjusted entry parsing for both long and short signals
            var entryPattern = side == "long"
                ? @"Enter\s*(above|at)\s*:\s*(?<entry>\d+(\.\d+)?)"
                : @"Enter\s*(below|at)\s*:\s*(?<entry>\d+(\.\d+)?)";
            var entryMatch = Regex.Match(message, entryPattern);
            if (!entryMatch.Success)
                throw new ArgumentException("Could not parse the entry range from the message.");
            var entry = decimal.Parse(entryMatch.Groups["entry"].Value, CultureInfo.InvariantCulture);

            // Parse the stop-loss value (e.g., SL 0.6112)
            var stopPattern = @"SL\s*(?<stoploss>\d+(\.\d+)?)";
            var stopMatch = Regex.Match(message, stopPattern);
            if (!stopMatch.Success)
                throw new ArgumentException("Could not parse the stop-loss from the message.");
            var stoploss = decimal.Parse(stopMatch.Groups["stoploss"].Value, CultureInfo.InvariantCulture);

            // Parse the take-profits (e.g., TP1 0.6300, TP2 0.6350, etc.)
            var targetPattern = @"TP\d+\s*(?<target>\d+(\.\d+)?)";
            var targetMatches = Regex.Matches(message, targetPattern);
            int i = 1;
            foreach (Match match in targetMatches)
            {
                if (match.Success)
                {
                    takeProfits[i++] = decimal.Parse(match.Groups["target"].Value, CultureInfo.InvariantCulture);
                }
            }

            // Validate that at least one take-profit value was found
            if (takeProfits.Count == 0)
                throw new ArgumentException("Could not parse the take-profit targets from the message.");

            // Concatenate take profit values as a string (comma-separated)
            var takeProfitsString = string.Join(",", takeProfits.Values.Select(tp => tp.ToString(CultureInfo.InvariantCulture).Replace(',', '.')));

            // Create the new signal
            var newSignal = new Signal
            {
                Symbol = symbol.ToUpper(),
                Side = side,
                Leverage = leverage,
                Entry = (float)entry,
                Stoploss = (float)stoploss,
                TakeProfits = takeProfitsString,
                Provider = "WolfX",
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
            logger.LogError($"Error extracting trade info: {ex.Message} - WolfX");
            return null;
        }
    }
}
