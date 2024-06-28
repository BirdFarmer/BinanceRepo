// Models/Kline.cs
namespace BinanceLive.Models
{
    public class Kline
    {
        public long OpenTime { get; set; }
        public decimal Open { get; set; }
        public decimal High { get; set; }
        public decimal Low { get; set; }
        public decimal Close { get; set; }
        public long CloseTime { get; set; }
        public int NumberOfTrades { get; set; }
    }
}
