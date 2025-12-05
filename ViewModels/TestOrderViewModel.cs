using System.ComponentModel.DataAnnotations;

namespace AutoSignals.ViewModels
{
    public class TestOrderViewModel : IValidatableObject
    {
        [Required]
        public string Exchange { get; set; }

        [Required]
        public string Symbol { get; set; } // Fixed to BTC/USDT:USDT by the view/controller

        // buy/sell
        [Required]
        [RegularExpression("^(buy|sell)$", ErrorMessage = "Direction must be 'buy' or 'sell'.")]
        public string Direction { get; set; }

        // Optional limit price; if null => market order
        [Range(0.0, double.MaxValue, ErrorMessage = "Price must be positive.")]
        public double? Price { get; set; }

        // Optional Stoploss price; service treats null/<=0 as no SL
        [Range(0.0, double.MaxValue, ErrorMessage = "Stoploss must be >= 0.")]
        public double? Stoploss { get; set; }

        // For entries: absolute size; For TP/Moonbag: percentage (0-100)
        [Range(0.00000001, double.MaxValue, ErrorMessage = "Size must be greater than zero.")]
        public double Size { get; set; }

        [Range(1, 20, ErrorMessage = "Leverage must be between 1 and 20.")]
        public int Leverage { get; set; }

        public bool IsIsolated { get; set; } = true;

        // Enforce allowed descriptions
        [Required]
        [RegularExpression("^(Initial Entry Order|DCA1 Entry Order|DCA2 Entry Order|Take Profit Order 1|Take Profit Order 1 \\+ MSL|Moonbag Order)$",
            ErrorMessage = "Invalid description.")]
        public string Description { get; set; }

        // Always OPEN for this page
        [Required]
        [RegularExpression("^OPEN$", ErrorMessage = "Status must be OPEN.")]
        public string Status { get; set; } = "OPEN";

        // Needed by SendTakeProfitOrderAsync to locate the existing position
        // Optional unless placing a TP/Moonbag order
        public string? PositionId { get; set; }

        // API credentials (not persisted)
        [Required]
        public string ApiKey { get; set; }

        [Required]
        public string ApiSecret { get; set; }

        [Required]
        public string Password { get; set; }

        // Exchange response payload (shown after POST)
        public string? ResponseJson { get; set; }

        // Conditional validation for TP/Moonbag
        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            var isTpLike = Description?.StartsWith("Take Profit", StringComparison.OrdinalIgnoreCase) == true
                           || string.Equals(Description, "Moonbag Order", StringComparison.OrdinalIgnoreCase);

            if (isTpLike)
            {
                if (Size <= 0 || Size > 100)
                    yield return new ValidationResult("For TP/Moonbag orders Size must be a percentage between 0 and 100.", new[] { nameof(Size) });

                if (string.IsNullOrWhiteSpace(PositionId))
                    yield return new ValidationResult("PositionId is required for Take Profit / Moonbag orders.", new[] { nameof(PositionId) });
            }
        }
    }
}