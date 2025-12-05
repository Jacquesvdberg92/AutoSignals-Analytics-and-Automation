using System.Globalization;
using System.Text.RegularExpressions;
using AutoSignals.Models;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

public static class CryptoInnerCircleSignalParser
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
        var symbolPattern = @"(?:COIN -|Coin :|Coin:) (?<pair>[A-Za-z0-9]+)";
        var entryPattern = @"ENTRY : ([\d.]+)";
        var takeProfitPattern = @"Target\s*\d+\s*-\s*([\d.]+)";
        var positionTypePattern = @"(?:TYPE-|Type -|Type :|TYPE :) (LONG|long|Long|SHORT|Short|short)";
        var stopLossPattern = @"(?:STOPLOSS -|STOP loss :|stoploss -|STOPLOSS :) ([\d.]+)";
        var leveragePattern = @"Leverage : (\d+)X";

        try
        {
            var pairMatch = Regex.Match(message, symbolPattern);
            if (!pairMatch.Success)
                throw new ArgumentException("Could not parse the trading pair from the message.");

            var entryMatch = Regex.Match(message, entryPattern);
            if (!entryMatch.Success)
                throw new ArgumentException("Entry not found in message");

            var takeProfitMatches = Regex.Matches(message, takeProfitPattern, RegexOptions.Multiline);
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
            var pair = pairMatch.Groups["pair"].Value;
            var symbol = pair.Replace("/", "");

            var entry = float.Parse(entryMatch.Groups[1].Value, CultureInfo.InvariantCulture);
            var takeProfits = takeProfitMatches.Select(m => float.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).ToArray();
            var stopLoss = float.Parse(stopLossMatch.Groups[1].Value, CultureInfo.InvariantCulture);
            var side = positionTypeMatch.Groups[1].Value.ToLower();
            var leverage = int.Parse(leverageMatch.Groups[1].Value, CultureInfo.InvariantCulture);

            // Check for duplicates
            if (lastThreeEntries.TryGetValue(symbol, out var queue))
            {
                if (queue.Any(s => s.Entry == entry && s.Stoploss == stopLoss))
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
                Side = side,
                Leverage = leverage,
                Entry = entry,
                Stoploss = stopLoss,
                TakeProfits = string.Join(",", takeProfits.Select(tp => tp.ToString(CultureInfo.InvariantCulture))),
                Provider = "CryptoInnerCircle",
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
            logger.LogError($"Error extracting trade info: {ex.Message} - CryptoInnerCircle");
            return null;
        }
    }
}
