namespace BinanceTestnet.Database
{
    public class ReportSettings
    {
        // Existing properties for basic report configuration
        public string OutputPath { get; set; } = "Reports";
        public bool AutoOpen { get; set; } = true;
    
        // public bool AutoOpenTextReport { get; set; } = false;
        // public bool AutoOpenHtmlReport { get; set; } = true;

        
        // New properties for enhanced reporting
        public string StrategyName { get; set; } = "Unknown Strategy";
        public int Leverage { get; set; } = 10;
        public decimal TakeProfitMultiplier { get; set; } = 2.5m;
        public decimal MarginPerTrade { get; set; } = 20m;
        public string Interval { get; set; } = "5m";
        
        // Risk analysis settings
        public decimal LiquidationWarningThreshold { get; set; } = 0.9m; // 90% of liquidation
        
        // Display settings
        public bool ShowTradeDetails { get; set; } = true;
        public int MaxCriticalTradesToShow { get; set; } = 5;
        public int TopPerformersCount { get; set; } = 3;
        
        // Formatting settings
        public string DateFormat { get; set; } = "yyyy-MM-dd HH:mm:ss";
        public string NumberFormat { get; set; } = "0.#####"; //"F2";
        
        // Add this new property
        public decimal StopLossRatio { get; set; } = 3m; // Default 1:3 ratio
        
        // Change this existing property to be just the percentage display
        public string StopLossThresholdDisplay => $"{(-100.0 / Leverage):N2}%";
        
        // Method to calculate liquidation threshold
        public decimal GetLiquidationThreshold()
        {
            return -100m / Leverage;
        }
        
        // Method to check if a trade is near liquidation
        public bool IsNearLiquidation(decimal priceChangePercentage)
        {
            decimal threshold = GetLiquidationThreshold() * LiquidationWarningThreshold;
            return priceChangePercentage <= threshold;
        }

        public int GetIntervalMinutes()  
        {  
            if (string.IsNullOrEmpty(Interval)) return 15; // Default  
            if (Interval.EndsWith("m") && int.TryParse(Interval[..^1], out int mins)) return mins;  
            if (Interval.EndsWith("h") && int.TryParse(Interval[..^1], out int hrs)) return hrs * 60;  
            return 15; // Fallback  
        }  
    }
}