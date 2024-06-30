public class ActiveTrade
{
    public string Symbol { get; set; }
    public decimal EntryPrice { get; set; }
    public decimal CurrentPrice { get; set; }
    public decimal Quantity { get; set; }
    public decimal TakeProfit { get; set; }
    public decimal StopLoss { get; set; }
    public bool IsLong { get; set; }

    public decimal CurrentValue
    {
        get
        {
            decimal priceDifference = IsLong ? (CurrentPrice - EntryPrice) : (EntryPrice - CurrentPrice);
            return priceDifference * Quantity;
        }
    }
}
