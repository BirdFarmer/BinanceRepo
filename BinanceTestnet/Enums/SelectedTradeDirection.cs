using System.ComponentModel;
public enum SelectedTradeDirection
{    
    [Description("Both Longs and Shorts")]
    Both = 1,
    
    [Description("Only Long trades")]
    OnlyLongs = 2,

    [Description("Only Short trades")]
    OnlyShorts = 3
}
