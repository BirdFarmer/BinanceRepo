public class Trade
{
    // Trade properties
    public int TradeId { get; } // Matches the database column name
    public string SessionId { get; set; } // Identifier for the backtest/live session
    public string Symbol { get; }
    public string TradeType => IsLong ? "Long" : "Short"; // Derived property for database
    public string Signal { get; }
    public DateTime EntryTime { get; } // Matches the database column name (UTC)
    public DateTime? ExitTime { get; set; } // Matches the database column name (UTC)
    public decimal EntryPrice { get; }
    public decimal? ExitPrice { get; set; } // Matches the database column name
    public decimal? Profit { get; set; }
    public decimal Leverage { get; }
    public decimal TakeProfit { get; } // Matches the database column name
    public decimal StopLoss { get; } // Matches the database column name
    public int Duration { get; set; } // Duration in minutes (matches the database column name)
    public decimal FundsAdded => Profit.HasValue ? Profit.Value * InitialMargin : 0; // Derived property for database
    public bool IsLong { get; }
    public bool IsInTrade { get; set; }
    public bool IsClosed { get; private set; }
    public string Interval { get; } // Matches the database column name
    public DateTime KlineTimestamp { get; set; } // Matches the database column name (UTC)

    // Derived properties
    public decimal InitialMargin => Quantity * EntryPrice / Leverage;
    public decimal Quantity { get; }

    public Trade(int tradeId, string sessionId, string symbol, decimal entryPrice, decimal takeProfitPrice, decimal stopLossPrice, 
                    decimal quantity, bool isLong, decimal leverage, string signal, string interval, long timestamp)
    {
        TradeId = tradeId;
        SessionId = sessionId;
        Symbol = symbol;
        EntryPrice = entryPrice;
        TakeProfit = takeProfitPrice;
        StopLoss = stopLossPrice;
        Quantity = quantity;
        IsLong = isLong;
        Leverage = leverage;
        Signal = signal;
        Interval = interval;
        KlineTimestamp = DateTimeOffset.FromUnixTimeMilliseconds(timestamp).UtcDateTime; // Ensure KlineTimestamp is UTC
        EntryTime = KlineTimestamp; // Use UTC for EntryTime
        IsInTrade = false;
        IsClosed = false;
    }

    /// <summary>
    /// Closes the trade with the specified exit price and exit time.
    /// </summary>
    /// <param name="exitPrice">The price at which the trade is closed.</param>
    /// <param name="exitTime">The timestamp of the exit candle (for backtesting).</param>
    public void CloseTrade(decimal exitPrice, DateTime? exitTime = null)
    {
        // Use the provided exitTime for backtesting (ensure it's UTC), or DateTime.UtcNow for live trading
        ExitTime = exitTime?.ToUniversalTime() ?? DateTime.UtcNow;
        ExitPrice = exitPrice;

        // Calculate duration as TimeSpan first
        TimeSpan duration = ExitTime.Value - EntryTime;

        // Convert duration to minutes (as int)
        Duration = (int)duration.TotalMinutes;

        // Calculate profit
        Profit = CalculateRealizedReturn(exitPrice);

        // Update trade status
        IsInTrade = false;
        IsClosed = true;
    }

    public decimal CalculateRealizedReturn(decimal closingPrice)
    {
        return ((closingPrice - EntryPrice) / EntryPrice) * (IsLong ? 1 : -1) * Leverage;
    }
}