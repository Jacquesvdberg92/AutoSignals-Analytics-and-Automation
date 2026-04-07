using System.ComponentModel.DataAnnotations;

namespace AutoSignals.ViewModels.ProviderRegex
{
    public class GenerateRulesViewModel
    {
        public int ProviderId { get; set; }
        public string ProviderName { get; set; } = "";

        [Display(Name = "Example Signal 1")]
        public string? Example1 { get; set; }

        [Display(Name = "Example Signal 2")]
        public string? Example2 { get; set; }

        [Display(Name = "Example Signal 3")]
        public string? Example3 { get; set; }

        [Display(Name = "Example Signal 4")]
        public string? Example4 { get; set; }

        [Display(Name = "Example Signal 5")]
        public string? Example5 { get; set; }

        public List<SuggestedParsingRule>? SuggestedRules { get; set; }
        public string? ErrorMessage { get; set; }
        public bool RulesGenerated { get; set; }

        public IEnumerable<string> GetExamples() =>
            new[] { Example1, Example2, Example3, Example4, Example5 }
                .Where(e => !string.IsNullOrWhiteSpace(e))
                .Select(e => e!);
    }

    public class SuggestedParsingRule
    {
        public string RuleType { get; set; } = "";
        public string RegexPattern { get; set; } = "";
        public string RegexGroupName { get; set; } = "";
        public string? FallbackValue { get; set; }
        public bool IsRequired { get; set; }
        public int Order { get; set; }
        public string? ValidationLogic { get; set; }
        public string? Notes { get; set; }
        public bool Selected { get; set; } = true;
    }
}
