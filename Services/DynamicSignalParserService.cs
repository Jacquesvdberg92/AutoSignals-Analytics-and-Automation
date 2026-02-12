// Services/DynamicSignalParserService.cs
using AutoSignals.Data;
using AutoSignals.Models;
using AutoSignals.Utilities;
using AutoSignals.ViewModels.ProviderRegex;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AutoSignals.Services
{
    public class DynamicSignalParserService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<DynamicSignalParserService> _logger;
        private readonly ConcurrentDictionary<int, SignalProviderConfig> _providerConfigCache = new();

        public DynamicSignalParserService(
            IServiceScopeFactory scopeFactory,
            ILogger<DynamicSignalParserService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public async Task<Signal?> ParseSignalAsync(
        string message,
        string telegramGroupId,
        ConcurrentDictionary<string, Queue<Signal>> lastThreeEntries)
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AutoSignalsDbContext>();

            // Prefer providers mapped to this Telegram group (major plus)
            var preferredProviders = await dbContext.SignalProviders
                .Include(p => p.ParsingRules)
                .Where(p => p.IsActive && p.TelegramGroupId == telegramGroupId)
                .ToListAsync();

            foreach (var provider in preferredProviders)
            {
                var signal = await ParseWithProviderConfig(message, provider, lastThreeEntries);
                if (signal != null)
                    return signal;
            }

            // Fallback: try all other active providers
            var fallbackProviders = await dbContext.SignalProviders
                .Include(p => p.ParsingRules)
                .Where(p => p.IsActive && p.TelegramGroupId != telegramGroupId)
                .ToListAsync();

            foreach (var provider in fallbackProviders)
            {
                var signal = await ParseWithProviderConfig(message, provider, lastThreeEntries);
                if (signal != null)
                    return signal;
            }

            _logger.LogWarning($"No provider matched message for Telegram group: {telegramGroupId}");
            return null;
        }

        private async Task<Signal?> ParseWithProviderConfig(
            string message,
            SignalProvider provider,
            ConcurrentDictionary<string, Queue<Signal>> lastThreeEntries)
        {
            try
            {
                // Sanitize the message but keep useful special characters
                var sanitizedMessage = MessageSanitizer.SanitizeMessage(message);

                // Log original vs sanitized for debugging
                _logger.LogDebug($"Original: {message.Substring(0, Math.Min(100, message.Length))}");
                _logger.LogDebug($"Sanitized: {sanitizedMessage.Substring(0, Math.Min(100, sanitizedMessage.Length))}");

                // Create a working copy that will be modified as matches are found
                var workingCopy = sanitizedMessage;

                var rules = provider.ParsingRules.OrderBy(r => r.Order).ToList();
                var parsedValues = new Dictionary<string, object>();
                var tpValues = new List<string>(); // To accumulate TP values

                foreach (var rule in rules)
                {
                    var result = ApplyParsingRule(ref workingCopy, rule, sanitizedMessage);

                    if (rule.IsRequired && result == null)
                    {
                        _logger.LogWarning($"Required rule '{rule.RuleType}' failed for provider '{provider.Name}'");
                        return null;
                    }

                    // For TakeProfit rules, accumulate values
                    if (rule.RuleType == "TakeProfit" && result != null)
                    {
                        if (result is string tpString)
                        {
                            ProcessTakeProfitValues(tpString, tpValues);
                        }
                    }
                    else if (result != null)
                    {
                        parsedValues[rule.RuleType] = result;
                    }
                    else if (!string.IsNullOrEmpty(rule.FallbackValue))
                    {
                        parsedValues[rule.RuleType] = rule.FallbackValue;
                    }
                }

                // Combine all TP values, remove duplicates while preserving order
                if (tpValues.Any())
                {
                    var distinctTps = tpValues
                        .Where(v => !string.IsNullOrWhiteSpace(v))
                        .Select(v => decimal.TryParse(v, out decimal num) ? num.ToString("0.########") : v)
                        .Distinct()
                        .OrderBy(v => decimal.Parse(v)) // Sort numerically
                        .ToList();

                    parsedValues["TakeProfit"] = string.Join(",", distinctTps);
                }

                // Create signal from parsed values
                var signal = MapToSignal(parsedValues, provider.Name);

                if (signal == null)
                    return null;

                // Apply deduplication...
                return signal;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error parsing signal for provider '{provider.Name}'");
                return null;
            }
        }

        private void ProcessTakeProfitValues(string tpString, List<string> tpValues)
        {
            if (tpString.Contains(','))
            {
                // Multiple TPs in one string (from single pattern with groups)
                var splitValues = tpString.Split(',')
                    .Select(v => v.Trim())
                    .Where(v => !string.IsNullOrEmpty(v) && IsValidDecimal(v));
                tpValues.AddRange(splitValues);
            }
            else if (IsValidDecimal(tpString))
            {
                // Single TP value
                tpValues.Add(tpString.Trim());
            }
        }

        private bool IsValidDecimal(string value)
        {
            return decimal.TryParse(value, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out _);
        }

        private object? ApplyParsingRule(ref string workingCopy, ProviderParsingRule rule, string originalSanitized)
        {
            try
            {
                // Try matching against the working copy first
                var match = Regex.Match(workingCopy, rule.RegexPattern,
                    RegexOptions.IgnoreCase | RegexOptions.Multiline);

                // If no match in working copy, try the original sanitized message
                if (!match.Success && workingCopy != originalSanitized)
                {
                    match = Regex.Match(originalSanitized, rule.RegexPattern,
                        RegexOptions.IgnoreCase | RegexOptions.Multiline);
                }

                if (!match.Success)
                    return null;

                // Extract value based on rule type
                object? extractedValue = ExtractValueFromMatch(match, rule);

                if (extractedValue == null)
                    return null;

                string extractedValueStr = extractedValue.ToString();

                // NEW: Apply JSON validation if exists
                if (!string.IsNullOrEmpty(rule.ValidationLogic) && !string.IsNullOrEmpty(extractedValueStr))
                {
                    var validationResult = ValidateWithJsonLogic(extractedValueStr, rule.ValidationLogic, rule.RuleType);

                    if (!validationResult.IsValid)
                    {
                        _logger.LogWarning($"Validation failed for rule {rule.RuleType}: {validationResult.ErrorMessage}");

                        // If rule is required and validation fails, return null
                        if (rule.IsRequired)
                            return null;

                        // If not required but has fallback value, use it
                        if (!string.IsNullOrEmpty(rule.FallbackValue))
                            return rule.FallbackValue;
                    }
                }

                // Remove matched content from working copy for TP rules
                if (rule.RuleType == "TakeProfit" && match.Success && match.Length > 0)
                {
                    workingCopy = RemoveMatchedText(workingCopy, match.Index, match.Length);
                }

                return extractedValue;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error applying regex rule '{rule.RuleType}'");
                return null;
            }
        }

        private object? ExtractValueFromMatch(Match match, ProviderParsingRule rule)
        {
            if (rule.RuleType == "TakeProfit")
            {
                var tpValues = new List<string>();

                // Check for numbered TP groups (tp1, tp2, tp3, tp4, etc.)
                for (int i = 1; i <= 10; i++) // Support up to 10 TP values
                {
                    var groupName = $"tp{i}";
                    if (match.Groups[groupName].Success)
                    {
                        var value = match.Groups[groupName].Value.Trim();
                        if (IsValidDecimal(value))
                            tpValues.Add(value);
                    }
                    else
                    {
                        // Stop when we don't find consecutive TP groups
                        if (i > 1) break;
                    }
                }

                // If we found numbered groups, return them as comma-separated
                if (tpValues.Any())
                    return string.Join(",", tpValues);

                // Fallback to single group extraction
                if (!string.IsNullOrEmpty(rule.RegexGroupName) && match.Groups[rule.RegexGroupName].Success)
                {
                    var value = match.Groups[rule.RegexGroupName].Value;
                    return ConvertValue(value, rule.RuleType);
                }

                // For simple pattern matching without groups:
                // Only extract numbers if the matched text actually looks like a TP/Targets section,
                // otherwise a too-broad match can accidentally pick up Entry.
                if (match.Success)
                {
                    var matchText = match.Value;

                    var looksLikeTakeProfitSection =
                        Regex.IsMatch(matchText, @"\b(tp|t\.p\.|target|targets|take\s*profit)\b",
                            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

                    if (looksLikeTakeProfitSection)
                    {
                        // Pick the first numeric token found in the TP section
                        // Supports integers and decimals, dot or comma decimals.
                        var numberMatch = Regex.Match(matchText, @"\d+(?:[.,]\d+)?",
                            RegexOptions.CultureInvariant);

                        if (numberMatch.Success)
                            return ConvertValue(numberMatch.Value.Replace(',', '.'), rule.RuleType);
                    }
                }

                return null;
            }

            // Original logic for other rules
            if (!string.IsNullOrEmpty(rule.RegexGroupName) && match.Groups[rule.RegexGroupName].Success)
            {
                var value = match.Groups[rule.RegexGroupName].Value;
                return ConvertValue(value, rule.RuleType);
            }

            return ConvertValue(match.Value, rule.RuleType);
        }

        // Update the ApplyParsingRule method in DynamicSignalParserService.cs


        private string RemoveMatchedText(string text, int startIndex, int length)
        {
            // Replace matched text with placeholder (or remove it)
            // Using placeholder makes it easier to maintain text structure
            return text.Remove(startIndex, Math.Min(length, text.Length - startIndex))
                       .Insert(startIndex, "[MATCHED]");
        }

        private object? ConvertValue(string value, string ruleType)
        {
            return ruleType switch
            {
                "Symbol" => value.ToUpper(),
                "Side" => value.ToLower(),
                "Entry" => float.Parse(value, System.Globalization.CultureInfo.InvariantCulture),
                "Stoploss" => float.Parse(value, System.Globalization.CultureInfo.InvariantCulture),
                "Leverage" => int.Parse(value, System.Globalization.CultureInfo.InvariantCulture),
                "TakeProfit" =>
                    // For TakeProfit, value should already be comma-separated from ApplyParsingRule
                    string.Join(",", value.Split(',')
                        .Select(v => decimal.Parse(v.Trim(), System.Globalization.CultureInfo.InvariantCulture)
                            .ToString(System.Globalization.CultureInfo.InvariantCulture))),
                _ => value
            };
        }

        private Signal? MapToSignal(Dictionary<string, object> values, string providerName)
        {
            try
            {
                var symbol = values.ContainsKey("Symbol") ? values["Symbol"].ToString() : null;
                if (string.IsNullOrWhiteSpace(symbol))
                    return null;

                // Normalize inputs like "BTCUSDT" and "BTC/USDT" to "BTC/USDT:USDT"
                var normalized = symbol.Trim().ToUpperInvariant().Replace("/", "");

                if (normalized.EndsWith("USDT", StringComparison.Ordinal))
                {
                    var baseAsset = normalized[..^"USDT".Length];
                    symbol = $"{baseAsset}/USDT:USDT";
                }
                else
                {
                    // If other formats ever appear, at least keep it slashless/uppercased.
                    symbol = normalized;
                }

                var side = values.ContainsKey("Side")
                    ? values["Side"].ToString()!.ToLowerInvariant()
                    : "long";

                var leverage = GetInt(values, "Leverage", 3);
                var entry = GetFloat(values, "Entry", 0f);
                var stoplossValue = GetFloatNullable(values, "Stoploss");
                var stoploss = stoplossValue ?? ComputeDefaultStoploss(entry, side, 0.10f);

                return new Signal
                {
                    Symbol = symbol,
                    Side = side,
                    Leverage = leverage,
                    Entry = entry,
                    Stoploss = stoploss,
                    TakeProfits = values.ContainsKey("TakeProfit") ? values["TakeProfit"].ToString()! : "",
                    Provider = providerName,
                    Time = DateTime.UtcNow
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error mapping parsed values to Signal");
                return null;
            }
        }

        private ValidationResult ValidateWithJsonLogic(string value, string jsonLogic, string ruleType)
        {
            var result = new ValidationResult();

            if (string.IsNullOrWhiteSpace(jsonLogic))
                return result;

            try
            {
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };

                var validationRules = JsonSerializer.Deserialize<List<ValidationRule>>(jsonLogic, options);

                if (validationRules == null || !validationRules.Any())
                    return result;

                foreach (var rule in validationRules)
                {
                    bool isValid = ValidateRule(value, rule, ruleType);

                    if (!isValid)
                    {
                        string error = rule.ErrorMessage ?? $"Value '{value}' failed validation for {ruleType} rule";
                        result.Errors.Add(error);
                        result.IsValid = false;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating with JSON logic");
                result.Errors.Add($"Validation error: {ex.Message}");
                result.IsValid = false;
            }

            return result;
        }

        private bool ValidateRule(string value, ValidationRule rule, string ruleType)
        {
            if (rule.Operator == null || rule.Value == null)
                return true;

            string operatorLower = rule.Operator.ToLowerInvariant();

            try
            {
                switch (operatorLower)
                {
                    case "min":
                        if (decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal numVal) &&
                            TryParseDecimal(rule.Value, out decimal minVal))
                            return numVal >= minVal;
                        break;

                    case "max":
                        if (decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal numVal2) &&
                            TryParseDecimal(rule.Value, out decimal maxVal))
                            return numVal2 <= maxVal;
                        break;

                    case "regex":
                        if (rule.Value is string regexPattern)
                            return Regex.IsMatch(value, regexPattern, RegexOptions.IgnoreCase);
                        break;

                    case "in":
                        var allowedValues = ParseStringArray(rule.Value);
                        if (allowedValues != null)
                            return allowedValues.Contains(value, StringComparer.OrdinalIgnoreCase);
                        break;

                    case "notnull":
                    case "required":
                        return !string.IsNullOrWhiteSpace(value);

                    case "lengthmin":
                        if (int.TryParse(rule.Value?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out int minLength))
                            return value.Length >= minLength;
                        break;

                    case "lengthmax":
                        if (int.TryParse(rule.Value?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out int maxLength))
                            return value.Length <= maxLength;
                        break;

                    case "positive":
                        if (decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal posVal))
                            return posVal > 0;
                        break;

                    case "negative":
                        if (decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal negVal))
                            return negVal < 0;
                        break;

                    case "range":
                        if (rule.Value is string rangeStr)
                        {
                            var parts = rangeStr.Split('-');
                            if (parts.Length == 2 &&
                                decimal.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out decimal rangeMin) &&
                                decimal.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out decimal rangeMax) &&
                                decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal rangeVal))
                                return rangeVal >= rangeMin && rangeVal <= rangeMax;
                        }
                        break;

                    case "equal":
                        return value.Equals(rule.Value.ToString(), StringComparison.OrdinalIgnoreCase);

                    case "notequal":
                        return !value.Equals(rule.Value.ToString(), StringComparison.OrdinalIgnoreCase);

                    case "custom":
                        // For custom logic, you could add a delegate or function pointer
                        // This would require additional configuration
                        break;
                }
            }
            catch (Exception ex)
            {
                // Log error but don't fail the entire validation
                _logger.LogDebug(ex, $"Error in validation rule {operatorLower} for {ruleType}");
                return false;
            }

            // If operator is not recognized or validation couldn't be performed
            return true;
        }

        // Helper methods for parsing
        private bool TryParseDecimal(object value, out decimal result)
        {
            result = 0;

            if (value == null)
                return false;

            if (value is JsonElement jsonElement)
            {
                if (jsonElement.ValueKind == JsonValueKind.Number)
                    return jsonElement.TryGetDecimal(out result);
                else if (jsonElement.ValueKind == JsonValueKind.String)
                    return decimal.TryParse(jsonElement.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out result);
            }

            return decimal.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out result);
        }

        private string[]? ParseStringArray(object value)
        {
            if (value == null)
                return null;

            if (value is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.Array)
            {
                return jsonElement.EnumerateArray()
                    .Select(e => e.ToString())
                    .ToArray();
            }
            else if (value is string stringValue)
            {
                return stringValue.Split(',')
                    .Select(v => v.Trim())
                    .Where(v => !string.IsNullOrEmpty(v))
                    .ToArray();
            }

            return null;
        }

        private static float ComputeDefaultStoploss(float entry, string side, float percent)
        {
            if (entry <= 0f)
                return 0f;

            var isShort = string.Equals(side, "short", StringComparison.OrdinalIgnoreCase);

            // long: SL below entry; short: SL above entry
            return isShort
                ? entry * (1f + percent)
                : entry * (1f - percent);
        }

        private static float? GetFloatNullable(Dictionary<string, object> values, string key)
        {
            if (!values.TryGetValue(key, out var raw) || raw is null)
                return null;

            if (raw is float f) return f;
            if (raw is double d) return (float)d;
            if (raw is decimal m) return (float)m;
            if (raw is int i) return i;

            var s = raw.ToString()?.Trim();
            if (string.IsNullOrWhiteSpace(s) || s.Equals("null", StringComparison.OrdinalIgnoreCase))
                return null;

            return float.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : null;
        }

        private static float GetFloat(Dictionary<string, object> values, string key, float defaultValue)
            => GetFloatNullable(values, key) ?? defaultValue;

        private static int GetInt(Dictionary<string, object> values, string key, int defaultValue)
        {
            if (!values.TryGetValue(key, out var raw) || raw is null)
                return defaultValue;

            if (raw is int i) return i;

            var s = raw.ToString()?.Trim();
            if (string.IsNullOrWhiteSpace(s) || s.Equals("null", StringComparison.OrdinalIgnoreCase))
                return defaultValue;

            return int.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : defaultValue;
        }

        private bool IsDuplicate(Signal newSignal, ConcurrentDictionary<string, Queue<Signal>> lastThreeEntries)
        {
            var symbolKey = newSignal.Symbol.Replace("/USDT:USDT", "USDT");

            if (lastThreeEntries.TryGetValue(symbolKey, out var queue))
            {
                return queue.Any(s =>
                    Math.Abs(s.Entry - newSignal.Entry) < 0.0001f &&
                    Math.Abs(s.Stoploss - newSignal.Stoploss) < 0.0001f);
            }

            return false;
        }

        // Method to refresh cache (call this when rules are updated)
        public async Task RefreshCacheAsync(int providerId)
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AutoSignalsDbContext>();

            var provider = await dbContext.SignalProviders
                .Include(p => p.ParsingRules)
                .FirstOrDefaultAsync(p => p.Id == providerId);

            if (provider != null)
            {
                _providerConfigCache[providerId] = new SignalProviderConfig
                {
                    Provider = provider,
                    Rules = provider.ParsingRules.OrderBy(r => r.Order).ToList()
                };
            }
        }

        public async Task<TestRegexViewModel> TestParsingAsync(int providerId, string message)
        {
            using var scope = _scopeFactory.CreateScope();
            var _context = scope.ServiceProvider.GetRequiredService<AutoSignalsDbContext>();

            var provider = await _context.SignalProviders
                .Include(p => p.ParsingRules)
                .FirstOrDefaultAsync(p => p.Id == providerId);

            var model = new TestRegexViewModel
            {
                ProviderId = providerId,
                ProviderName = provider?.Name,
                TelegramMessage = message
            };

            // Use the same testing logic from controller
            return model;
        }

        private class SignalProviderConfig
        {
            public SignalProvider Provider { get; set; } = null!;
            public List<ProviderParsingRule> Rules { get; set; } = new();
        }
    }
}