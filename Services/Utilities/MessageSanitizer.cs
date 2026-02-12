// Utilities/MessageSanitizer.cs
using System.Text.RegularExpressions;

namespace AutoSignals.Utilities
{
    public static class MessageSanitizer
    {
        public static string SanitizeMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return message;

            // 1. Remove common emojis and symbols that interfere with parsing
            // Keep: / $ , . - ( ) [ ] : + % x
            string sanitized = Regex.Replace(message,
                @"[\p{So}\p{Sm}\p{Sc}\p{Sk}\p{Cs}]+", " ");

            // Alternative: Remove specific known problematic emojis
            // sanitized = Regex.Replace(message, 
            //     @"[🔥🚀🥇🥈🥉📈📉✅❌⚡✨💥⭐]+", " ");

            // 2. Normalize whitespace: multiple spaces/tabs to single space
            sanitized = Regex.Replace(sanitized, @"[ \t]+", " ");

            // 3. Normalize line breaks: multiple newlines to single newline
            sanitized = Regex.Replace(sanitized, @"(\r\n|\n|\r)+", "\n");

            // 4. Remove leading/trailing spaces from each line
            var lines = sanitized.Split('\n')
                .Select(line => line.Trim())
                .Where(line => !string.IsNullOrWhiteSpace(line));

            return string.Join("\n", lines);
        }

        // Alternative: Keep emojis but mark them as spaces for regex matching
        public static string NormalizeForParsing(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return message;

            // Replace emojis and special symbols with spaces but preserve structure
            string normalized = message;

            // Replace common trading emojis with descriptive text or spaces
            normalized = Regex.Replace(normalized, @"🔥|🚀|⚡|✨|💥", " ");
            normalized = Regex.Replace(normalized, @"🥇|🥈|🥉|🏆|⭐", " ");
            normalized = Regex.Replace(normalized, @"📈|📉|↗️|↘️|⬆️|⬇️", " ");
            normalized = Regex.Replace(normalized, @"✅|❌|✔️|✖️", " ");

            // Normalize whitespace
            normalized = Regex.Replace(normalized, @"\s+", " ");
            normalized = Regex.Replace(normalized, @"(\r\n|\n|\r)+", "\n");

            var lines = normalized.Split('\n')
                .Select(line => line.Trim())
                .Where(line => !string.IsNullOrWhiteSpace(line));

            return string.Join("\n", lines);
        }

        // Even better: Transform message while keeping emoji information
        public static (string CleanedMessage, Dictionary<string, string> EmojiMap)
            SanitizeWithEmojiMapping(string message)
        {
            var emojiMap = new Dictionary<string, string>();
            var cleaned = message;

            // Find and replace emojis with placeholders
            var emojiMatches = Regex.Matches(message, @"\p{So}+");
            int emojiIndex = 0;

            foreach (Match match in emojiMatches)
            {
                string placeholder = $"[EMOJI_{emojiIndex++}]";
                emojiMap[placeholder] = match.Value;
                cleaned = cleaned.Replace(match.Value, " " + placeholder + " ");
            }

            // Normalize whitespace
            cleaned = Regex.Replace(cleaned, @"\s+", " ");
            cleaned = Regex.Replace(cleaned, @"(\r\n|\n|\r)+", "\n");

            return (cleaned, emojiMap);
        }
    }
}