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

        try
        {
            // --------------------------
            // 1 — Extract Trading Pair
            // --------------------------
            var pairPattern = @"(?i)#?(?<pair>[A-Z0-9]{2,15})(?:\/USDT|USDT)?";
            var pairMatch = Regex.Match(message, pairPattern);
            if (!pairMatch.Success)
                throw new ArgumentException("Could not parse the trading pair.");

            var pair = pairMatch.Groups["pair"].Value.ToUpper() + "USDT";


            // -------------------------------------------
            // 2 — Extract Entry (single or range)
            // -------------------------------------------
            var entryPattern =
                @"(?i)Entry\s*price\s*[-:]\s*\$?\s*(?<e1>\d+(\.\d+)?)(?:\s*(?:to|-)\s*\$?(?<e2>\d+(\.\d+)?))?";

            var entryMatch = Regex.Match(message, entryPattern);
            if (!entryMatch.Success)
                throw new ArgumentException("Entry not found.");

            decimal entry = entryMatch.Groups["e2"].Success
                ? decimal.Parse(entryMatch.Groups["e2"].Value, CultureInfo.InvariantCulture)
                : decimal.Parse(entryMatch.Groups["e1"].Value, CultureInfo.InvariantCulture);


            // -------------------------------------------
            // 3 — Extract Targets (many separators)
            // -------------------------------------------
            var targetPattern =
                @"(?i)Target\s*[-:]\s*(?<all>(?:\$?\d+(\.\d+)?\+?)(?:\s*[,;&]\s*\$?\d+(\.\d+)?\+?)*)";

            var targetMatch = Regex.Match(message, targetPattern);
            if (!targetMatch.Success)
                throw new ArgumentException("Targets not found.");

            var tpRaw = targetMatch.Groups["all"].Value;
            var takeProfits = tpRaw
                .Split(new[] { ',', '&', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(v => v.Trim().Replace("$", ""))
                .Where(v => decimal.TryParse(v.Trim('+'), out _))
                .Select(v => decimal.Parse(v.Trim('+'), CultureInfo.InvariantCulture))
                .ToList();

            if (takeProfits.Count == 0)
                throw new ArgumentException("No valid TP values were found.");


            // -------------------------------------------
            // 4 — Extract Stop Loss
            //    Supports:
            //    - SL - 0.218
            //    - Stop Loss - If candle closes below $0.80
            // -------------------------------------------
            var stopLossPattern =
                @"(?i)Stop\s*Loss.*?(?:below\s*\$?(?<sl1>\d+(\.\d+)?)|[-:]\s*\$?(?<sl2>\d+(\.\d+)?))";

            var slMatch = Regex.Match(message, stopLossPattern);
            if (!slMatch.Success)
                throw new ArgumentException("Stop loss not found.");

            decimal stoploss = slMatch.Groups["sl1"].Success
                ? decimal.Parse(slMatch.Groups["sl1"].Value, CultureInfo.InvariantCulture)
                : decimal.Parse(slMatch.Groups["sl2"].Value, CultureInfo.InvariantCulture);


            // -------------------------------------------
            // 5 — Extract Position Type (Long/Short)
            //    If missing → auto-detect from TP direction
            // -------------------------------------------
            var typePattern = @"(?i)Type\s*[-:]\s*(?<side>Long|Short)";
            var typeMatch = Regex.Match(message, typePattern);

            string side;

            if (typeMatch.Success)
            {
                side = typeMatch.Groups["side"].Value.ToLower();
            }
            else
            {
                // Auto detect:
                // if TP > Entry → LONG
                // if TP < Entry → SHORT
                decimal firstTp = takeProfits.First();
                side = firstTp > entry ? "long" : "short";
            }


            // -------------------------------------------
            // 6 — Extract Leverage (optional)
            //    Default: 3x
            // -------------------------------------------
            var leveragePattern = @"(?i)Leverage\s*[:\-]\s*(?:Cross\s*)?\(?(?<lev>\d+)";
            var levMatch = Regex.Match(message, leveragePattern);

            int leverage = levMatch.Success
                ? int.Parse(levMatch.Groups["lev"].Value, CultureInfo.InvariantCulture)
                : 3;


            // -------------------------------------------
            // 7 — Create TP String
            // -------------------------------------------
            var tpString = string.Join(",", takeProfits.Select(x =>
                x.ToString(CultureInfo.InvariantCulture)));


            // -------------------------------------------
            // 8 — Duplicate Check
            // -------------------------------------------
            if (lastThreeEntries.TryGetValue(pair, out var queue))
            {
                if (queue.Any(s => s.Entry == (float)entry && s.Stoploss == (float)stoploss))
                {
                    logger.LogWarning($"Duplicate signal detected for {pair}. Ignoring.");
                    return null;
                }
            }
            else
            {
                queue = new Queue<Signal>();
                lastThreeEntries[pair] = queue;
            }


            // -------------------------------------------
            // 9 — Build Final Signal
            // -------------------------------------------
            var unifiedSymbol = pair.Replace("USDT", "/USDT:USDT");
            var newSignal = new Signal
            {
                Symbol = unifiedSymbol,
                Side = side,
                Leverage = leverage,
                Entry = (float)entry,
                Stoploss = (float)stoploss,
                TakeProfits = tpString,
                Provider = "CryptoAman",
                Time = DateTime.Now
            };

            queue.Enqueue(newSignal);
            if (queue.Count > 3)
                queue.Dequeue();

            return newSignal;
        }
        catch (Exception ex)
        {
            logger.LogError($"Error extracting trade info: {ex.Message} - CryptoAman");
            return null;
        }

    }
}
