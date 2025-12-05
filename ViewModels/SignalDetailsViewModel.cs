using System;
using System.Collections.Generic;
using System.Linq;

namespace AutoSignals.Models
{
    public class SignalDetailsViewModel
    {
        public Signal Signal { get; set; }
        public SignalPerformance? Performance { get; set; }
        public Provider? Provider { get; set; }

        // Set from appsettings.json in the controller
        public string? TelegramGroup { get; set; }

        public string SymbolWithHash => $"#{Signal?.Symbol}";
        public string LeverageWithX => $"{Signal?.Leverage}x";
        public string SideColor =>
            Signal?.Side?.ToLower() == "long" ? "text-success" :
            Signal?.Side?.ToLower() == "short" ? "text-danger" :
            "text-secondary";

        public List<string> TakeProfitsList =>
            (Signal?.TakeProfits ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();

        public List<string> NotifiedTakeProfitsList =>
            (Performance?.NotifiedTakeProfits ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();

        public List<string> AchievedTakeProfitsList =>
            (Performance?.AchievedTakeProfits ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();

        /// <summary>
        /// Duration in d.hh:mm:ss format (legacy, use FormattedDuration for display)
        /// </summary>
        public string? Duration
        {
            get
            {
                if (Performance?.EndTime != null)
                {
                    var duration = Performance.EndTime.Value - Performance.StartTime;
                    return duration.ToString(@"d\.hh\:mm\:ss");
                }
                return null;
            }
        }

        /// <summary>
        /// Duration in "X months Y days Z hours W minutes" format
        /// </summary>
        public string? FormattedDuration
        {
            get
            {
                if (Performance?.EndTime != null)
                {
                    var duration = Performance.EndTime.Value - Performance.StartTime;
                    int months = duration.Days / 30;
                    int days = duration.Days % 30;
                    int hours = duration.Hours;
                    int minutes = duration.Minutes;
                    return $"{months} months {days} days {hours} hours {minutes} minutes";
                }
                return null;
            }
        }

        /// <summary>
        /// Returns a Telegram group link if TelegramGroup is set, else null.
        /// </summary>
        public string? TelegramGroupLink
        {
            get
            {
                if (string.IsNullOrWhiteSpace(TelegramGroup))
                    return null;

                // Numeric ID (private group)
                if (long.TryParse(TelegramGroup, out var groupId))
                {
                    // Remove -100 prefix if present
                    var webId = groupId.ToString();
                    if (webId.StartsWith("-100"))
                        webId = "-" + webId.Substring(4);
                    return $"https://web.telegram.org/k/#{webId}";
                }

                // Public group/channel
                return $"https://t.me/{TelegramGroup.TrimStart('@')}";
            }
        }


    }
}
