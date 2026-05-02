using AutoSignals.Models;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace AutoSignals.Services
{
    /// <summary>
    /// Sends a chart image to a GPT-4o vision endpoint and extracts a trading signal.
    /// Extracts entry zone, stop-loss and the final TP from the chart, then generates
    /// at least 3 intermediate TP levels between entry and the final TP.
    /// </summary>
    public class ImageSignalParserService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<ImageSignalParserService> _logger;

        public const string DefaultSystemPrompt = """
            You are a professional technical analyst specializing in price action and support/resistance trading.

            Analyze the provided candlestick chart and identify a high-probability trade setup based on these rules:

            1. Identify if the asset is in a consolidation range (sideways movement).
            2. Detect clear support (bottom of range) and resistance (top of range).
            3. If price is near the upper boundary and showing breakout potential → return a LONG signal.
            4. If price is near the lower boundary and showing breakdown potential → return a SHORT signal.
            5. Entry should be near the current price or breakout level.
            6. Stoploss must be placed just beyond the nearest invalidation level:
               - Below support for LONG
               - Above resistance for SHORT
            7. Define between 4 and 9 take profit targets (minimum 4):
               - Evenly spaced between entry and the major resistance (for LONG)
               - Evenly spaced between entry and the major support (for SHORT)
            8. Use realistic price levels visible on the chart (not arbitrary values).
            9. Extract the trading pair and return ONLY the base asset symbol (e.g., BTC from BTCUSDT).
            10. Default leverage = 3 unless a strong breakout justifies higher (max 5).

            Return ONLY valid JSON in this exact format:

            {
              "symbol": "<BASE asset only>",
              "side": "<long or short>",
              "entry": <number>,
              "stoploss": <number>,
              "takeprofits": [<tp1>, <tp2>, <tp3>, <tp4>, ...],
              "leverage": <number>
            }

            If you cannot confidently determine a valid setup, return:
            { "signal": false }

            Otherwise, ALWAYS return valid JSON with all fields filled.
            Do not omit takeprofits.
            """;

        public ImageSignalParserService(
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            ILogger<ImageSignalParserService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _logger = logger;
        }

        /// <summary>
        /// Parses a chart image and returns a <see cref="Signal"/> with auto-generated TP ladder,
        /// or <c>null</c> if no valid signal could be extracted.
        /// </summary>
        /// <param name="customPrompt">
        /// Optional system prompt override. When null or empty the built-in
        /// <see cref="DefaultSystemPrompt"/> is used.
        /// </param>
        public async Task<Signal?> ParseFromImageAsync(
            byte[] imageBytes,
            string providerName,
            string? customPrompt = null,
            CancellationToken cancellationToken = default)
        {
            var token = _configuration["GitHub:ModelsToken"];
            if (string.IsNullOrWhiteSpace(token))
            {
                _logger.LogWarning("ImageSignalParser: GitHub:ModelsToken not configured — image parsing disabled.");
                return null;
            }

            if (imageBytes == null || imageBytes.Length == 0)
                return null;

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(30));

            try
            {
                var base64Image = Convert.ToBase64String(imageBytes);
                var mimeType = DetectMimeType(imageBytes);

                var client = _httpClientFactory.CreateClient();
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

                var systemPrompt = string.IsNullOrWhiteSpace(customPrompt) ? DefaultSystemPrompt : customPrompt;

                var requestBody = new
                {
                    model = "gpt-4o",
                    messages = new object[]
                    {
                        new { role = "system", content = systemPrompt },
                        new
                        {
                            role = "user",
                            content = new object[]
                            {
                                new
                                {
                                    type = "image_url",
                                    image_url = new { url = $"data:{mimeType};base64,{base64Image}" }
                                }
                            }
                        }
                    },
                    max_tokens = 400,
                    temperature = 0.1
                };

                var json = JsonSerializer.Serialize(requestBody);
                var httpContent = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await client.PostAsync(
                    "https://models.inference.ai.azure.com/chat/completions", httpContent, cts.Token);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("ImageSignalParser: HTTP {Status} for provider '{Provider}'",
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

                var cleaned = rawText.Trim();
                if (cleaned.StartsWith("```"))
                {
                    var firstNewline = cleaned.IndexOf('\n');
                    var lastFence = cleaned.LastIndexOf("```");
                    if (firstNewline >= 0 && lastFence > firstNewline)
                        cleaned = cleaned[(firstNewline + 1)..lastFence].Trim();
                }

                // --- PARSE JSON ---
                using var parsed = JsonDocument.Parse(cleaned);
                var root = parsed.RootElement;

                // OPTIONAL: allow model to explicitly say "no signal"
                if (root.TryGetProperty("signal", out var sigFlag) &&
                    sigFlag.ValueKind == JsonValueKind.False)
                {
                    _logger.LogDebug("ImageSignalParser: no valid signal detected for provider '{Provider}'", providerName);
                    return null;
                }

                // --- SYMBOL ---
                var symbolRaw = GetString(root, "symbol");
                if (string.IsNullOrWhiteSpace(symbolRaw))
                    return null;

                var normalized = symbolRaw.Trim().ToUpperInvariant()
                    .Replace("/", "").Replace("-", "").Replace("USDT", "");

                var symbol = $"{normalized}/USDT:USDT";

                // --- SIDE ---
                var side = GetString(root, "side")?.ToLowerInvariant();
                if (side != "long" && side != "short")
                    side = "long";

                // --- ENTRY ---
                double entry = GetDouble(root, "entry");

                // backward compatibility
                if (entry <= 0)
                {
                    var entryMin = GetDouble(root, "entryMin");
                    var entryMax = GetDouble(root, "entryMax");

                    if (entryMin > 0 && entryMax > 0)
                        entry = (entryMin + entryMax) / 2.0;
                    else
                        entry = entryMin > 0 ? entryMin : entryMax;
                }

                if (entry <= 0)
                    return null;

                // --- STOPLOSS ---
                var stoploss = GetDouble(root, "stoploss");
                if (stoploss <= 0)
                {
                    stoploss = side == "short"
                        ? entry * 1.10
                        : entry * 0.90;
                }

                // --- TAKE PROFITS ---
                List<float> takeProfits = new();

                // NEW FORMAT (preferred)
                if (root.TryGetProperty("takeprofits", out var tpsElement) &&
                    tpsElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var tp in tpsElement.EnumerateArray())
                    {
                        if (tp.TryGetDouble(out var val) && val > 0)
                            takeProfits.Add((float)val);
                    }
                }

                // BACKWARD COMPATIBILITY (old format)
                if (takeProfits.Count == 0)
                {
                    var tpZoneMin = GetDouble(root, "tpZoneMin");
                    var tpZoneMax = GetDouble(root, "tpZoneMax");

                    if (tpZoneMax <= 0)
                        tpZoneMax = GetDouble(root, "finalTp");

                    if (tpZoneMax > 0)
                    {
                        takeProfits = GenerateIntermediateTps(entry, tpZoneMin, tpZoneMax, side);
                    }
                }

                // FINAL VALIDATION
                if (takeProfits.Count == 0)
                {
                    _logger.LogWarning("ImageSignalParser: no valid take profits extracted");
                    return null;
                }

                // Ensure between 4 and 9 TPs
                takeProfits = takeProfits
                    .Where(tp => tp > 0)
                    .Distinct()
                    .Take(9)
                    .ToList();

                if (takeProfits.Count < 4)
                    return null;

                // --- LEVERAGE ---
                var leverage = GetInt(root, "leverage", 3);
                var takeProfitsCsv = string.Join(",", takeProfits.Select(tp =>
                    ((decimal)tp).ToString("0.########", CultureInfo.InvariantCulture)));

                // --- BUILD SIGNAL ---
                var signal = new Signal
                {
                    Symbol = symbol,
                    Side = side,
                    Entry = (float)entry,
                    Stoploss = (float)stoploss,
                    TakeProfits = takeProfitsCsv,
                    Leverage = leverage,
                    Provider = providerName,
                    Time = DateTime.UtcNow
                };

                _logger.LogInformation(
                    "ImageSignalParser: {Symbol} {Side} Entry={Entry} SL={SL} TPs={TPs} Provider='{Provider}'",
                    signal.Symbol, signal.Side, signal.Entry, signal.Stoploss,
                    signal.TakeProfits, providerName);

                return signal;
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("ImageSignalParser: timed out for provider '{Provider}'", providerName);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ImageSignalParser: error for provider '{Provider}'", providerName);
                return null;
            }
        }

        /// <summary>
        /// Distributes 4 TP levels evenly across the blue-bar zone
        /// [<paramref name="tpZoneMin"/> .. <paramref name="tpZoneMax"/>].
        /// When no valid zone is available, falls back to 4 TPs from entry to the
        /// available bound.
        /// </summary>
        private static List<float> GenerateIntermediateTps(
            double entry, double tpZoneMin, double tpZoneMax, string side)
        {
            const int count  = 4;
            var isShort      = string.Equals(side, "short", StringComparison.OrdinalIgnoreCase);
            var tps          = new List<double>(count);

            var hasZone = tpZoneMin > 0 && tpZoneMax > 0
                          && Math.Abs(tpZoneMax - tpZoneMin) > double.Epsilon;

            if (hasZone)
            {
                // Evenly space 4 TPs from the bottom to the top of the blue bar
                // TP1 = tpZoneMin  (first price target reached)
                // TP4 = tpZoneMax  (ultimate target, top of the blue bar)
                var zoneRange = tpZoneMax - tpZoneMin;
                for (int i = 0; i < count; i++)
                    tps.Add(tpZoneMin + zoneRange * i / (count - 1));
            }
            else
            {
                // Fallback: generate from entry to whichever zone bound is available
                var target = tpZoneMax > 0 ? tpZoneMax : tpZoneMin;
                if (target <= 0) return new List<float>();

                var range = target - entry;
                for (int i = 1; i <= count; i++)
                    tps.Add(entry + range * i / count);
            }

            tps = isShort
                ? tps.OrderByDescending(t => t).ToList()
                : tps.OrderBy(t => t).ToList();

            return tps.Select(t => (float)t).ToList();
        }

        private static string DetectMimeType(byte[] bytes)
        {
            if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
                return "image/jpeg";
            if (bytes.Length >= 4 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
                return "image/png";
            if (bytes.Length >= 6 && bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46)
                return "image/gif";
            return "image/jpeg";
        }

        private static string? GetString(JsonElement el, string key)
        {
            if (el.TryGetProperty(key, out var prop) && prop.ValueKind == JsonValueKind.String)
                return prop.GetString();
            return null;
        }

        private static double GetDouble(JsonElement el, string key)
        {
            if (!el.TryGetProperty(key, out var prop)) return 0.0;
            return prop.ValueKind switch
            {
                JsonValueKind.Number => prop.GetDouble(),
                JsonValueKind.String => double.TryParse(prop.GetString(),
                    NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0.0,
                _ => 0.0
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
