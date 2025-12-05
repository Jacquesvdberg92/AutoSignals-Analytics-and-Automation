using System.Globalization;
using System.Text.RegularExpressions;
using AutoSignals.Models;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

public static class BybitProSignalParser
{
    public static Signal? Parse(
        string message,
        float stoplossPercent,
        ILogger logger,
        ConcurrentDictionary<string, Queue<Signal>> lastThreeEntries)
    {
        try
        {
            var takeProfits = new Dictionary<string, decimal>();

            // Parse the symbol, side (Long/Short), and leverage
            var symbolSideLeveragePattern = @"#(?<symbol>[A-Za-z0-9]+\/[A-Za-z]+) \((?<side>Long|Short).*x(?<leverage>\d+(\.\d+)?)\)";
            var symbolSideLeverageMatch = Regex.Match(message, symbolSideLeveragePattern);
            if (symbolSideLeverageMatch.Success)
            {
                var symbol = symbolSideLeverageMatch.Groups["symbol"].Value.Replace("/", "");
                var side = symbolSideLeverageMatch.Groups["side"].Value.ToLower();
                var leverage = int.Parse(symbolSideLeverageMatch.Groups["leverage"].Value, CultureInfo.InvariantCulture);

                // Parse the entry price
                var entryPattern = @"Entry\s*[-:]\s*(?<entry>\d+(\.\d+)?)";
                var entryMatch = Regex.Match(message, entryPattern);
                if (entryMatch.Success)
                {
                    var entry = float.Parse(entryMatch.Groups["entry"].Value, CultureInfo.InvariantCulture);

                    // Calculate stoploss (adjust based on long/short)
                    float stoploss = side == "long"
                        ? entry - (entry * stoplossPercent / 100)
                        : entry + (entry * stoplossPercent / 100);
                    var stoplossValue = stoploss;

                    // Parse the take profits
                    var takeProfitPattern = @"(?<tier>🥉|🥈|🥇|🚀)\s+(?<takeProfit>\d+(\.\d+)?)";
                    var takeProfitMatches = Regex.Matches(message, takeProfitPattern);
                    foreach (Match match in takeProfitMatches)
                    {
                        if (match.Success)
                        {
                            takeProfits[match.Groups["tier"].Value] = decimal.Parse(match.Groups["takeProfit"].Value, CultureInfo.InvariantCulture);
                        }
                    }

                    // Concatenate take profit values as a string (comma separated) with period as the decimal separator
                    var takeProfitsString = string.Join(",", takeProfits.Values.Select(tp => tp.ToString(CultureInfo.InvariantCulture).Replace(',', '.')));

                    // Create the new signal
                    var newSignal = new Signal
                    {
                        Symbol = symbol.ToUpper(),
                        Side = side,
                        Leverage = leverage,
                        Entry = entry,
                        Stoploss = stoplossValue,
                        TakeProfits = takeProfitsString,
                        Provider = "BybitPro",
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
                    throw new ArgumentException("Could not parse the entry price from the message.");
                }
            }
            else
            {
                throw new ArgumentException("Could not parse the symbol, side, or leverage from the message.");
            }
        }
        catch (Exception ex)
        {
            logger.LogError($"Error extracting trade info: {ex.Message} - BybitPro");
            return null;
        }
    }
}
