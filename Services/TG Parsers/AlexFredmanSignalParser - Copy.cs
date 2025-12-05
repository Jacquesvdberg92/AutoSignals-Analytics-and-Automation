using System.Globalization;
using System.Text.RegularExpressions;
using AutoSignals.Models;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;
using System.Collections.Concurrent;

public static class AlexFredmanSignalParser
{
    public static Signal? Parse(string message, float stoplossPercent, ILogger logger, ConcurrentDictionary<string, Queue<Signal>> lastThreeEntries)
    {
        try
        {
            var takeProfits = new Dictionary<int, decimal>();

            // Symbol extraction
            var symbolPattern = @"🪙\s*(?<symbol>[A-Za-z0-9]+(?:\/[A-Za-z]+)?)";
            var symbolMatch = Regex.Match(message, symbolPattern);
            if (!symbolMatch.Success)
                throw new ArgumentException("Could not parse the symbol from the message.");

            var symbol = symbolMatch.Groups["symbol"].Value.Replace("/", "");

            // Type and Entry range parsing
            var entryPattern = @"(LONG|SHORT)\s*:\s*(?<entry>\d+(\.\d+)?(\s*-\s*\d+(\.\d+)?)?)";
            var entryMatch = Regex.Match(message, entryPattern);
            if (!entryMatch.Success)
                throw new ArgumentException("Could not parse the entry price from the message.");

            var side = entryMatch.Value.Contains("LONG") ? "long" : "short";

            var entryValue = entryMatch.Groups["entry"].Value;
            float entry;
            if (entryValue.Contains("-"))
            {
                var entryRange = entryValue.Split('-').Select(v => float.Parse(v.Trim(), CultureInfo.InvariantCulture)).ToArray();
                entry = entryRange[1]; // Use the upper value
            }
            else
            {
                entry = float.Parse(entryValue, CultureInfo.InvariantCulture);
            }

            // Leverage
            var leveragePattern = @"Cross\s*(?<leverage>\d+(\.\d+)?)[A-Za-zА-Яа-я]*x";
            var leverageMatch = Regex.Match(message, leveragePattern);
            if (!leverageMatch.Success)
                throw new ArgumentException("Could not parse the leverage from the message.");

            var leverage = decimal.Parse(leverageMatch.Groups["leverage"].Value, CultureInfo.InvariantCulture);

            // Take Profits
            var takeProfitPattern = @"\d+\)\s*(?<takeProfit>\d+(\.\d+)?)(\+)?";
            var takeProfitMatches = Regex.Matches(message, takeProfitPattern);
            int i = 1;
            foreach (Match match in takeProfitMatches)
            {
                if (match.Success)
                {
                    var takeProfitValue = match.Groups["takeProfit"].Value.Replace(",", "");
                    takeProfits[i++] = decimal.Parse(takeProfitValue, CultureInfo.InvariantCulture);
                }
            }

            if (takeProfits.Count == 0)
                throw new ArgumentException("Could not parse the take-profit targets from the message.");

            // Stop-loss: extract max % from range "5%-10%"
            var stopLossPattern = @"SL:\s*(?<range>\d+%-\d+%)";
            var stopLossMatch = Regex.Match(message, stopLossPattern);
            float stopLossPercent = stoplossPercent; // default
            if (stopLossMatch.Success)
            {
                var range = stopLossMatch.Groups["range"].Value.Replace("%", "");
                var parts = range.Split('-');
                if (parts.Length == 2)
                    stopLossPercent = float.Parse(parts[1], CultureInfo.InvariantCulture); // use max %
            }

            float stoplossValue = side == "long"
                ? entry - (entry * stopLossPercent / 100)
                : entry + (entry * stopLossPercent / 100);

            var takeProfitsString = string.Join(",", takeProfits.Values.Select(tp => tp.ToString(CultureInfo.InvariantCulture)));

            var newSignal = new Signal
            {
                Symbol = symbol.ToUpper(),
                Side = side,
                Leverage = (int)leverage,
                Entry = entry,
                Stoploss = stoplossValue,
                TakeProfits = takeProfitsString,
                Provider = "AlexFredman",
                Time = DateTime.Now
            };

            // Duplicate check
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

            queue.Enqueue(newSignal);
            if (queue.Count > 3)
                queue.Dequeue();

            return newSignal;
        }
        catch (Exception ex)
        {
            logger.LogError($"Error extracting trade info: {ex.Message} - AlexFredman");
            return null;
        }
    }
}
