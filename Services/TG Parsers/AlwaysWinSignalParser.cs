using AutoSignals.Models;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;

public static class AlwaysWinSignalParser
{
    public static Signal? Parse(
        string message,
        ILogger logger,
        ConcurrentDictionary<string, Queue<Signal>> lastThreeEntries)
    {
        try
        {
            var takeProfits = new Dictionary<string, decimal>();

            // Parse symbol and side
            var symbolSidePattern = @"(?<symbol>[A-Za-z0-9]+\/USDT)\s+(?<side>LONG|SHORT)";
            var symbolSideMatch = Regex.Match(message, symbolSidePattern, RegexOptions.IgnoreCase);
            if (!symbolSideMatch.Success)
                throw new ArgumentException("Could not parse symbol or side.");

            var symbol = symbolSideMatch.Groups["symbol"].Value.Replace("/", "");
            var side = symbolSideMatch.Groups["side"].Value.ToLower();

            // Parse leverage
            var leveragePattern = @"Leverage\s*-?\s*(?<leverage>\d+)[xX]";
            var leverageMatch = Regex.Match(message, leveragePattern);
            if (!leverageMatch.Success)
                throw new ArgumentException("Could not parse leverage.");
            var leverage = int.Parse(leverageMatch.Groups["leverage"].Value);

            // Parse entry or entry zone
            float entry;
            var entryPattern = @"Entries\s*(?<entry>\d+(\.\d+)?)";
            var entryZonePattern = @"Entry zone\s*(?<entryLow>\d+(\.\d+)?)\s*-\s*(?<entryHigh>\d+(\.\d+)?)";

            if (Regex.IsMatch(message, entryZonePattern))
            {
                var match = Regex.Match(message, entryZonePattern);
                var low = float.Parse(match.Groups["entryLow"].Value, CultureInfo.InvariantCulture);
                var high = float.Parse(match.Groups["entryHigh"].Value, CultureInfo.InvariantCulture);
                entry = (low + high) / 2f; // average of zone
            }
            else
            {
                var match = Regex.Match(message, entryPattern);
                if (!match.Success)
                    throw new ArgumentException("Could not parse entry price.");
                entry = float.Parse(match.Groups["entry"].Value, CultureInfo.InvariantCulture);
            }

            // Parse take profits (Targets)
            var targetPattern = @"Target\s*(?<index>\d+)\s+(?<value>\d+(\.\d+)?)";
            foreach (Match match in Regex.Matches(message, targetPattern))
            {
                var label = $"T{match.Groups["index"].Value}";
                var value = decimal.Parse(match.Groups["value"].Value, CultureInfo.InvariantCulture);
                takeProfits[label] = value;
            }

            // Parse stoploss
            var stoplossPattern = @"SL\s*(?<sl>\d+(\.\d+)?)";
            var stoplossMatch = Regex.Match(message, stoplossPattern);
            if (!stoplossMatch.Success)
                throw new ArgumentException("Could not parse stoploss.");
            var stoploss = float.Parse(stoplossMatch.Groups["sl"].Value, CultureInfo.InvariantCulture);

            // Concatenate take profit values
            var takeProfitsString = string.Join(",", takeProfits.Values.Select(tp => tp.ToString(CultureInfo.InvariantCulture)));

            // Build signal
            var newSignal = new Signal
            {
                Symbol = symbol.ToUpper(),
                Side = side,
                Leverage = leverage,
                Entry = entry,
                Stoploss = stoploss,
                TakeProfits = takeProfitsString,
                Provider = "AlwaysWin",
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
            logger.LogError($"Error parsing AlwaysWin signal: {ex.Message}");
            return null;
        }
    }
}
