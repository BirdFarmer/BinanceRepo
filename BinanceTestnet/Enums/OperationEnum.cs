using System.ComponentModel;

namespace BinanceTestnet.Enums
{
    public enum OperationMode
    {
        [Description("Paper Trading")]
        LivePaperTrading = 1,

        [Description("Backtesting")]
        Backtest = 2,

        [Description("Live Real Trading")]
        LiveRealTrading = 3 // Make sure to add the description if you want it to be displayed
    }
}
