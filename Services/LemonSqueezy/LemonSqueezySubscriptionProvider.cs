using AutoSignals.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace AutoSignals.Services.LemonSqueezy
{
    public class LemonSqueezySubscriptionProvider : ISubscriptionProvider
    {
        public string ProviderName => "LemonSqueezy";

        private readonly HttpClient _http;
        private readonly LemonSqueezyOptions _options;
        private readonly AutoSignalsDbContext _context;
        private readonly ILogger<LemonSqueezySubscriptionProvider> _logger;

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public LemonSqueezySubscriptionProvider(
            HttpClient http,
            IOptions<LemonSqueezyOptions> options,
            AutoSignalsDbContext context,
            ILogger<LemonSqueezySubscriptionProvider> logger)
        {
            _http = http;
            _options = options.Value;
            _context = context;
            _logger = logger;

            _http.BaseAddress = new Uri("https://api.lemonsqueezy.com/v1/");
            _http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _options.ApiKey);
            _http.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/vnd.api+json"));
        }

        /// <inheritdoc />
        /// <param name="userId">AutoSignals user ID embedded as custom checkout data.</param>
        /// <param name="planId">AutoSignals <see cref="Models.SubscriptionPlan.Id"/>.</param>
        /// <param name="successUrl">URL Lemon Squeezy redirects to on payment completion.</param>
        /// <param name="cancelUrl">URL Lemon Squeezy redirects to when the buyer closes the overlay.</param>
        /// <returns>The hosted checkout URL to redirect the user to.</returns>
        public async Task<string> CreateCheckoutSessionAsync(
            string userId, int planId, string successUrl, string cancelUrl)
        {
            var plan = await _context.SubscriptionPlans
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == planId && p.IsActive);

            if (plan == null)
                throw new InvalidOperationException($"SubscriptionPlan {planId} not found or inactive.");

            if (string.IsNullOrWhiteSpace(plan.LemonSqueezyVariantId))
                throw new InvalidOperationException($"SubscriptionPlan {planId} has no LemonSqueezyVariantId configured.");

            var body = new
            {
                data = new
                {
                    type = "checkouts",
                    attributes = new
                    {
                        checkout_data = new
                        {
                            custom = new Dictionary<string, string>
                            {
                                ["user_id"] = userId,
                                ["plan_id"] = planId.ToString()
                            }
                        },
                        product_options = new
                        {
                            redirect_url = successUrl
                        }
                    },
                    relationships = new
                    {
                        store = new
                        {
                            data = new { type = "stores", id = _options.StoreId }
                        },
                        variant = new
                        {
                            data = new { type = "variants", id = plan.LemonSqueezyVariantId }
                        }
                    }
                }
            };

            var json = JsonSerializer.Serialize(body);
            var content = new StringContent(json, Encoding.UTF8, "application/vnd.api+json");

            var response = await _http.PostAsync("checkouts", content);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("LemonSqueezy checkout creation failed. Status={Status} Body={Body}",
                    response.StatusCode, responseBody);
                throw new InvalidOperationException(
                    $"LemonSqueezy {(int)response.StatusCode}: {responseBody}");
            }

            using var doc = JsonDocument.Parse(responseBody);
            var checkoutUrl = doc.RootElement
                .GetProperty("data")
                .GetProperty("attributes")
                .GetProperty("url")
                .GetString();

            return checkoutUrl ?? throw new InvalidOperationException("LemonSqueezy returned no checkout URL.");
        }

        /// <summary>
        /// Returns the customer-portal URL for the given user.
        /// LemonSqueezy embeds the portal URL in the customer resource.
        /// </summary>
        public async Task<string> GetBillingPortalUrlAsync(string userId, string returnUrl)
        {
            var userData = await _context.UsersData.AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (string.IsNullOrWhiteSpace(userData?.LemonSqueezyCustomerId))
            {
                // Fall back to the generic customer portal
                return "https://app.lemonsqueezy.com/my-orders";
            }

            var response = await _http.GetAsync($"customers/{userData.LemonSqueezyCustomerId}");
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("LemonSqueezy customer fetch failed. Status={Status}", response.StatusCode);
                return "https://app.lemonsqueezy.com/my-orders";
            }

            using var doc = JsonDocument.Parse(responseBody);
            var portalUrl = doc.RootElement
                .GetProperty("data")
                .GetProperty("attributes")
                .GetProperty("urls")
                .GetProperty("customer_portal")
                .GetString();

            return portalUrl ?? "https://app.lemonsqueezy.com/my-orders";
        }

        /// <summary>
        /// Cancels a LemonSqueezy subscription at the end of the current billing period.
        /// </summary>
        public async Task CancelSubscriptionAsync(string externalSubscriptionId)
        {
            if (string.IsNullOrWhiteSpace(externalSubscriptionId))
                return;

            var body = JsonSerializer.Serialize(new
            {
                data = new
                {
                    type = "subscriptions",
                    id = externalSubscriptionId,
                    attributes = new { cancelled = true }
                }
            });

            var content = new StringContent(body, Encoding.UTF8, "application/vnd.api+json");
            var request = new HttpRequestMessage(HttpMethod.Patch, $"subscriptions/{externalSubscriptionId}")
            {
                Content = content
            };

            var response = await _http.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync();
                _logger.LogError("LemonSqueezy subscription cancel failed. Id={Id} Status={Status} Body={Body}",
                    externalSubscriptionId, response.StatusCode, err);
            }
        }
    }
}
