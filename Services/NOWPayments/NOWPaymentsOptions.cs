using System.ComponentModel.DataAnnotations;

namespace AutoSignals.Services.NOWPayments
{
    public class NOWPaymentsOptions
    {
        public const string SectionName = "NOWPayments";

        /// <summary>NOWPayments API key — keep in User Secrets / env vars.</summary>
        [Required(AllowEmptyStrings = false)]
        public string ApiKey { get; set; } = string.Empty;

        /// <summary>IPN secret key — used to verify incoming webhook signatures.</summary>
        [Required(AllowEmptyStrings = false)]
        public string IpnSecret { get; set; } = string.Empty;

        /// <summary>Absolute URL NOWPayments will POST payment notifications to.</summary>
        public string IpnCallbackUrl { get; set; } = string.Empty;
    }
}
