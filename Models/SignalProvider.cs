// Models/SignalProvider.cs
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace AutoSignals.Models
{
    public class SignalProvider
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(500)]
        public string Description { get; set; } = string.Empty;

        [Required]
        [MaxLength(50)]
        public string TelegramGroupId { get; set; } = string.Empty;

        [MaxLength(100)]
        public string TelegramGroupName { get; set; } = string.Empty;

        [Required]
        public bool IsActive { get; set; } = true;

        /// <summary>
        /// When true, if the regex rules cannot fully parse a message from this group,
        /// the AI signal parser will be used as a fallback.
        /// </summary>
        public bool UseAiFallback { get; set; } = false;

        /// <summary>
        /// When true, chart images sent to this group's Telegram chat will be analysed
        /// by the vision-AI image signal parser.
        /// </summary>
        public bool UseImageParsing { get; set; } = false;

        /// <summary>
        /// Optional custom system prompt sent to the GPT-4o vision model when parsing
        /// chart images for this provider. Leave null or empty to use the built-in default.
        /// </summary>
        public string? ImageParsingPrompt { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        // Navigation property
        public virtual ICollection<ProviderParsingRule> ParsingRules { get; set; } = new List<ProviderParsingRule>();
    }

    // Models/ProviderParsingRule.cs
    public class ProviderParsingRule
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [ForeignKey("SignalProvider")]
        public int ProviderId { get; set; }

        [Required]
        [MaxLength(50)]
        public string RuleType { get; set; } = string.Empty; // "Symbol", "Side", "Entry", "Stoploss", "TakeProfit", "Leverage"

        [Required]
        [MaxLength(500)]
        public string RegexPattern { get; set; } = string.Empty;

        [MaxLength(500)]
        public string RegexGroupName { get; set; } = string.Empty;

        [MaxLength(500)]
        public string? FallbackValue { get; set; } = null;

        public bool IsRequired { get; set; } = true;

        public int Order { get; set; } = 0;

        [MaxLength(1000)]
        public string? ValidationLogic { get; set; } = null; // JSON or custom logic

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        // Navigation properties
        [ValidateNever]
        public virtual SignalProvider Provider { get; set; } = null!;
    }
}