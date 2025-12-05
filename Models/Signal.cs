namespace AutoSignals.Models
{
    public class Signal
    { 
        public int Id { get; set; }
        public string Symbol { get; set; }
        public string Side { get; set; }
        public int Leverage { get; set; }
        public float Entry { get; set; }
        public float Stoploss { get; set; }
        public string TakeProfits { get; set; }
        public string Provider { get; set; }
        public DateTime Time { get; set; }
    }
}
