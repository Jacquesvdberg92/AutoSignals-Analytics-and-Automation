using System.Globalization;
using System.Text.RegularExpressions;
using AutoSignals.Models;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

public static class CoinCoachSignalParser
{
    public static Signal? Parse(
        string message,
        ILogger logger,
        ConcurrentDictionary<string, Queue<Signal>> lastThreeEntries)
    {
        try
        {
            var takeProfits = new Dictionary<int, decimal>();

            // Parse the symbol (e.g., #HIFIUSDT)
            var symbolPattern = @"#(?<symbol>[A-Za-z0-9]+USDT)";
            var symbolMatch = Regex.Match(message, symbolPattern);
            if (!symbolMatch.Success)
                throw new ArgumentException("Could not parse the symbol from the message.");

            var symbol = symbolMatch.Groups["symbol"].Value;

            // Parse the signal direction (BUY or SELL)
            var directionPattern = @"\b(BUY|SELL)\b";
            var directionMatch = Regex.Match(message, directionPattern, RegexOptions.IgnoreCase);
            if (!directionMatch.Success)
                throw new ArgumentException("Could not parse the direction from the message.");

            var side = directionMatch.Value.Equals("BUY", StringComparison.OrdinalIgnoreCase) ? "long" : "short";

            // Parse the leverage (e.g., Cross 10.00X)
            var leveragePattern = @"Leverage:\s*Cross\s*\(?(?<leverage>\d+(\.\d+)?)X\)?";
            var leverageMatch = Regex.Match(message, leveragePattern);
            if (!leverageMatch.Success)
                throw new ArgumentException("Could not parse the leverage from the message.");

            var leverage = decimal.Parse(leverageMatch.Groups["leverage"].Value, CultureInfo.InvariantCulture);

            // Parse the entry range (e.g., 0.5480-0.5623)
            var entryPattern = @"(BUY|SELL)\s*:\s*(?<entry1>\d+(\.\d+)?)\s*-\s*(?<entry2>\d+(\.\d+)?)";
            var entryMatch = Regex.Match(message, entryPattern, RegexOptions.IgnoreCase);
            if (!entryMatch.Success)
                throw new ArgumentException("Could not parse the entry range from the message.");

            var entry = (float.Parse(entryMatch.Groups["entry1"].Value, CultureInfo.InvariantCulture) +
                         float.Parse(entryMatch.Groups["entry2"].Value, CultureInfo.InvariantCulture)) / 2;

            // Parse the stop-loss value (e.g., 0.5939)
            var stopPattern = @"STOPLOSS:\s*(?<stoploss>\d+(\.\d+)?)";
            var stopMatch = Regex.Match(message, stopPattern, RegexOptions.IgnoreCase);
            if (!stopMatch.Success)
                throw new ArgumentException("Could not parse the stop-loss from the message.");

            var stoploss = float.Parse(stopMatch.Groups["stoploss"].Value, CultureInfo.InvariantCulture);

            // Parse the take profits (multiple values)
            var targetPattern = @"\d+\)\s*(?<target>\d+(\.\d+)?)[+]?";
            var targetMatches = Regex.Matches(message, targetPattern);
            int i = 1;
            foreach (Match match in targetMatches)
            {
                if (match.Success)
                {
                    takeProfits[i++] = decimal.Parse(match.Groups["target"].Value, CultureInfo.InvariantCulture);
                }
            }

            if (takeProfits.Count == 0)
                throw new ArgumentException("Could not parse the take-profit targets from the message.");

            // Concatenate take profit values as a string (comma separated)
            var takeProfitsString = string.Join(",", takeProfits.Values.Select(tp => tp.ToString(CultureInfo.InvariantCulture).Replace(',', '.')));

            // Create the new signal
            var newSignal = new Signal
            {
                Symbol = symbol.ToUpper(),
                Side = side,
                Leverage = (int)leverage,
                Entry = (float)entry,
                Stoploss = (float)stoploss,
                TakeProfits = takeProfitsString,
                Provider = "Coin Coach",
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

            return newSignal;
        }
        catch (Exception ex)
        {
            logger.LogError($"Error extracting trade info: {ex.Message} - Coin Coach");
            return null;
        }
    }
}
