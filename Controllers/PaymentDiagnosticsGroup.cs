using AutoSignals.Models;

namespace AutoSignals.Controllers
{
    public class PaymentDiagnosticsGroup
    {
        public string?  PaymentId   { get; init; }
        public string?  OrderId     { get; init; }
        public string   UserId      { get; init; } = string.Empty;
        public string   UserEmail   { get; init; } = string.Empty;
        public List<SubscriptionEvent> Events { get; init; } = [];
        public bool     IsActivated { get; init; }
        public bool     HasFailure  { get; init; }
    }
}
