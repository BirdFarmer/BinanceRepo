using System;

namespace BinanceTestnet.Database;

public class PerformanceMetrics
{
        public int TotalTrades { get; set; }
        public int WinningTrades { get; set; }
        public int LosingTrades { get; set; }
        public decimal NetProfit { get; set; }
        public decimal WinRate { get; set; }
        public decimal MaximumDrawdown { get; set; }
        public decimal ProfitFactor { get; set; }
        public decimal SharpeRatio { get; set; }

}
