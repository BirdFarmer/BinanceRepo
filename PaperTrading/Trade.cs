using System;

public class Trade
{
    public int Id { get; }
    public string Symbol { get; }
    public decimal EntryPrice { get; }
    public decimal TakeProfitPrice { get; }
    public decimal StopLossPrice { get; }
    public decimal Quantity { get; }
    public bool IsLong { get; }
    public decimal Leverage { get; }
    public string Signal { get; }
    public DateTime EntryTimestamp { get; }
    public bool IsInTrade { get; private set; }
    public TimeSpan Duration { get; private set; }
    public decimal? Profit { get; private set; }

    // New property to track if the trade is closed
    public bool IsClosed { get; private set; }
    public string Interval { get; } // Add this property

    public Trade(int id, string symbol, decimal entryPrice, decimal takeProfitPrice, decimal stopLossPrice, decimal quantity, bool isLong, decimal leverage, string signal, string interval)
    {
        Id = id;
        Symbol = symbol;
        EntryPrice = entryPrice;
        TakeProfitPrice = takeProfitPrice;
        StopLossPrice = stopLossPrice;
        Quantity = quantity;
        IsLong = isLong;
        Leverage = leverage;
        Signal = signal;
        EntryTimestamp = DateTime.Now;
        IsInTrade = true;
        IsClosed = false; // Initialize as not closed
        Interval = interval; // Set the interval
    }

    public void CloseTrade(decimal exitPrice)
    {
        Duration = DateTime.Now - EntryTimestamp;
        IsInTrade = false;
        IsClosed = true; // Mark as closed
        Profit = CalculateRealizedReturn(exitPrice);
    }

    public decimal InitialMargin => Quantity * EntryPrice / Leverage;

    public decimal CalculateRealizedReturn(decimal closingPrice)
    {
        return ((closingPrice - EntryPrice) / EntryPrice) * (IsLong ? 1 : -1) * Leverage;
    }
}
