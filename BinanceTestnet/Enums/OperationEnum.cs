using System.ComponentModel;

namespace BinanceTestnet.Enums
{
    public enum OperationMode
    {
        [Description("Paper Trading")]
        LivePaperTrading = 0, 
        
        [Description("Backtesting")]
        Backtest = 1,
        
        [Description("Live Real Trading")]
        LiveRealTrading = 2
    }
}