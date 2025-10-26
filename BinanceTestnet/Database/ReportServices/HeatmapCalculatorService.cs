using BinanceTestnet.MarketAnalysis;

namespace BinanceTestnet.Services.Reporting
{
    public class HeatmapCalculatorService
    {
        public List<StrategyRegimePerformance> CalculateStrategyRegimePerformance(
            List<Trade> trades, List<MarketRegimeSegment> regimeSegments)
        {
            var results = new List<StrategyRegimePerformance>();
            var strategies = trades.Select(t => t.Signal).Distinct();

            foreach (var strategy in strategies)
            {
                var strategyTrades = trades.Where(t => t.Signal == strategy).ToList();
                var performance = new StrategyRegimePerformance
                {
                    Strategy = strategy,
                    TotalPnL = strategyTrades.Sum(t => t.Profit ?? 0)
                };

                // Calculate win rates for each regime type
                performance.BullishWinRate = CalculateWinRateForRegime(strategyTrades, regimeSegments, MarketRegimeType.BullishTrend);
                performance.BearishWinRate = CalculateWinRateForRegime(strategyTrades, regimeSegments, MarketRegimeType.BearishTrend);
                performance.RangingWinRate = CalculateWinRateForRegime(strategyTrades, regimeSegments, MarketRegimeType.RangingMarket);
                performance.HighVolWinRate = CalculateWinRateForRegime(strategyTrades, regimeSegments, MarketRegimeType.HighVolatility);

                results.Add(performance);
            }

            return results;
        }
        
        private decimal CalculateWinRateForRegime(List<Trade> trades, List<MarketRegimeSegment> segments, MarketRegimeType regimeType)
        {
            var regimeTrades = trades.Where(t =>
                segments.Any(s =>
                    s.Regime.Type == regimeType &&
                    t.EntryTime >= s.StartTime &&
                    t.EntryTime <= s.EndTime))
                .ToList();

            if (regimeTrades.Count == 0) return 0;

            return (decimal)regimeTrades.Count(t => t.Profit > 0) / regimeTrades.Count * 100;
        }
    }
}