using System;
using System.Collections.Generic;
using System.Linq;
using BinanceTestnet.Models;

namespace BinanceTestnet.Strategies.VolumeProfile
{
    public class OrderBookEntry
    {
        public decimal Price { get; set; }
        public decimal Quantity { get; set; }
    }

    public static class OrderBookVapCalculator
    {
        // Build a simple volume-at-price map from order-book depth (bids + asks)
        public static VolumeProfileResult BuildFromOrderBook(IEnumerable<OrderBookEntry> bids, IEnumerable<OrderBookEntry> asks, int buckets = 200, decimal valueAreaPct = 0.70m)
        {
            var all = new List<OrderBookEntry>();
            if (bids != null) all.AddRange(bids);
            if (asks != null) all.AddRange(asks);

            // Defensive: remove extreme outlier prices that can skew bucket span (e.g. malformed depth entries).
            // Compute median price and keep entries within a reasonable band around it.
            if (all.Count > 0)
            {
                var prices = all.Select(a => a.Price).OrderBy(p => p).ToList();
                decimal median = prices[prices.Count / 2];
                decimal lowerBound = median * 0.2m; // 20% of median
                decimal upperBound = median * 5m;   // 500% of median
                var filtered = all.Where(a => a.Price >= lowerBound && a.Price <= upperBound).ToList();
                if (filtered.Count != all.Count)
                {
                    Console.WriteLine($"[OrderBookVAP] Filtered out {all.Count - filtered.Count} outlier depth entries (median={median}). Using {filtered.Count} entries.");
                    all = filtered;
                }
            }

            var result = new VolumeProfileResult();
            if (all.Count == 0) return result;

            decimal minPrice = all.Min(e => e.Price);
            decimal maxPrice = all.Max(e => e.Price);
            if (maxPrice <= minPrice)
            {
                var center = minPrice;
                result.PriceBuckets[center] = all.Sum(a => a.Quantity);
                result.POC = center;
                result.VAH = center;
                result.VAL = center;
                return result;
            }

            decimal span = maxPrice - minPrice;
            decimal bucketWidth = span / buckets;
            var bucketsDict = new SortedDictionary<decimal, decimal>();
            for (int i = 0; i <= buckets; i++)
            {
                decimal key = minPrice + bucketWidth * i;
                bucketsDict[key] = 0m;
            }

            foreach (var e in all)
            {
                // map price to nearest bucket
                var nearest = NearestBucketKey(bucketsDict.Keys, e.Price);
                bucketsDict[nearest] += e.Quantity;
            }

            var bucketList = bucketsDict.Select(kv => new { Price = kv.Key, Volume = kv.Value }).ToList();
            decimal totalVol = bucketList.Sum(b => b.Volume);
            if (totalVol <= 0)
            {
                return result;
            }

            var pocBucket = bucketList.OrderByDescending(b => b.Volume).First();
            result.POC = pocBucket.Price;

            int pocIndex = bucketList.FindIndex(b => b.Price == pocBucket.Price);
            int lowIndex = pocIndex;
            int highIndex = pocIndex;
            decimal cum = bucketList[pocIndex].Volume;
            decimal target = totalVol * valueAreaPct;

            while (cum < target)
            {
                decimal nextHigh = highIndex + 1 < bucketList.Count ? bucketList[highIndex + 1].Volume : -1m;
                decimal nextLow = lowIndex - 1 >= 0 ? bucketList[lowIndex - 1].Volume : -1m;

                if (nextHigh >= nextLow && nextHigh > 0)
                {
                    highIndex++;
                    cum += bucketList[highIndex].Volume;
                }
                else if (nextLow > 0)
                {
                    lowIndex--;
                    cum += bucketList[lowIndex].Volume;
                }
                else break;
            }

            result.VAH = bucketList[highIndex].Price + bucketWidth / 2m;
            result.VAL = bucketList[lowIndex].Price - bucketWidth / 2m;

            foreach (var kv in bucketsDict)
                result.PriceBuckets[kv.Key] = kv.Value;

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
