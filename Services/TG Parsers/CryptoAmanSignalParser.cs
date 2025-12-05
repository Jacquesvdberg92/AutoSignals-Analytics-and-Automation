using System.Globalization;
using System.Text.RegularExpressions;
using AutoSignals.Models;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

public static class CryptoAmanSignalParser
{
    public static Signal? Parse(
        string message,
        ILogger logger,
        ConcurrentDictionary<string, Queue<Signal>> lastThreeEntries)
    {
        if (string.IsNullOrEmpty(message))
        {
            logger.LogWarning("Not a valid signal from start");
            return null;
        }

        // Remove consecutive spaces
        message = Regex.Replace(message, @"\s{2,}", " ");

        // Define the regular expressions
        var symbolPattern = @"TRADE -\s*(?<pair>[A-Za-z0-9\s/]+)";
        var entryPattern = @"(Buy Zone|Short Zone) -\s*([\d.,]+)\$";
        var takeProfitPattern = @"(?<=\d\.\s*)\d+\.\d+(?=\$)";
        var positionTypePattern = @"Type -\s*(LONG|long|Long|SHORT|Short|short)";
        var stopLossPattern = @"Stop loss\s*([\d.,]+)\$";
        var leveragePattern = @"Leverage-\s*(\d+)X to (\d+)X";

        try
        {
            var pairMatch = Regex.Match(message, symbolPattern);
            if (!pairMatch.Success)
                throw new ArgumentException("Could not parse the trading pair from the message.");

            var entryMatch = Regex.Match(message, entryPattern);
            if (!entryMatch.Success)
                throw new ArgumentException("Entry not found in message");

            var takeProfitMatches = Regex.Matches(message, takeProfitPattern);
            if (takeProfitMatches.Count == 0)
                throw new ArgumentException("Take profits not found in message");

            var stopLossMatch = Regex.Match(message, stopLossPattern);
            if (!stopLossMatch.Success)
                throw new ArgumentException("Stop loss not found in message");

            var positionTypeMatch = Regex.Match(message, positionTypePattern);
            if (!positionTypeMatch.Success)
                throw new ArgumentException("Position type not found in message");

            var leverageMatch = Regex.Match(message, leveragePattern);
            if (!leverageMatch.Success)
                throw new ArgumentException("Leverage not found in message");

            // Extract values
            var pair = pairMatch.Groups["pair"].Value.Replace(" ", "").Replace("/", "");
            var entry = float.Parse(entryMatch.Groups[2].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
            var takeProfits = string.Join(",", takeProfitMatches.Cast<Match>().Select(m => m.Value));
            var stopLoss = float.Parse(stopLossMatch.Groups[1].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
            var side = positionTypeMatch.Groups[1].Value.ToLower();
            var leverage = int.Parse(leverageMatch.Groups[1].Value, CultureInfo.InvariantCulture); // Use the lower leverage value

            // Check for duplicates
            if (lastThreeEntries.TryGetValue(pair, out var queue))
            {
                if (queue.Any(s => s.Entry == entry && s.Stoploss == stopLoss))
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

            // Create the new signal
            var newSignal = new Signal
            {
                Symbol = pair.ToUpper(),
                Side = side,
                Leverage = leverage,
                Entry = entry,
                Stoploss = stopLoss,
                TakeProfits = takeProfits,
                Provider = "CryptoAman",
                Time = DateTime.Now
            };

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
            logger.LogError($"Error extracting trade info: {ex.Message} - Crypto Aman");
            return null;
        }
    }
}
