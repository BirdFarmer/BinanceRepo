public class Trade
{
    public string Symbol { get; private set; }
    public decimal EntryPrice { get; private set; }
    public decimal Leverage { get; private set; }
    public decimal Quantity { get; private set; }
    public bool IsLong { get; private set; }
    public decimal InitialMargin => EntryPrice * Quantity / Leverage;
    public decimal TakeProfitPrice => IsLong ? EntryPrice * 1.01m : EntryPrice * 0.99m;
    public decimal StopLossPrice => IsLong ? EntryPrice * 0.995m : EntryPrice * 1.005m;

    public Trade(string symbol, decimal entryPrice, decimal leverage, decimal quantity, bool isLong)
    {
        Symbol = symbol;
        EntryPrice = entryPrice;
        Leverage = leverage;
        Quantity = quantity;
        IsLong = isLong;
    }

    public decimal CalculateProfit(decimal closingPrice)
    {
        var priceDifference = IsLong ? closingPrice - EntryPrice : EntryPrice - closingPrice;
        return priceDifference * Quantity * Leverage;
    }

    public bool IsStoppedOut(decimal currentPrice)
    {
        return IsLong ? currentPrice <= StopLossPrice : currentPrice >= StopLossPrice;
    }

    public bool IsTakeProfitHit(decimal currentPrice)
    {
        return IsLong ? currentPrice >= TakeProfitPrice : currentPrice <= TakeProfitPrice;
    }

    public decimal CalculateRealizedReturn(decimal closingPrice)
    {
        var profit = CalculateProfit(closingPrice);
        return profit / InitialMargin;
    }

    public decimal CurrentValue(decimal currentPrice)
    {
        decimal value = Quantity * currentPrice;

        if (IsLong)
        {
            return value;
        }
        else // For short positions
        {
            return -value; // Make it negative for short positions
        }
    }
}
