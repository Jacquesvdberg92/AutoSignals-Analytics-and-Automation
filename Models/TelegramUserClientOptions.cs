namespace AutoSignals.Models
{
    public class TelegramUserClientOptions
    {
        public const string SectionName = "TelegramUserClient";

        public int ApiId { get; set; }
        public string ApiHash { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;

        /// <summary>
        /// File path (without extension) used to persist the WTelegramClient session.
        /// Defaults to "telegram_user_session" in the working directory.
        /// </summary>
        public string SessionPath { get; set; } = "telegram_user_session";

        /// <summary>
        /// Optional: 2FA cloud password if your account has two-step verification enabled.
        /// Leave empty to be prompted via the admin UI.
        /// </summary>
        public string? Password { get; set; }
    }
}
