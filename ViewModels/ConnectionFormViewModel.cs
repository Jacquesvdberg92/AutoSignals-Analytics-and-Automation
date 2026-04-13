using Microsoft.AspNetCore.Mvc.Rendering;

namespace AutoSignals.ViewModels
{
    public class ConnectionFormViewModel
    {
        public int Id { get; set; }
        public int ExchangeId { get; set; }
        public string? Label { get; set; }
        public string? ApiKeyInput { get; set; }
        public string? ApiSecretInput { get; set; }
        public string? ApiPasswordInput { get; set; }
        public bool IsDefault { get; set; }
        public bool IsActive { get; set; } = true;
        public bool HasExistingCredentials { get; set; }
        public List<SelectListItem> AvailableExchanges { get; set; } = new();
    }
}
