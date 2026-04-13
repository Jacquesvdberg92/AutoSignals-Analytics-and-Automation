namespace AutoSignals.Services.LemonSqueezy
{
    public class LemonSqueezyOptions
    {
        public const string SectionName = "LemonSqueezy";

        /// <summary>LemonSqueezy Store ID (numeric string).</summary>
        public string StoreId { get; set; } = string.Empty;

        /// <summary>LemonSqueezy API Key — keep in User Secrets / env vars, never in appsettings.json.</summary>
        public string ApiKey { get; set; } = string.Empty;

        /// <summary>Webhook signing secret — keep in User Secrets / env vars.</summary>
        public string WebhookSecret { get; set; } = string.Empty;
    }
}
