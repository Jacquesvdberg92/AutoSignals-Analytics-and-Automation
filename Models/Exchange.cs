namespace AutoSignals.Models
{
    public class Exchange
    {
        public int Id { get; set; }
        public string Name { get; set; }

        public string Description { get; set; } // Detailed description of the exchange

        public string Referal { get; set; }
        public string Url { get; set; }
        public int ReferalClicked { get; set; }

        public string ReferralBonus { get; set; } // Info about the referral bonus

        public string Type { get; set; } // "CEX" or "DEX"

        public string LogoUrl { get; set; } // Path or URL to the logo image

        public bool IsEnabled { get; set; }
    }
}