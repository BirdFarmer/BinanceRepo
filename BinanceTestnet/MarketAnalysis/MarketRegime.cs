using System;

namespace BinanceTestnet.MarketAnalysis
{
    public enum MarketRegimeType
    {
        BullishTrend,     // 🟢 Risk-On Environment
        BearishTrend,     // 🔴 Risk-Off Environment  
        RangingMarket,    // 🟡 Neutral/Consolidation
        HighVolatility,   // 🟠 Breakout/Breakdown Pending
        Unknown          // Data insufficient or error
    }

    public enum TrendStrength
    {
        VeryStrong,    // All indicators aligned + strong momentum
        Strong,        // Primary trend confirmed
        Moderate,      // Mixed signals but bias clear
        Weak,          // Contradictory indicators
        Neutral        // No clear trend
    }

    public enum VolatilityLevel
    {
        VeryHigh,   // ATR Ratio > 2.0
        High,       // ATR Ratio > 1.5  
        Elevated,   // ATR Ratio > 1.2  ← ADD THIS
        Normal,     // ATR Ratio 0.8-1.2
        Low         // ATR Ratio < 0.8
    }

    public class MarketRegime
    {
        public MarketRegimeType Type { get; set; }
        public TrendStrength TrendStrength { get; set; }
        public VolatilityLevel Volatility { get; set; }
        public string DominantTimeframe { get; set; } = "1H"; // 1H, 4H, Daily

        // Confidence scores (0-100)
        public int TrendConfidence { get; set; }
        public int VolatilityConfidence { get; set; }
        public int OverallConfidence { get; set; }

        // Timestamps
        public DateTime AnalysisTime { get; set; }
        public DateTime PeriodStart { get; set; }
        public DateTime PeriodEnd { get; set; }

        // Key metrics for display
        public decimal PriceVs200EMA { get; set; } // Percentage above/below
        public decimal RSI { get; set; }
        public decimal ATRRatio { get; set; } // Current vs average ATR
        public decimal VolumeRatio { get; set; } // Current vs average volume

        public string Description => GetDescription();

        private string GetDescription()
        {
            return Type switch
            {
                MarketRegimeType.BullishTrend => $"Bullish Trend ({TrendStrength}) - {Volatility} Volatility",
                MarketRegimeType.BearishTrend => $"Bearish Trend ({TrendStrength}) - {Volatility} Volatility",
                MarketRegimeType.RangingMarket => $"Ranging Market - {Volatility} Volatility",
                MarketRegimeType.HighVolatility => $"High Volatility - Potential Breakout",
                _ => "Market Regime Unknown"
            };
        }
    }

    public class MarketRegimeSegment
    {
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public MarketRegime Regime { get; set; }
        public int TradeCount { get; set; }
        public decimal TotalPnL { get; set; }
        public decimal WinRate { get; set; }

        public TimeSpan Duration => EndTime - StartTime;
    }
    
    public class StrategyRegimePerformance
    {
        public int TotalTrades { get; set; }
        public decimal NetProfit { get; set; }
        public decimal WinRate { get; set; }
        public decimal LongWinRate { get; set; }
        public decimal ShortWinRate { get; set; }
        public decimal LongProfit { get; set; }
        public decimal ShortProfit { get; set; }
        public double AvgTradeDuration { get; set; }
        
        public decimal LongShortWinRateDiff => LongWinRate - ShortWinRate;
        public decimal LongShortProfitDiff => LongProfit - ShortProfit;
    }    
}