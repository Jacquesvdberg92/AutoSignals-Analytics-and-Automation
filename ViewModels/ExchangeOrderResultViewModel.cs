namespace AutoSignals.ViewModels
{
    using AutoSignals.Models;
    using Microsoft.AspNetCore.Identity;
    using Microsoft.AspNetCore.Mvc.Rendering;
    using System.Collections.Generic;

    public class ExchangeOrderResult
    {
        public bool Success { get; set; }
        public string? ErrorCode { get; set; }
        public string? ErrorMessage { get; set; }
        public object? Response { get; set; }
        public string? ExternalOrderId { get; set; }
        public string? ClientOrderId { get; set; }
        public string? Status { get; set; }
        public decimal? AveragePrice { get; set; }
        public decimal? FilledQuantity { get; set; }
    }
}
