using BinanceTestnet.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BinanceTestnet.Strategies.VolumeProfile
{
    public class VolumeProfileResult
    {
        public decimal POC { get; set; }
        public decimal VAH { get; set; }
        public decimal VAL { get; set; }
        public Dictionary<decimal, decimal> PriceBuckets { get; set; } = new Dictionary<decimal, decimal>();
    }

    public static class VolumeProfileCalculator
    {
        // Build a volume profile approximation from OHLCV klines
        public static VolumeProfileResult BuildFromKlines(List<Kline> klines, int buckets = 100, decimal valueAreaPct = 0.70m)
        {
            var result = new VolumeProfileResult();
            if (klines == null || klines.Count == 0) return result;

            decimal globalLow = klines.Min(k => k.Low);
            decimal globalHigh = klines.Max(k => k.High);
            if (globalHigh <= globalLow)
            {
                // Degenerate case: all prices equal
                decimal center = globalLow;
                result.PriceBuckets[center] = klines.Sum(k => k.Volume);
                result.POC = center;
                result.VAH = center;
                result.VAL = center;
                return result;
            }

            // Prepare buckets as price -> volume mapping
            var bucketsDict = new SortedDictionary<decimal, decimal>();
            decimal span = globalHigh - globalLow;
            decimal bucketWidth = span / buckets;

            Console.WriteLine($"[VolumeProfile] Building profile: symbol sample candles={klines.Count}, buckets={buckets}, bucketWidth={bucketWidth:F6}, valueAreaPct={valueAreaPct:P0}");

            // Initialize buckets
            for (int i = 0; i <= buckets; i++)
            {
                decimal price = globalLow + bucketWidth * i;
                bucketsDict[price] = 0m;
            }

            // Distribute each candle's volume across buckets that fall within its high-low range
            foreach (var k in klines)
            {
                decimal low = k.Low;
                decimal high = k.High;
                if (high <= low)
                {
                    // allocate to nearest bucket
                    decimal key = NearestBucketKey(bucketsDict.Keys, low);
                    bucketsDict[key] += k.Volume;
                    continue;
                }

                // Find bucket indices overlapping candle
                int startIdx = (int)Math.Floor((double)((low - globalLow) / bucketWidth));
                int endIdx = (int)Math.Ceiling((double)((high - globalLow) / bucketWidth));
                startIdx = Math.Max(0, startIdx);
                endIdx = Math.Min(buckets, endIdx);
                int count = Math.Max(1, endIdx - startIdx + 1);

                // Even distribution across overlapping buckets
                decimal perBucket = k.Volume / count;
                for (int bi = startIdx; bi <= endIdx; bi++)
                {
                    decimal price = globalLow + bucketWidth * bi;
                    bucketsDict[price] += perBucket;
                }
            }

            // Convert to list for POC calculation
            var bucketList = bucketsDict.Select(kv => new { Price = kv.Key, Volume = kv.Value }).ToList();
            decimal totalVol = bucketList.Sum(b => b.Volume);
            if (totalVol <= 0)
            {
                // fallback: use close-weighted POC
                decimal weightedPrice = klines.Sum(k => k.Close * k.Volume) / Math.Max(1, klines.Sum(k => k.Volume));
                result.POC = weightedPrice;
                result.VAH = weightedPrice;
                result.VAL = weightedPrice;
                return result;
            }

            // POC: bucket with max volume
            var pocBucket = bucketList.OrderByDescending(b => b.Volume).First();
            result.POC = pocBucket.Price;

            // Compute Value Area around POC: expand up/down from POC until cumulative >= valueAreaPct*totalVol
            int pocIndex = bucketList.FindIndex(b => b.Price == pocBucket.Price);
            int lowIndex = pocIndex;
            int highIndex = pocIndex;
            decimal cum = bucketList[pocIndex].Volume;
            decimal target = totalVol * valueAreaPct;

            while (cum < target)
            {
                // choose side with larger adjacent bucket volume to expand first
                decimal nextHighVol = highIndex + 1 < bucketList.Count ? bucketList[highIndex + 1].Volume : -1m;
                decimal nextLowVol = lowIndex - 1 >= 0 ? bucketList[lowIndex - 1].Volume : -1m;

                if (nextHighVol >= nextLowVol && nextHighVol > 0)
                {
                    highIndex++;
                    cum += bucketList[highIndex].Volume;
                }
                else if (nextLowVol > 0)
                {
                    lowIndex--;
                    cum += bucketList[lowIndex].Volume;
                }
                else
                {
                    break; // nothing left
                }
            }

            result.VAH = bucketList[highIndex].Price + bucketWidth / 2m;
            result.VAL = bucketList[lowIndex].Price - bucketWidth / 2m;

            // Copy buckets into result (center prices)
            foreach (var kv in bucketsDict)
            {
                result.PriceBuckets[kv.Key] = kv.Value;
            }

            return result;
        }

        private static decimal NearestBucketKey(IEnumerable<decimal> keys, decimal price)
        {
            decimal best = keys.First();
            decimal bestDiff = Math.Abs(best - price);
            foreach (var k in keys)
            {
                var d = Math.Abs(k - price);
                if (d < bestDiff)
                {
                    best = k; bestDiff = d;
                }
            }
            return best;
        }
    }
}
