// Models/ValidationModels.cs (keep this as shared model)
using System.Text.Json.Serialization;

namespace AutoSignals.Models
{
    public class ValidationRule
    {
        [JsonPropertyName("operator")]
        public string Operator { get; set; }

        [JsonPropertyName("value")]
        public object Value { get; set; }

        [JsonPropertyName("errorMessage")]
        public string ErrorMessage { get; set; }
    }

    public class ValidationResult
    {
        public bool IsValid { get; set; } = true;
        public List<string> Errors { get; set; } = new();
        public string ErrorMessage => string.Join("; ", Errors);
    }
}