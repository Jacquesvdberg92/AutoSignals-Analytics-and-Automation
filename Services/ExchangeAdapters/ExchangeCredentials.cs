namespace AutoSignals.Services.ExchangeAdapters
{
    public sealed class ExchangeCredentials
    {
        public ExchangeCredentials(string apiKey, string apiSecret, string? passphrase = null)
        {
            ApiKey = apiKey;
            ApiSecret = apiSecret;
            Passphrase = passphrase;
        }

        public string ApiKey { get; }
        public string ApiSecret { get; }
        public string? Passphrase { get; }
    }
}
