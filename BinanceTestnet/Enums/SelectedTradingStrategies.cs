using System.ComponentModel;

public enum SelectedTradingStrategy
{
    [Description("Loop through all strategies")]
    All = 1,

    [Description("3 SMAs expanding, trade reversal.")]
    SMAExpansion = 2,

    [Description("MACD Diversion")]
    MACD = 3,
    
    [Description("Hull with 200SMA")]
    Aroon = 4
}
