namespace BinanceTestnet.Trading
{
    public class TradingSettings
    {
        public string Interval { get; set; } = "1m"; // Default value
        public decimal Leverage { get; set; }
        public List<string>? Strategies { get; set; }
        public decimal TakeProfit { get; set; }
        public List<string>? CoinPairs { get; set; }
    }
}
