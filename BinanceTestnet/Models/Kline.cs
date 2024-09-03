namespace BinanceTestnet.Models
{
    public class Kline
    {
        public string? Symbol { get; set; }
        public long OpenTime { get; set; }
        public decimal Open { get; set; }
        public decimal High { get; set; }
        public decimal Low { get; set; }
        public decimal Close { get; set; }
        public long CloseTime { get; set; }
        public int NumberOfTrades { get; set; }
        public decimal Volume { get; set; } // Ensure this is included
    }
}
