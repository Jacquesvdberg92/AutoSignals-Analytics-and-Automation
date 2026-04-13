namespace AutoSignals.Models
{
    public class UserExchangeConnection
    {
        public int Id { get; set; }

        public string UserId { get; set; } = default!;

        public int ExchangeId { get; set; }
        public Exchange? Exchange { get; set; }

        /// <summary>User-defined label, e.g. "Main Bitget", "OKX Scalper"</summary>
        public string? Label { get; set; }

        // AES-256 encrypted via AesEncryptionService (same pattern as UserData)
        public string? ApiKey { get; set; }
        public string? ApiSecret { get; set; }
        public string? ApiPassword { get; set; }

        /// <summary>true = this connection is used when no explicit ConnectionId is set on ProviderSettings</summary>
        public bool IsDefault { get; set; } = false;

        /// <summary>User can disable a connection without deleting it</summary>
        public bool IsActive { get; set; } = true;

        /// <summary>"1" = last test passed, "0" = last test failed, null = never tested</summary>
        public string? TestResult { get; set; }
        public DateTime? LastTestedAt { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
