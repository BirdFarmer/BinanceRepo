public class TradeManager
{
    private List<ActiveTrade> activeTrades = new List<ActiveTrade>();
    private List<PendingTrade> pendingTrades = new List<PendingTrade>();
    private List<HistoricalTrade> historicalTrades = new List<HistoricalTrade>();

    public decimal GetTotalActiveTradesValue()
    {
        return activeTrades.Sum(trade => trade.CurrentValue);
    }

    // Methods to add, remove, and update trades
    public void AddActiveTrade(ActiveTrade trade)
    {
        activeTrades.Add(trade);
    }

    public void UpdateActiveTrade(ActiveTrade trade)
    {
        // Find and update the trade
    }

    public void CloseActiveTrade(ActiveTrade trade)
    {
        activeTrades.Remove(trade);
        historicalTrades.Add(new HistoricalTrade
        {
            Symbol = trade.Symbol,
            EntryPrice = trade.EntryPrice,
            CurrentPrice = trade.CurrentPrice,
            Quantity = trade.Quantity,
            TakeProfit = trade.TakeProfit,
            StopLoss = trade.StopLoss,
            IsLong = trade.IsLong,
            CloseTime = DateTime.Now,
            ProfitLoss = trade.CurrentValue
        });
    }
}
