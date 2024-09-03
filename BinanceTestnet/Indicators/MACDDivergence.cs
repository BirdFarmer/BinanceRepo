using Skender.Stock.Indicators;

namespace BinanceLive.Indicators
{
    public static class MACDDivergence
    {
        public static List<(DateTime Date, string Signal)> CalculateMACDDivergence(List<Quote> history, int fastPeriod = 12, int slowPeriod = 26, int signalPeriod = 9)
        {
            var macdResults = Indicator.GetMacd(history, fastPeriod, slowPeriod, signalPeriod).ToList();
            var signals = new List<(DateTime Date, string Signal)>();

            for (int i = 1; i < macdResults.Count; i++)
            {
                if (macdResults[i].Macd == null || macdResults[i].Signal == null || macdResults[i].Histogram == null)
                    continue;
                var prevMacd = macdResults[i - 1].Macd.GetValueOrDefault(double.MinValue);
                var prevSignal = macdResults[i - 1].Signal.GetValueOrDefault(double.MinValue);
                var macd = macdResults[i].Macd.GetValueOrDefault(double.MinValue);
                var signal = macdResults[i].Signal.GetValueOrDefault(double.MinValue);
                var close = history[i].Close;

                if (macd > signal && prevMacd < prevSignal && close > history[i - 1].Close)
                {
                    signals.Add((macdResults[i].Date, "Bullish Divergence"));
                }
                // Bearish Divergence
                else if (macd < signal && prevMacd > prevSignal && close < history[i - 1].Close)
                {
                    signals.Add((macdResults[i].Date, "Bearish Divergence"));
                }
            }

            return signals;
        }
    }
}
