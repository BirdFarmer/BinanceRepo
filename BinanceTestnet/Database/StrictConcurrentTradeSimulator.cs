public class StrictConcurrentTradeSimulator
{
    public List<SimulatedTrade> Simulate(List<Trade> trades, int maxConcurrentTrades = 8)
    {
        var results = new List<SimulatedTrade>();
        var activeTrades = new List<Trade>();
        
        foreach (var trade in trades.OrderBy(t => t.EntryTime))
        {
            // Remove completed trades
            activeTrades.RemoveAll(t => t.ExitTime != null && t.ExitTime <= trade.EntryTime);

            if (activeTrades.Count < maxConcurrentTrades)
            {
                // Slot available - execute trade
                activeTrades.Add(trade);
                results.Add(new SimulatedTrade {
                    OriginalTrade = trade,
                    WasExecuted = true,
                    ExecutionTime = trade.EntryTime
                });
            }
            else
            {
                // No slots available - skip trade
                results.Add(new SimulatedTrade {
                    OriginalTrade = trade,
                    WasExecuted = false,
                    SkipReason = "All 8 trade slots were full"
                });
            }
        }
        
        return results;
    }

    public class SimulatedTrade
    {
        public Trade OriginalTrade { get; set; }
        public bool WasExecuted { get; set; }
        public DateTime? ExecutionTime { get; set; }
        public string SkipReason { get; set; }
    }

    public class SimulationResult
    {
        public List<SimulatedTrade> Trades { get; set; }
        public int MaxConcurrentTrades { get; set; }
        public Dictionary<DateTime, int> ConcurrentTradesOverTime { get; set; }
    }

    public SimulationResult SimulateWithDetails(List<Trade> trades, int maxConcurrentTrades = 8)
    {
        var result = new SimulationResult
        {
            Trades = new List<SimulatedTrade>(),
            ConcurrentTradesOverTime = new Dictionary<DateTime, int>(),
            MaxConcurrentTrades = 0
        };
        
        var activeTrades = new List<Trade>();
        
        foreach (var trade in trades.OrderBy(t => t.EntryTime))
        {
            // Remove completed trades
            activeTrades.RemoveAll(t => t.ExitTime != null && t.ExitTime <= trade.EntryTime);
            
            // Track concurrent trades
            result.ConcurrentTradesOverTime[trade.EntryTime] = activeTrades.Count;
            result.MaxConcurrentTrades = Math.Max(result.MaxConcurrentTrades, activeTrades.Count);
            
            if (activeTrades.Count < maxConcurrentTrades)
            {
                // Slot available
                activeTrades.Add(trade);
                result.Trades.Add(new SimulatedTrade {
                    OriginalTrade = trade,
                    WasExecuted = true,
                    ExecutionTime = trade.EntryTime
                });
            }
            else
            {
                // No slots available
                result.Trades.Add(new SimulatedTrade {
                    OriginalTrade = trade,
                    WasExecuted = false,
                    SkipReason = $"Max concurrent trades ({maxConcurrentTrades}) reached"
                });
            }
        }
        
        return result;
    }    
}