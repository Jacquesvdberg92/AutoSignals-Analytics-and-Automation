// Models/TestRegexViewModel.cs
namespace AutoSignals.ViewModels.ProviderRegex
{
    public class TestRegexViewModel
    {
        public int ProviderId { get; set; }
        public string ProviderName { get; set; }
        public string TelegramMessage { get; set; }
        public List<RuleTestResult> Results { get; set; } = new();
        public ParsedSignal? ParsedSignal { get; set; }
    }

    public class RuleTestResult
    {
        public int RuleId { get; set; }
        public string RuleType { get; set; }
        public string RegexPattern { get; set; }
        public string RegexGroupName { get; set; }
        public string FallbackValue { get; set; }
        public bool IsRequired { get; set; }
        public bool IsSuccess { get; set; }
        public string ExtractedValue { get; set; }
        public string ErrorMessage { get; set; }
        public string RawMatch { get; set; }
        public int Order { get; set; }
        public string Notes { get; set; } = "";
        public string MatchDetails { get; set; } = "";

        public string ValidationLogic { get; set; } = "";
        public List<string> ValidationErrors { get; set; } = new();
        public bool ValidationPassed { get; set; } = true;
    }

    public class ParsedSignal
    {
        public string Symbol { get; set; }
        public string OriginalSymbol { get; set; }
        public string Side { get; set; }
        public decimal Entry { get; set; }
        public decimal Stoploss { get; set; }
        public string TakeProfits { get; set; }
        public int Leverage { get; set; }
        public bool IsValid { get; set; }
        public List<string> Warnings { get; set; } = new();
        public List<string> ValidationErrors { get; set; } = new();
    }

    // ADD THESE NEW MODELS:

    public class TestSingleRuleRequest
    {
        public string RuleType { get; set; }
        public string RegexPattern { get; set; }
        public string RegexGroupName { get; set; }
        public string FallbackValue { get; set; }
        public string ValidationLogic { get; set; }
        public bool IsRequired { get; set; }
        public int? Order { get; set; }
        public string SampleText { get; set; }
    }

    public class TestSingleRuleResponse
    {
        public bool Success { get; set; }
        public List<RuleMatchInfo> Matches { get; set; } = new();
        public string ExtractedValue { get; set; }
        public List<ValidationTestResult> ValidationResults { get; set; } = new();
        public bool FallbackUsed { get; set; }
        public string Error { get; set; }
    }

    public class RuleMatchInfo
    {
        public string GroupName { get; set; }
        public string Value { get; set; }
        public int Index { get; set; }
        public int Length { get; set; }
    }

    public class ValidationTestResult
    {
        public string Operator { get; set; }
        public object Value { get; set; }
        public bool IsValid { get; set; }
        public string ErrorMessage { get; set; }
    }
}