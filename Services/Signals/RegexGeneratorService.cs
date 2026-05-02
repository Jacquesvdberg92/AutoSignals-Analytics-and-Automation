using AutoSignals.Models;
using AutoSignals.Utilities;
using AutoSignals.ViewModels.ProviderRegex;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace AutoSignals.Services
{
    public class RegexGeneratorService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<RegexGeneratorService> _logger;

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public RegexGeneratorService(
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            ILogger<RegexGeneratorService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<string?> GenerateSignalNarrativeAsync(
            Signal signal,
            SignalPrediction prediction,
            CancellationToken cancellationToken = default)
        {
            var token = _configuration["GitHub:ModelsToken"];
            if (string.IsNullOrWhiteSpace(token))
                return null;

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(30));

            try
            {
                var tpProbs = prediction.TpProbabilities
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var tp1Prob = tpProbs.Length > 0 ? tpProbs[0] : "N/A";

                var userPrompt = $"""
                    Signal: {signal.Symbol} {signal.Side?.ToUpper()} {signal.Leverage}x
                    Entry: {signal.Entry} | Stoploss: {signal.Stoploss} | TPs: {signal.TakeProfits}
                    Provider: {signal.Provider}
                    Scores: confidence {prediction.ConfidenceScore:0.#}% | TP1 probability {tp1Prob}% | stoploss probability {prediction.StoplossProbability:0.#}% | provider accuracy {prediction.ProviderAccuracyScore:0.#}% ({prediction.ProviderSampleSize} samples) | market alignment {prediction.MarketAlignmentScore:0.#}% | volatility fit {prediction.VolatilityFitScore:0.#}% | history {prediction.HistoricalSampleSize} signals
                    """;

                var client = _httpClientFactory.CreateClient();
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

                var requestBody = new
                {
                    model = "gpt-4o-mini",
                    messages = new[]
                    {
                        new { role = "system", content = BuildNarrativeSystemPrompt() },
                        new { role = "user",   content = userPrompt }
                    },
                    max_tokens = 160,
                    temperature = 0.4
                };

                var json = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await client.PostAsync(
                    "https://models.inference.ai.azure.com/chat/completions", content, cts.Token);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("AI narrative request failed ({Status}) for signal {SignalId}.",
                        response.StatusCode, signal.Id);
                    return null;
                }

                var responseText = await response.Content.ReadAsStringAsync(cts.Token);
                var completion = JsonSerializer.Deserialize<JsonElement>(responseText);
                return completion
                    .GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString()
                    ?.Trim();
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("AI narrative generation timed out for signal {SignalId}.", signal.Id);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AI narrative generation failed for signal {SignalId}.", signal.Id);
                return null;
            }
        }

        private static string BuildNarrativeSystemPrompt() => """
            You are a concise crypto trading signal analyst. Given a signal's details and its statistical prediction scores, write 2-3 sentences of plain-English analysis.

            Sentence 1: What the confidence and TP1 probability indicate about the likely outcome.
            Sentence 2: The single most important risk factor or market condition for this trade.
            Sentence 3 (optional): One specific thing the trader should watch for before or during the trade.

            Rules:
            - Be specific to the numbers given; do not restate them verbatim
            - No disclaimers, no "past performance" boilerplate
            - 60–90 words total, plain text only, no markdown or bullet points
            """;

        public async Task<(List<SuggestedParsingRule>? Rules, string? Error)> GenerateRulesAsync(
            IEnumerable<string> exampleMessages)
        {
            var token = _configuration["GitHub:ModelsToken"];
            if (string.IsNullOrWhiteSpace(token))
                return (null, "GitHub Models token is not configured. Add 'GitHub:ModelsToken' to appsettings.json. " +
                              "Create a PAT at https://github.com/settings/tokens (no scopes required).");

            var examples = exampleMessages
                .Where(e => !string.IsNullOrWhiteSpace(e))
                .Select(e => MessageSanitizer.SanitizeMessage(e))
                .ToList();
            if (examples.Count == 0)
                return (null, "Please provide at least one example signal message.");

            var systemPrompt = BuildSystemPrompt();
            var userPrompt = BuildUserPrompt(examples);

            try
            {
                var client = _httpClientFactory.CreateClient();
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

                var requestBody = new
                {
                    model = "gpt-4o",
                    response_format = new { type = "json_object" },
                    messages = new[]
                    {
                        new { role = "system", content = systemPrompt },
                        new { role = "user",   content = userPrompt }
                    },
                    temperature = 0.2
                };

                var json = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await client.PostAsync(
                    "https://models.inference.ai.azure.com/chat/completions", content);
                var responseText = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("GitHub Models API error {Status}: {Body}", response.StatusCode, responseText);
                    return (null, $"GitHub Models API error ({response.StatusCode}): {ExtractOpenAIError(responseText)}");
                }

                var completion = JsonSerializer.Deserialize<JsonElement>(responseText);
                var messageContent = completion
                    .GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString();

                if (string.IsNullOrWhiteSpace(messageContent))
                    return (null, "GitHub Models returned an empty response.");

                return ParseRulesFromJson(messageContent);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calling GitHub Models API");
                return (null, $"Error calling GitHub Models: {ex.Message}");
            }
        }

        private (List<SuggestedParsingRule>? Rules, string? Error) ParseRulesFromJson(string json)
        {
            try
            {
                var doc = JsonSerializer.Deserialize<JsonElement>(json);

                // The root may be { "rules": [...] } or directly [...]
                JsonElement rulesElement;
                if (doc.ValueKind == JsonValueKind.Array)
                {
                    rulesElement = doc;
                }
                else if (doc.TryGetProperty("rules", out var prop))
                {
                    rulesElement = prop;
                }
                else
                {
                    // Try to find the first array property
                    foreach (var p in doc.EnumerateObject())
                    {
                        if (p.Value.ValueKind == JsonValueKind.Array)
                        {
                            rulesElement = p.Value;
                            goto found;
                        }
                    }
                    return (null, "Could not locate a rules array in the AI response.");
                    found:;
                }

                var rules = new List<SuggestedParsingRule>();
                int order = 1;

                foreach (var item in rulesElement.EnumerateArray())
                {
                    var rule = new SuggestedParsingRule
                    {
                        RuleType = GetString(item, "ruleType") ?? GetString(item, "RuleType") ?? "",
                        RegexPattern = GetString(item, "regexPattern") ?? GetString(item, "RegexPattern") ?? "",
                        RegexGroupName = GetString(item, "regexGroupName") ?? GetString(item, "RegexGroupName") ?? "",
                        FallbackValue = GetString(item, "fallbackValue") ?? GetString(item, "FallbackValue"),
                        IsRequired = GetBool(item, "isRequired") ?? GetBool(item, "IsRequired") ?? true,
                        ValidationLogic = GetString(item, "validationLogic") ?? GetString(item, "ValidationLogic"),
                        Notes = GetString(item, "notes") ?? GetString(item, "Notes"),
                        Selected = true
                    };

                    // Use provided order if present, otherwise auto-assign
                    var providedOrder = GetInt(item, "order") ?? GetInt(item, "Order");
                    rule.Order = providedOrder ?? order;
                    order = rule.Order + 1;

                    // Basic validation — skip invalid rules silently
                    if (string.IsNullOrWhiteSpace(rule.RuleType) ||
                        string.IsNullOrWhiteSpace(rule.RegexPattern))
                        continue;

                    // Validate the regex actually compiles
                    try { _ = new Regex(rule.RegexPattern); }
                    catch
                    {
                        rule.Notes = $"⚠ Regex compile error — review before saving. {rule.Notes}".Trim();
                    }

                    rules.Add(rule);
                }

                if (rules.Count == 0)
                    return (null, "AI returned a valid JSON structure but no recognisable rules.");

                // Normalize: empty FallbackValue → null; RegexGroupName → always lowercase
                foreach (var r in rules)
                {
                    r.FallbackValue = string.IsNullOrWhiteSpace(r.FallbackValue) ? null : r.FallbackValue;
                    r.RegexGroupName = r.RegexGroupName.ToLowerInvariant();
                }

                // Enforce mandatory fields regardless of what the AI returned
                var mandatoryTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Symbol", "Side", "Entry" };
                foreach (var r in rules)
                {
                    if (mandatoryTypes.Contains(r.RuleType))
                        r.IsRequired = true;
                }

                // First TakeProfit (lowest order) is mandatory; subsequent ones are optional
                var tpRules = rules
                    .Where(r => string.Equals(r.RuleType, "TakeProfit", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(r => r.Order)
                    .ToList();
                if (tpRules.Count > 0)
                {
                    tpRules[0].IsRequired = true;
                    foreach (var tp in tpRules.Skip(1))
                        tp.IsRequired = false;
                }

                return (rules, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing AI JSON response: {Json}", json);
                return (null, $"Could not parse the AI response as JSON: {ex.Message}");
            }
        }

        private static string? GetString(JsonElement el, string key)
        {
            if (el.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.String)
                return p.GetString();
            return null;
        }

        private static bool? GetBool(JsonElement el, string key)
        {
            if (!el.TryGetProperty(key, out var p)) return null;
            if (p.ValueKind == JsonValueKind.True)  return true;
            if (p.ValueKind == JsonValueKind.False) return false;
            if (p.ValueKind == JsonValueKind.String &&
                bool.TryParse(p.GetString(), out var b)) return b;
            return null;
        }

        private static int? GetInt(JsonElement el, string key)
        {
            if (!el.TryGetProperty(key, out var p)) return null;
            if (p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var i)) return i;
            if (p.ValueKind == JsonValueKind.String && int.TryParse(p.GetString(), out var s)) return s;
            return null;
        }

        private static string ExtractOpenAIError(string json)
        {
            try
            {
                var doc = JsonSerializer.Deserialize<JsonElement>(json);
                if (doc.TryGetProperty("error", out var err) &&
                    err.TryGetProperty("message", out var msg))
                    return msg.GetString() ?? json;
            }
            catch { }
            return json.Length > 200 ? json[..200] : json;
        }

        private static string BuildSystemPrompt() => """
            You are an expert at writing .NET regular expressions for parsing cryptocurrency trading signals sent via Telegram.

            ## Rule Types
            Each signal provider has a set of named parsing rules. The rule types you must produce are:
            - **Symbol**    – The trading pair, e.g. BTC/USDT, ETH/USDT
            - **Side**      – Trade direction: long / short / buy / sell
            - **Entry**     – Entry price (decimal number)
            - **TakeProfit**– Take-profit target(s). Create ONE rule per TP level found in the examples.
            - **Stoploss**  – Stop-loss price (decimal number)
            - **Leverage**  – Leverage multiplier, e.g. 10x → capture "10"

            ## Rule Schema (JSON)
            Each rule is an object with these fields:
            {
              "ruleType":       string,          // one of the types above
              "regexPattern":   string,          // .NET regex with a named capture group
              "regexGroupName": string,          // name of the primary capture group
              "fallbackValue":  string | null,   // default when no match (null means required)
              "isRequired":     boolean,         // true = signal invalid without it
              "order":          integer,         // processing order; Symbol first, TPs consecutive
              "validationLogic":string | null,   // optional JSON validation (see below)
              "notes":          string | null    // brief explanation of the pattern
            }

            ## Naming Conventions
            Use lowercase group names that match the ruleType:
            - Symbol    → (?<symbol>...)
            - Side      → (?<side>...)
            - Entry     → (?<entry>...)
            - TakeProfit→ (?<takeprofit>...)   (one capture group per rule)
            - Stoploss  → (?<stoploss>...)
            - Leverage  → (?<leverage>...)

            ## Order Convention
            Symbol=1, Side=2, Entry or Leverage next, then TakeProfit rules consecutively, Stoploss last.

            ## Required vs Optional Rules
            - Symbol, Side, Entry: always set isRequired=true, fallbackValue=null
            - TakeProfit TP1 (the first/lowest numbered target): isRequired=true, fallbackValue=null
            - TakeProfit TP2 and higher: isRequired=false, fallbackValue=null
            - Stoploss: isRequired=true if present in every example, otherwise isRequired=false
            - Leverage: isRequired=false; set fallbackValue="1" when not consistently present

            ## ValidationLogic Examples
            Side validation:
            [{"Operator":"in","Value":["long","short","buy","sell","Long","Short","Buy","Sell","LONG","SHORT","BUY","SELL"],"ErrorMessage":"Side must be long/short"}]

            Symbol validation (if format is always COIN/USDT):
            [{"Operator":"regex","Value":"^[A-Z]+/USDT$","ErrorMessage":"Symbol must be in format: BTC/USDT"}]

            Leverage range:
            [{"Operator":"range","Value":"1-100","ErrorMessage":"Leverage must be 1–100"}]

            Numeric range (entry/stoploss/takeprofit):
            [{"Operator":"min","Value":0,"ErrorMessage":"Must be positive"},{"Operator":"max","Value":1000000,"ErrorMessage":"Too large"}]

            Set validationLogic to null when no validation is needed.

            ## Output Format
            Return ONLY a JSON object with a single key "rules" whose value is an array of rule objects:
            { "rules": [ { ... }, { ... } ] }
            """;

        private static string BuildUserPrompt(List<string> examples)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Analyse the following trading signal examples and generate the complete set of parsing rules.");
            sb.AppendLine("Cover every field visible across the examples (Symbol, Side, Entry, TakeProfit levels, Stoploss, Leverage).");
            sb.AppendLine("If a field is not present in the examples, omit that rule type.");
            sb.AppendLine("For TakeProfit, create a separate rule for EACH numbered target level found.");
            sb.AppendLine();
            sb.AppendLine("## Example Signals");

            for (int i = 0; i < examples.Count; i++)
            {
                sb.AppendLine($"### Example {i + 1}");
                sb.AppendLine("```");
                sb.AppendLine(examples[i]);
                sb.AppendLine("```");
            }

            sb.AppendLine();
            sb.AppendLine("Return ONLY the JSON object with the \"rules\" array. Do not include any explanation outside the JSON.");

            return sb.ToString();
        }
    }
}
