using BinanceTestnet.Enums;

public class TradingConfig
{
    public OperationMode Mode { get; set; }
    public string HistoricalDataPath { get; set; } // Path to historical data for backtesting
}
