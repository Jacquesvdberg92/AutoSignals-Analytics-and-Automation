using AutoSignals.Models;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace AutoSignals.Services
{
    public class AiSignalParserService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<AiSignalParserService> _logger;

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private const string SystemPrompt = """
            You are a crypto trading signal parser. Extract structured data from the raw signal message.
            Return ONLY a JSON object (no markdown fences, no explanation) with these fields:
            {
              "symbol": "<BASE asset + /USDT:USDT, e.g. BTC>",
              "side": "<long or short>",
              "entry": <number or null>,
              "stoploss": <number or null>,
              "takeProfits": "<comma-separated numbers or empty string>",
              "leverage": <integer or null>
            }
            Rules:
            - symbol: uppercase base asset only (strip USDT/USDC/BTC quote). If unclear, return null.
            - side: must be exactly "long" or "short" (lowercase). If unclear, default to "long".
            - entry: the entry/open price as a number. null if not found.
            - stoploss: the stop loss price. null if not found.
            - takeProfits: comma-separated TP prices in ascending order for long, descending for short. Empty string if none.
            - leverage: integer multiplier (e.g. 10). null if not specified.
            If the message does not contain a valid trading signal, return: {"signal": false}
            """;

        public AiSignalParserService(
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            ILogger<AiSignalParserService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<Signal?> ParseSignalAsync(string message, string providerName, CancellationToken cancellationToken = default)
        {
            var token = _configuration["GitHub:ModelsToken"];
            if (string.IsNullOrWhiteSpace(token))
                return null;

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(20));

            try
            {
                var client = _httpClientFactory.CreateClient();
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

                var requestBody = new
                {
                    model = "gpt-4o-mini",
                    messages = new[]
                    {
                        new { role = "system", content = SystemPrompt },
                        new { role = "user",   content = message }
                    },
                    max_tokens = 200,
                    temperature = 0.1
                };

                var json = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await client.PostAsync(
                    "https://models.inference.ai.azure.com/chat/completions", content, cts.Token);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("AI parser HTTP {Status} for provider '{Provider}'",
                        response.StatusCode, providerName);
                    return null;
                }

                var responseBody = await response.Content.ReadAsStringAsync(cts.Token);
                using var doc = JsonDocument.Parse(responseBody);

                var rawText = doc.RootElement
                    .GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString();

                if (string.IsNullOrWhiteSpace(rawText))
                    return null;

                // Strip markdown code fences if present
                var cleaned = rawText.Trim();
                if (cleaned.StartsWith("```"))
                {
                    var firstNewline = cleaned.IndexOf('\n');
                    var lastFence = cleaned.LastIndexOf("```");
                    if (firstNewline >= 0 && lastFence > firstNewline)
                        cleaned = cleaned[(firstNewline + 1)..lastFence].Trim();
                }

                using var parsed = JsonDocument.Parse(cleaned);
                var root = parsed.RootElement;

                // Model indicated no valid signal
                if (root.TryGetProperty("signal", out var sigFlag) &&
                    sigFlag.ValueKind == JsonValueKind.False)
                {
                    _logger.LogDebug("AI parser: no valid signal detected for provider '{Provider}'", providerName);
                    return null;
                }

                var symbol = GetString(root, "symbol");
                if (string.IsNullOrWhiteSpace(symbol))
                    return null;

                // Normalize symbol → BASE/USDT:USDT
                var normalized = symbol.Trim().ToUpperInvariant()
                    .Replace("/", "").Replace("-", "").Replace("USDT", "");
                symbol = $"{normalized}/USDT:USDT";

                var side = GetString(root, "side")?.ToLowerInvariant() ?? "long";
                if (side != "long" && side != "short") side = "long";

                var entry = GetFloat(root, "entry");
                var stoploss = GetFloat(root, "stoploss");

                if (stoploss <= 0f || stoploss == entry)
                {
                    stoploss = side == "short"
                        ? entry * 1.10f
                        : entry * 0.90f;
                }

                var leverage = GetInt(root, "leverage", 3);
                var takeProfits = GetString(root, "takeProfits") ?? string.Empty;

                var signal = new Signal
                {
                    Symbol = symbol,
                    Side = side,
                    Entry = entry,
                    Stoploss = stoploss,
                    TakeProfits = takeProfits,
                    Leverage = leverage,
                    Provider = providerName,
                    Time = DateTime.UtcNow
                };

                _logger.LogInformation("AI parser extracted signal: {Symbol} {Side} from provider '{Provider}'",
                    signal.Symbol, signal.Side, providerName);

                return signal;
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("AI parser timed out for provider '{Provider}'", providerName);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AI parser error for provider '{Provider}'", providerName);
                return null;
            }
        }

        private static string? GetString(JsonElement el, string key)
        {
            if (el.TryGetProperty(key, out var prop) && prop.ValueKind == JsonValueKind.String)
                return prop.GetString();
            return null;
        }

        private static float GetFloat(JsonElement el, string key)
        {
            if (!el.TryGetProperty(key, out var prop)) return 0f;
            return prop.ValueKind switch
            {
                JsonValueKind.Number => (float)prop.GetDouble(),
                JsonValueKind.String => float.TryParse(prop.GetString(),
                    NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0f,
                _ => 0f
            };
        }

        private static int GetInt(JsonElement el, string key, int defaultValue)
        {
            if (!el.TryGetProperty(key, out var prop)) return defaultValue;
            return prop.ValueKind switch
            {
                JsonValueKind.Number => prop.TryGetInt32(out var v) ? v : defaultValue,
                JsonValueKind.String => int.TryParse(prop.GetString(), out var v) ? v : defaultValue,
                _ => defaultValue
            };
        }
    }
}
