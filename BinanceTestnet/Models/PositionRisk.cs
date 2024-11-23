using System;

namespace BinanceTestnet.Models;
public class PositionRisk
{
    public string Symbol { get; set; }
    public decimal PositionAmt { get; set; }      // Quantity of position
    public decimal EntryPrice { get; set; }
    public decimal UnrealizedProfit { get; set; }
    public string PositionSide { get; set; }      // LONG or SHORT
}
