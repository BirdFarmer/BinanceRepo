namespace BinanceTestnet.Models
{
    public class Quote : Skender.Stock.Indicators.IQuote
    {
        public string Symbol { get; set; }  // Add this property
        public decimal Open { get; set; }
        public decimal High { get; set; }
        public decimal Low { get; set; }
        public decimal Close { get; set; }
        public decimal Volume { get; set; }
        public DateTime Date { get; set; }
    }
}
