using System.Globalization;
using System.Text.RegularExpressions;
using AutoSignals.Models;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

public static class FedRussianInsiderSignalParser
{
    public static Signal? Parse(
        string message,
        ILogger logger,
        ConcurrentDictionary<string, Queue<Signal>> lastThreeEntries)
    {
        try
        {
            var takeProfits = new List<decimal>();

            // -------------------------------
            // 1. Extract Pair
            // -------------------------------
            // Supports: $BTC, $BTC/USDT, $BDXN etc.
            var pairMatch = Regex.Match(message, @"\$(?<pair>[A-Za-z0-9]+)(\/USDT)?");
            if (!pairMatch.Success)
                throw new ArgumentException("Pair not found.");

            string baseSymbol = pairMatch.Groups["pair"].Value.ToUpper();
            string pair = baseSymbol + "USDT";

            // -------------------------------
            // 2. Extract Entry
            // -------------------------------
            // Supports: Entry: 0.025  | Entry: Below 0.025  | Entry: 0.02 - 0.025
            var entryMatch = Regex.Match(
                message,
                @"Entry\s*:\s*(?:Below|Above|<|>)?\s*(?<e1>\d+(\.\d+)?)(\s*-\s*(?<e2>\d+(\.\d+)?))?",
                RegexOptions.IgnoreCase);

            if (!entryMatch.Success)
                throw new ArgumentException("Entry not found.");

            decimal entry = entryMatch.Groups["e2"].Success
                ? decimal.Parse(entryMatch.Groups["e2"].Value, CultureInfo.InvariantCulture)
                : decimal.Parse(entryMatch.Groups["e1"].Value, CultureInfo.InvariantCulture);

            // -------------------------------
            // 3. Extract SL
            // -------------------------------
            var slMatch = Regex.Match(
                message,
                @"(?:SL|Stop\s*Loss)\s*:\s*(?:Below|Above|<|>)?\s*(?<sl>\d+(\.\d+)?)",
                RegexOptions.IgnoreCase);

            if (!slMatch.Success)
                throw new ArgumentException("Stop-loss not found.");

            decimal stoploss = decimal.Parse(slMatch.Groups["sl"].Value, CultureInfo.InvariantCulture);

            // -------------------------------
            // 4. Extract Targets
            // -------------------------------
            // Inline: Targets: 0.027 - 0.029 - 0.032
            var inlineTargets = Regex.Match(
                message,
                @"Targets?\s*:\s*(?<t>(\d+(\.\d+)?\s*-\s*)+\d+(\.\d+)?)",
                RegexOptions.IgnoreCase);

            if (inlineTargets.Success)
            {
                takeProfits = inlineTargets.Groups["t"].Value
                    .Split('-', StringSplitOptions.RemoveEmptyEntries)
                    .Select(t => decimal.Parse(t.Trim(), CultureInfo.InvariantCulture))
                    .ToList();
            }
            else
            {
                // Fallback: Target 1: 0.027
                var matches = Regex.Matches(message, @"Target\s*\d+\s*[:\-]?\s*(?<tp>\d+(\.\d+)?)");
                foreach (Match m in matches)
                    takeProfits.Add(decimal.Parse(m.Groups["tp"].Value, CultureInfo.InvariantCulture));
            }

            if (takeProfits.Count == 0)
                throw new ArgumentException("No take-profit targets found.");

            // -------------------------------
            // 5. Extract Direction (optional)
            // -------------------------------
            string? side = null;

            var dirMatch = Regex.Match(
                message,
                @"Direction\s*:\s*(?<dir>Short|Long)|⬇️SHORT|⬆️LONG",
                RegexOptions.IgnoreCase);

            if (dirMatch.Success)
            {
                side = dirMatch.Value.ToLower().Contains("short") ? "short" : "long";
            }

            // Auto-detect if missing
            if (side == null)
            {
                decimal firstTp = takeProfits.First();
                side = firstTp < entry ? "short" : "long";
            }

            // -------------------------------
            // 6. Extract Leverage (optional, default 3x)
            // -------------------------------
            var levMatch = Regex.Match(
                message,
                @"Leverage\s*:\s*(?<l1>\d+)(\s*-\s*(?<l2>\d+))?X",
                RegexOptions.IgnoreCase);

            int leverage = 3; // default

            if (levMatch.Success)
            {
                int l1 = int.Parse(levMatch.Groups["l1"].Value);
                int l2 = levMatch.Groups["l2"].Success
                    ? int.Parse(levMatch.Groups["l2"].Value)
                    : l1;

                leverage = Math.Max(l1, l2);
            }

            // -------------------------------
            // 7. Risk (optional)
            // -------------------------------
            var riskMatch = Regex.Match(message, @"RISK\s*:\s*(?<risk>[A-Za-z\/]+)", RegexOptions.IgnoreCase);
            string risk = riskMatch.Success ? riskMatch.Groups["risk"].Value : "N/A";

            // -------------------------------
            // 8. Create Signal object
            // -------------------------------
            var unifiedSymbol = pair.Replace("USDT", "/USDT:USDT");
            var newSignal = new Signal
            {
                Symbol = unifiedSymbol,
                Side = side,
                Leverage = leverage,
                Entry = (float)entry,
                Stoploss = (float)stoploss,
                TakeProfits = string.Join(",", takeProfits.Select(tp => tp.ToString(CultureInfo.InvariantCulture))),
                Provider = "Fed Russian Insider",
                Time = DateTime.UtcNow
            };

            // -------------------------------
            // 9. Deduplication
            // -------------------------------
            if (!lastThreeEntries.TryGetValue(pair, out var queue))
            {
                queue = new Queue<Signal>();
                lastThreeEntries[pair] = queue;
            }
            else
            {
                if (queue.Any(s => s.Entry == newSignal.Entry && s.Stoploss == newSignal.Stoploss))
                    return null;
            }

            queue.Enqueue(newSignal);
            if (queue.Count > 3)
                queue.Dequeue();

            return newSignal;
        }
        catch (Exception ex)
        {
            logger.LogError($"Signal parsing error: {ex.Message}");
            return null;
        }

    }
}
