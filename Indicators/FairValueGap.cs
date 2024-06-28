using BinanceLive.Models;

namespace BinanceLive.Indicators
{
    public static class FairValueGap
    {
        public static List<(DateTime Date, double Gap)> CalculateFVG(List<Kline> klines)
        {
            var gaps = new List<(DateTime Date, double Gap)>();

            for (int i = 1; i < klines.Count; i++)
            {
                var prevClose = (double)klines[i - 1].Close;
                var open = (double)klines[i].Open;

                // Calculate the gap
                var gap = open - prevClose;
                if (Math.Abs(gap) > 0) // Ignore if there's no gap
                {
                    gaps.Add((DateTimeOffset.FromUnixTimeMilliseconds(klines[i].OpenTime).UtcDateTime, gap));
                }
            }

            return gaps;
        }
    }
}
