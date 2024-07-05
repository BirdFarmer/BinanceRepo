public class TradeRecord
{
    public string Symbol { get; set; }
    public decimal EntryPrice { get; set; }
    public decimal ExitPrice { get; set; }
    public bool IsLong { get; set; }
    public decimal Quantity { get; set; }
    public string Signal { get; set; }
    public decimal Profit { get; set; }
    public DateTime Timestamp { get; set; } // Ensure this is DateTime
}
