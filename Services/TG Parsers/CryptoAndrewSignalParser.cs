using System.Globalization;
using System.Text.RegularExpressions;
using AutoSignals.Models;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

public static class CryptoAndrewSignalParser
{
    public static Signal? Parse(
        string message,
        float stoplossPercent,
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
        var symbolPattern = @"Trading Pair: (?<pair>[A-Za-z0-9]+/[A-Za-z0-9]+)";
        var initialEntryPattern = @"Entry: ([\d.]+)";
        var entryPattern = @"Averaging \(DCA\): ([\d.]+)?";
        var takeProfitPattern = @"Targets:\s*([\d.]+)\s*([\d.]+)\s*([\d.]+)\s*([\d.]+)\s*([\d.]+)";
        var positionTypePattern = @"OPEN — (LONG|SHORT)";
        var stopLossPattern = @"(?:Stop loss|SL): ([\d.]+)?"; // Optional stop-loss
        var spotPattern = @"\(SPOT\)";
        var marketPattern = @"\(Market order\)";

        try
        {
            var pairMatch = Regex.Match(message, symbolPattern);
            if (!pairMatch.Success)
                throw new ArgumentException("Could not parse the trading pair from the message.");

            var initialEntryMatch = Regex.Match(message, initialEntryPattern);
            if (!initialEntryMatch.Success)
                throw new ArgumentException("Initial entry not found in message");

            var entryMatch = Regex.Match(message, entryPattern);
            if (!entryMatch.Success)
                throw new ArgumentException("Averaging entry not found in message");

            var takeProfitMatch = Regex.Match(message, takeProfitPattern, RegexOptions.Multiline);
            if (!takeProfitMatch.Success)
                throw new ArgumentException("Take profits not found in message");

            var stopLossMatch = Regex.Match(message, stopLossPattern);
            var spotMatch = Regex.Match(message, spotPattern);
            var positionTypeMatch = Regex.Match(message, positionTypePattern);
            var marketMatch = Regex.Match(message, marketPattern);

            if (!positionTypeMatch.Success)
                throw new ArgumentException("Position type not found in message");

            // Extract values
            var pair = pairMatch.Groups["pair"].Value;
            var symbol = pair.Replace("/", "");

            var initialEntry = float.Parse(initialEntryMatch.Groups[1].Value, CultureInfo.InvariantCulture);

            var entryValue = float.TryParse(entryMatch.Groups[1].Value, out var avgEntry) ? avgEntry : initialEntry;
            var takeProfit = takeProfitMatch.Groups.Values.Skip(1).Select(g => float.Parse(g.Value, CultureInfo.InvariantCulture)).ToArray();
            var side = positionTypeMatch.Groups[1].Value;

            // Handle stop-loss: use default 10% if not found
            var stopLoss = spotMatch.Success
                ? 0
                : stopLossMatch.Success && float.TryParse(stopLossMatch.Groups[1].Value, out var slValue)
                    ? slValue
                    : initialEntry * (1 - (stoplossPercent / 100));

            if (side.ToLower() == "short")
            {
                stopLoss = spotMatch.Success
                    ? 0
                    : stopLossMatch.Success && float.TryParse(stopLossMatch.Groups[1].Value, out slValue)
                        ? slValue
                        : initialEntry * (1 + (stoplossPercent / 100));
            }

            // Check for duplicates
            if (lastThreeEntries.TryGetValue(symbol, out var queue))
            {
                if (queue.Any(s => s.Entry == initialEntry && s.Stoploss == stopLoss))
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

            // Create the new signal
            var newSignal = new Signal
            {
                Symbol = symbol.ToUpper(),
                Side = side.ToLower(),
                Leverage = 0, // Assuming leverage is not provided in the message
                Entry = initialEntry,
                Stoploss = stopLoss,
                TakeProfits = string.Join(",", takeProfit.Select(tp => tp.ToString(CultureInfo.InvariantCulture))),
                Provider = "CryptoAndrew",
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
            logger.LogError($"Error extracting trade info: {ex.Message} - CryptoAndrew");
            return null;
        }
    }
}
