using Microsoft.Extensions.Configuration;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Text.Json.Serialization;

public class RecaptchaService
{
    private readonly string _secretKey;
    private readonly HttpClient _httpClient;

    public RecaptchaService(IConfiguration configuration, IHttpClientFactory httpClientFactory)
    {
        _secretKey = configuration["Recaptcha:SecretKey"];
        _httpClient = httpClientFactory.CreateClient();
    }

    public async Task<RecaptchaVerifyResponse?> VerifyAsyncFull(string recaptchaResponse)
    {
        if (string.IsNullOrEmpty(recaptchaResponse))
            return null;

        var response = await _httpClient.PostAsync(
            $"https://www.google.com/recaptcha/api/siteverify?secret={_secretKey}&response={recaptchaResponse}",
            null);

        var json = await response.Content.ReadAsStringAsync();

        // Log the raw response for debugging
        Console.WriteLine("reCAPTCHA raw response: " + json);

        if (!response.IsSuccessStatusCode)
            return null;

        return JsonSerializer.Deserialize<RecaptchaVerifyResponse>(json);
    }

    public class RecaptchaVerifyResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("score")]
        public double Score { get; set; }

        [JsonPropertyName("action")]
        public string Action { get; set; }

        [JsonPropertyName("challenge_ts")]
        public string Challenge_ts { get; set; }

        [JsonPropertyName("hostname")]
        public string Hostname { get; set; }

        [JsonPropertyName("error-codes")]
        public string[] ErrorCodes { get; set; }
    }
}
