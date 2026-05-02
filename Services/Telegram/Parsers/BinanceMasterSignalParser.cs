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

            // -----------------------------------------------------
            // 1. SYMBOL
            // Example: #BIGTIME/USDT
            // -----------------------------------------------------
            var symbolMatch = Regex.Match(message, @"#(?<symbol>[A-Za-z0-9]+\/[A-Za-z]+)");
            if (!symbolMatch.Success)
                throw new ArgumentException("Could not parse the symbol.");

            var symbol = symbolMatch.Groups["symbol"].Value.Replace("/", "").ToUpper(); // BIGTIMEUSDT


            // -----------------------------------------------------
            // 2. TYPE (Long / Short)
            // -----------------------------------------------------
            var sideMatch = Regex.Match(message, @"Signal Type:\s*Regular\s*\((?<type>Long|Short)\)", RegexOptions.IgnoreCase);
            if (!sideMatch.Success)
                throw new ArgumentException("Could not parse the signal type.");

            var side = sideMatch.Groups["type"].Value.ToLower(); // "short"


            // -----------------------------------------------------
            // 3. LEVERAGE (Cross 20х with Cyrillic х)
            // -----------------------------------------------------
            var leverageMatch = Regex.Match(message, @"Leverage:\s*Cross\s*\((?<lev>\d+)[xх]\)", RegexOptions.IgnoreCase);
            if (!leverageMatch.Success)
                throw new ArgumentException("Could not parse leverage.");

            int leverage = int.Parse(leverageMatch.Groups["lev"].Value, CultureInfo.InvariantCulture);


            // -----------------------------------------------------
            // 4. ENTRY TARGET
            // Example:
            // Entry Targets:
            // 0.02331
            // -----------------------------------------------------
            var entryMatch = Regex.Match(message, @"Entry Targets:\s*(?<entry>\d+(\.\d+)?)", RegexOptions.IgnoreCase);
            if (!entryMatch.Success)
                throw new ArgumentException("Entry price not found.");

            decimal entry = decimal.Parse(entryMatch.Groups["entry"].Value, CultureInfo.InvariantCulture);


            // -----------------------------------------------------
            // 5. TAKE PROFIT TARGETS
            // Example:
            // 1) 0.02309
            // 2) 0.02285
            // ...
            // -----------------------------------------------------
            var tpMatches = Regex.Matches(message, @"\d+\)\s*(?<tp>\d+(\.\d+)?)");

            if (tpMatches.Count == 0)
                throw new ArgumentException("No take-profit targets found.");

            int tpIndex = 1;
            foreach (Match m in tpMatches)
            {
                takeProfits[tpIndex++] = decimal.Parse(m.Groups["tp"].Value, CultureInfo.InvariantCulture);
            }


            // -----------------------------------------------------
            // 6. STOPLOSS
            // Example:
            // Stoploss
            // 0.02448
            // -----------------------------------------------------
            var slMatch = Regex.Match(message, @"Stoploss\s*\n(?<sl>\d+(\.\d+)?)", RegexOptions.IgnoreCase);
            if (!slMatch.Success)
                throw new ArgumentException("Stoploss not found.");

            decimal stoploss = decimal.Parse(slMatch.Groups["sl"].Value, CultureInfo.InvariantCulture);


            // -----------------------------------------------------
            // 7. Create signal
            // -----------------------------------------------------
            var unifiedSymbol = symbol.Replace("USDT", "/USDT:USDT");
            var newSignal = new Signal
            {
                Symbol = unifiedSymbol,
                Side = side,
                Leverage = leverage,
                Entry = (float)entry,
                Stoploss = (float)stoploss,
                TakeProfits = string.Join(",", takeProfits.Values.Select(t => t.ToString(CultureInfo.InvariantCulture))),
                Provider = "BinanceMaster",
                Time = DateTime.UtcNow
            };


            // -----------------------------------------------------
            // 8. Deduplication
            // -----------------------------------------------------
            if (!lastThreeEntries.TryGetValue(symbol, out var queue))
                queue = lastThreeEntries[symbol] = new Queue<Signal>();

            if (queue.Any(s => s.Entry == newSignal.Entry && s.Stoploss == newSignal.Stoploss))
                return null;

            queue.Enqueue(newSignal);
            if (queue.Count > 3)
                queue.Dequeue();


            return newSignal;
        }
        catch (Exception ex)
        {
            logger.LogError($"Error extracting trade info: {ex.Message} - BinanceMaster");
            return null;
        }

    }
}
