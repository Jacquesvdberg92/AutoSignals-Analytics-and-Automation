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
    }
}
