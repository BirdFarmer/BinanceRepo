namespace TradingAPI.Models
{
    public class TradingSettings
    {
        public string Interval { get; set; }
        public decimal Leverage { get; set; }
        public string[] Strategies { get; set; }
        public decimal TakeProfit { get; set; }
        public string[] CoinPairs { get; set; }
        public string OperationMode { get; set; }
        public string TradeDirection { get; set; }
        public string Strategy { get; set; }
    }
}
