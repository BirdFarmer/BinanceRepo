public class Trade
{
    public string Symbol { get; }
    public decimal EntryPrice { get; }
    public decimal TakeProfitPrice { get; }
    public decimal StopLossPrice { get; }
    public decimal Quantity { get; }
    public bool IsLong { get; }
    public decimal Leverage { get; }

    public Trade(string symbol, decimal entryPrice, decimal takeProfitPrice, decimal stopLossPrice, decimal quantity, bool isLong, decimal leverage)
    {
        Symbol = symbol;
        EntryPrice = entryPrice;
        TakeProfitPrice = takeProfitPrice;
        StopLossPrice = stopLossPrice;
        Quantity = quantity;
        IsLong = isLong;
        Leverage = leverage;
    }

    public decimal CurrentValue(decimal currentPrice)
    {
        return Quantity * currentPrice;
    }

    public decimal InitialMargin => Quantity * EntryPrice / Leverage;

    public bool IsTakeProfitHit(decimal currentPrice)
    {
        
        //Console.WriteLine($"{Symbol} LONG:{IsLong}, price: {currentPrice:F5} compared to TP: {TakeProfitPrice:F5} ");
        return IsLong ? currentPrice >= TakeProfitPrice : currentPrice <= TakeProfitPrice;
    }

    public bool IsStoppedOut(decimal currentPrice)
    {
        //Console.WriteLine($"{Symbol} LONG:{IsLong}, price: {currentPrice:F5} compared to SL: {StopLossPrice:F5} ");
        return IsLong ? currentPrice <= StopLossPrice : currentPrice >= StopLossPrice;
    }

    public decimal CalculateRealizedReturn(decimal closingPrice)
    {
        return ((closingPrice - EntryPrice) / EntryPrice) * (IsLong ? 1 : -1) * Leverage;
    }
}
