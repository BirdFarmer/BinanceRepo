using BinanceTestnet.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BinanceTestnet.Strategies.VolumeProfile
{
    // Reuses VolumeProfileResult defined in VolumeProfileCalculator.cs
    public static class FixedRangeVolumeProfileCalculator
    {
        // Build a fixed-range volume profile from OHLCV klines by distributing each bar's
        // volume proportionally across the price interval [low, high]. This approximates
        // the traded-volume profile when trade ticks are not available.
        public static VolumeProfileResult BuildFromKlines(List<Kline> klines, int buckets = 100, decimal valueAreaPct = 0.70m)
        {
            var result = new VolumeProfileResult();
            if (klines == null || klines.Count == 0) return result;

            decimal globalLow = klines.Min(k => k.Low);
            decimal globalHigh = klines.Max(k => k.High);
            if (globalHigh <= globalLow)
            {
                decimal center = globalLow;
                result.PriceBuckets[center] = klines.Sum(k => k.Volume);
                result.POC = center;
                result.VAH = center;
                result.VAL = center;
                return result;
            }

            // Prepare buckets by their lower edge; we'll expose centers as key when returning
            decimal span = globalHigh - globalLow;
            decimal bucketWidth = span / buckets;

            var bucketVolumes = new decimal[buckets];

            // Distribute each candle's volume across overlapping buckets proportionally to overlap length
            foreach (var k in klines)
            {
                decimal low = k.Low;
                decimal high = k.High;
                if (high <= low)
                {
                    // allocate to nearest bucket center
                    int idx = (int)Math.Floor((double)((low - globalLow) / bucketWidth));
                    idx = Math.Max(0, Math.Min(buckets - 1, idx));
                    bucketVolumes[idx] += k.Volume;
                    continue;
                }

                // For each bucket compute overlap with candle range
                for (int i = 0; i < buckets; i++)
                {
                    decimal bLow = globalLow + i * bucketWidth;
                    decimal bHigh = bLow + bucketWidth;
                    decimal overlapLow = Math.Max(bLow, low);
                    decimal overlapHigh = Math.Min(bHigh, high);
                    decimal overlap = overlapHigh - overlapLow;
                    if (overlap > 0)
                    {
                        decimal frac = overlap / (high - low);
                        bucketVolumes[i] += k.Volume * frac;
                    }
                }
            }

            // Build bucket list (center price -> volume)
            var bucketList = new List<(decimal PriceCenter, decimal Volume)>();
            for (int i = 0; i < buckets; i++)
            {
                decimal bLow = globalLow + i * bucketWidth;
                decimal center = bLow + bucketWidth / 2m;
                bucketList.Add((center, bucketVolumes[i]));
                result.PriceBuckets[center] = bucketVolumes[i];
            }

            decimal totalVol = bucketList.Sum(b => b.Volume);
            if (totalVol <= 0)
            {
                // fallback to close-weighted
                decimal weighted = klines.Sum(k => k.Close * k.Volume) / Math.Max(1, klines.Sum(k => k.Volume));
                result.POC = weighted;
                result.VAH = weighted;
                result.VAL = weighted;
                return result;
            }

            // POC: center with max volume
            var poc = bucketList.OrderByDescending(b => b.Volume).First();
            result.POC = poc.PriceCenter;

            // Ensure POC is inside the session bounds (guard against numerical edge cases)
            var minCenter = bucketList.First().PriceCenter;
            var maxCenter = bucketList.Last().PriceCenter;
            if (result.POC < minCenter || result.POC > maxCenter)
            {
                // Clamp to nearest center
                var clamped = Math.Min(Math.Max(result.POC, minCenter), maxCenter);
                result.POC = clamped;
            }

            // Compute Value Area around POC
            int pocIndex = bucketList.FindIndex(b => b.PriceCenter == poc.PriceCenter);
            int lowIndex = pocIndex;
            int highIndex = pocIndex;
            decimal cum = bucketList[pocIndex].Volume;
            decimal target = totalVol * valueAreaPct;

            while (cum < target)
            {
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
                    break;
                }
            }

            // VAH/VAL map to bucket edges; clamp to session bounds
            var vahRaw = bucketList[highIndex].PriceCenter + bucketWidth / 2m;
            var valRaw = bucketList[lowIndex].PriceCenter - bucketWidth / 2m;
            var sessionLow = bucketList.First().PriceCenter - bucketWidth / 2m;
            var sessionHigh = bucketList.Last().PriceCenter + bucketWidth / 2m;

            result.VAH = Math.Min(Math.Max(vahRaw, sessionLow), sessionHigh);
            result.VAL = Math.Min(Math.Max(valRaw, sessionLow), sessionHigh);

            return result;
        }

        // Placeholder: build from trades if trade/tick history is later added.
        // public static VolumeProfileResult BuildFromTrades(IEnumerable<Trade> trades, decimal tickSize, decimal valueAreaPct = 0.70m) { ... }
    }
}
