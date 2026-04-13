using System.ComponentModel.DataAnnotations;

namespace AutoSignals.ViewModels
{
    public class TestSequenceViewModel : IValidatableObject
    {
        [Required]
        public string Exchange { get; set; } = "BITGET";

        [Required]
        [RegularExpression("^(buy|sell)$", ErrorMessage = "Direction must be 'buy' or 'sell'.")]
        public string Direction { get; set; } = "buy";

        [Required(ErrorMessage = "API Key is required.")]
        public string ApiKey { get; set; } = "";

        [Required(ErrorMessage = "API Secret is required.")]
        public string ApiSecret { get; set; } = "";

        // Only required for exchanges that use a passphrase (Bitget, OKX, KuCoin)
        public string? Password { get; set; }

        public List<string> Logs { get; set; } = new();

        public bool IsCompleted { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            var exchange = (Exchange ?? "").Trim().ToUpperInvariant();
            var needsPassphrase = exchange is "BITGET" or "OKX" or "KUCOIN";

            if (needsPassphrase && string.IsNullOrWhiteSpace(Password))
                yield return new ValidationResult(
                    "Passphrase is required for Bitget, OKX, and KuCoin.",
                    new[] { nameof(Password) });
        }
    }
}
