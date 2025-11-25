using System;
using System.Collections.Generic;
using System.Linq;
using BinanceTestnet.Models;
using System.Globalization;
using Newtonsoft.Json;

namespace TradingAppDesktop.Views
{
    public partial class PreFlightMarketCheckWindow
    {
        // Compute timestamp-aligned Pearson correlation on log-returns with tolerant matching.
        // We match timestamps allowing nearest-key matches within one interval to tolerate small alignment gaps.
        private static (double? corr, int matched) ComputeTimestampAlignedCorrelation(List<Kline> a, List<Kline> b, int minMatches = 50)
        {
            if (a == null || b == null) return (null, 0);

            // Build returns keyed by CloseTime (log-returns)
            var returnsA = new Dictionary<long, double>();
            for (int i = 1; i < a.Count; i++)
            {
                var prev = a[i - 1];
                var cur = a[i];
                if (prev.Close <= 0 || cur.Close <= 0) continue;
                returnsA[cur.CloseTime] = Math.Log((double)cur.Close / (double)prev.Close);
            }

            var returnsB = new Dictionary<long, double>();
            for (int i = 1; i < b.Count; i++)
            {
                var prev = b[i - 1];
                var cur = b[i];
                if (prev.Close <= 0 || cur.Close <= 0) continue;
                returnsB[cur.CloseTime] = Math.Log((double)cur.Close / (double)prev.Close);
            }

            if (returnsA.Count == 0 || returnsB.Count == 0) return (null, 0);

            // Prepare sorted keys for B for nearest lookup
            var keysB = returnsB.Keys.OrderBy(k => k).ToArray();

            // Estimate typical interval (use median diff from B if available else A)
            long intervalMs = EstimateMedianInterval(keysB);
            if (intervalMs <= 0)
            {
                var keysA = returnsA.Keys.OrderBy(k => k).ToArray();
                intervalMs = EstimateMedianInterval(keysA);
            }
            if (intervalMs <= 0) intervalMs = 60 * 1000; // fallback 1m

            // Relax tolerance to two intervals to improve match rate when series are nearly aligned
            long tolerance = intervalMs * 2; // allow nearest within two candle intervals

            var matchedPairs = new List<(double a, double b)>();

            var sortedKeysA = returnsA.Keys.OrderBy(k => k).ToArray();
            foreach (var kA in sortedKeysA)
            {
                // try exact match first
                if (returnsB.TryGetValue(kA, out var rb))
                {
                    matchedPairs.Add((returnsA[kA], rb));
                    continue;
                }

                // binary search nearest in keysB
                int idx = Array.BinarySearch(keysB, kA);
                if (idx < 0) idx = ~idx;
                long nearestKey = -1;
                long bestDiff = long.MaxValue;
                if (idx < keysB.Length)
                {
                    var diff = Math.Abs(keysB[idx] - kA);
                    if (diff < bestDiff) { bestDiff = diff; nearestKey = keysB[idx]; }
                }
                if (idx - 1 >= 0)
                {
                    var diff = Math.Abs(keysB[idx - 1] - kA);
                    if (diff < bestDiff) { bestDiff = diff; nearestKey = keysB[idx - 1]; }
                }

                if (nearestKey >= 0 && bestDiff <= tolerance)
                {
                    matchedPairs.Add((returnsA[kA], returnsB[nearestKey]));
                }
            }

            int n = matchedPairs.Count;
            if (n < minMatches) return (null, n);

            // compute Pearson on matched pairs
            double sumA = matchedPairs.Sum(p => p.a);
            double sumB = matchedPairs.Sum(p => p.b);
            double meanA = sumA / n;
            double meanB = sumB / n;
            double cov = 0, varA = 0, varB = 0;
            foreach (var p in matchedPairs)
            {
                var da = p.a - meanA;
                var db = p.b - meanB;
                cov += da * db;
                varA += da * da;
                varB += db * db;
            }
            var denom = Math.Sqrt(varA * varB);
            if (denom <= double.Epsilon) return (null, n);
            var corr = cov / denom;
            return (corr, n);
        }

        private static long EstimateMedianInterval(long[] keys)
        {
            if (keys == null || keys.Length < 2) return 0;
            var diffs = new List<long>();
            for (int i = 1; i < keys.Length; i++) diffs.Add(keys[i] - keys[i - 1]);
            diffs.Sort();
            return diffs[diffs.Count / 2];
        }

        private static string UnixMsToUtc(long ms)
        {
            try
            {
                return DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime.ToString("yyyy-MM-dd HH:mm");
            }
            catch
            {
                return ms.ToString();
            }
        }

        // Index-aligned correlation fallback: align last N bars by index and compute Pearson on log-returns.
        private static (double? corr, int matched) ComputeIndexAlignedCorrelation(List<Kline> a, List<Kline> b, int targetMatches = 950)
        {
            if (a == null || b == null) return (null, 0);

            // Determine how many bars to use so that returns length is targetMatches if possible
            int maxBars = Math.Min(a.Count, b.Count);
            int desiredBars = Math.Min(maxBars, targetMatches + 1); // need bars = matches+1
            if (desiredBars < 2) return (null, 0);

            // take last desiredBars from each
            var sliceA = a.Skip(a.Count - desiredBars).ToList();
            var sliceB = b.Skip(b.Count - desiredBars).ToList();

            var returnsA = new List<double>();
            for (int i = 1; i < sliceA.Count; i++)
            {
                var prev = sliceA[i - 1];
                var cur = sliceA[i];
                if (prev.Close <= 0 || cur.Close <= 0) continue;
                returnsA.Add(Math.Log((double)cur.Close / (double)prev.Close));
            }

            var returnsB = new List<double>();
            for (int i = 1; i < sliceB.Count; i++)
            {
                var prev = sliceB[i - 1];
                var cur = sliceB[i];
                if (prev.Close <= 0 || cur.Close <= 0) continue;
                returnsB.Add(Math.Log((double)cur.Close / (double)prev.Close));
            }

            int n = Math.Min(returnsA.Count, returnsB.Count);
            if (n < 2) return (null, n);

            // align by last n
            var ra = returnsA.Skip(returnsA.Count - n).ToArray();
            var rb = returnsB.Skip(returnsB.Count - n).ToArray();

            double meanA = ra.Average();
            double meanB = rb.Average();
            double cov = 0, varA = 0, varB = 0;
            for (int i = 0; i < n; i++)
            {
                var da = ra[i] - meanA;
                var db = rb[i] - meanB;
                cov += da * db;
                varA += da * da;
                varB += db * db;
            }
            var denom = Math.Sqrt(varA * varB);
            if (denom <= double.Epsilon) return (null, n);
            return (cov / denom, n);
        }

        private static decimal? ComputeSimpleBTCCorrelation(List<Kline> currentCoinKlines, List<Kline> btcKlines)
        {
            if (currentCoinKlines == null || btcKlines == null || currentCoinKlines.Count < 50 || btcKlines.Count < 50)
                return null;

            try
            {
                // Simple approach: Use the last 200 candles and align by close time
                var coinSlice = currentCoinKlines.TakeLast(200).ToList();
                var btcSlice = btcKlines.TakeLast(200).ToList();
                
                // Build dictionaries for quick lookup by close time
                var btcByTime = btcSlice.ToDictionary(k => k.CloseTime, k => k.Close);
                
                var coinReturns = new List<decimal>();
                var btcReturns = new List<decimal>();
                
                // Calculate returns for matching timestamps
                for (int i = 1; i < coinSlice.Count; i++)
                {
                    var currentCandle = coinSlice[i];
                    var prevCandle = coinSlice[i - 1];
                    
                    if (btcByTime.TryGetValue(currentCandle.CloseTime, out decimal btcClose) &&
                        btcByTime.TryGetValue(prevCandle.CloseTime, out decimal btcPrevClose))
                    {
                        // Calculate returns (percentage change)
                        decimal coinReturn = (currentCandle.Close - prevCandle.Close) / prevCandle.Close;
                        decimal btcReturn = (btcClose - btcPrevClose) / btcPrevClose;
                        
                        coinReturns.Add(coinReturn);
                        btcReturns.Add(btcReturn);
                    }
                }
                
                if (coinReturns.Count < 30) return null; // Need minimum data
                
                // Calculate correlation
                return CalculateCorrelation(coinReturns, btcReturns);
            }
            catch
            {
                return null;
            }
        }

        private static decimal CalculateCorrelation(List<decimal> x, List<decimal> y)
        {
            if (x.Count != y.Count) return 0;
            
            decimal meanX = x.Average();
            decimal meanY = y.Average();
            
            decimal numerator = 0;
            decimal denomX = 0;
            decimal denomY = 0;
            
            for (int i = 0; i < x.Count; i++)
            {
                decimal diffX = x[i] - meanX;
                decimal diffY = y[i] - meanY;
                
                numerator += diffX * diffY;
                denomX += diffX * diffX;
                denomY += diffY * diffY;
            }
            
            if (denomX == 0 || denomY == 0) return 0;
            
            return numerator / (decimal)(Math.Sqrt((double)(denomX * denomY)));
        }

        private static List<Kline> ParseKlinesFromContent(string content)
        {
            var result = new List<Kline>();
            try
            {
                var klineData = JsonConvert.DeserializeObject<List<List<object>>>(content);
                if (klineData == null) return result;

                foreach (var kline in klineData)
                {
                    long openTime = kline.Count > 0 && kline[0] != null ? Convert.ToInt64(kline[0], CultureInfo.InvariantCulture) : 0L;
                    string s1 = kline.Count > 1 ? kline[1]?.ToString() ?? "0" : "0";
                    string s2 = kline.Count > 2 ? kline[2]?.ToString() ?? "0" : "0";
                    string s3 = kline.Count > 3 ? kline[3]?.ToString() ?? "0" : "0";
                    string s4 = kline.Count > 4 ? kline[4]?.ToString() ?? "0" : "0";
                    string s5 = kline.Count > 5 ? kline[5]?.ToString() ?? "0" : "0";
                    long closeTime = kline.Count > 6 && kline[6] != null ? Convert.ToInt64(kline[6], CultureInfo.InvariantCulture) : 0L;
                    string tradesStr = kline.Count > 8 ? kline[8]?.ToString() ?? "0" : "0";

                    decimal open = decimal.TryParse(s1, NumberStyles.Any, CultureInfo.InvariantCulture, out var _open) ? _open : 0m;
                    decimal high = decimal.TryParse(s2, NumberStyles.Any, CultureInfo.InvariantCulture, out var _high) ? _high : 0m;
                    decimal low = decimal.TryParse(s3, NumberStyles.Any, CultureInfo.InvariantCulture, out var _low) ? _low : 0m;
                    decimal close = decimal.TryParse(s4, NumberStyles.Any, CultureInfo.InvariantCulture, out var _close) ? _close : 0m;
                    decimal vol = decimal.TryParse(s5, NumberStyles.Any, CultureInfo.InvariantCulture, out var _vol) ? _vol : 0m;
                    int trades = int.TryParse(tradesStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var _trades) ? _trades : 0;

                    result.Add(new Kline
                    {
                        OpenTime = openTime,
                        Open = open,
                        High = high,
                        Low = low,
                        Close = close,
                        Volume = vol,
                        CloseTime = closeTime,
                        NumberOfTrades = trades
                    });
                }
            }
            catch
            {
                // swallow parsing errors - caller will handle empty list
            }
            return result;
        }
    }
}
