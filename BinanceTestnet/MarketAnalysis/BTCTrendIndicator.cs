using System;
using System.Collections.Generic;
using System.Linq;
using BinanceTestnet.Models; // or wherever your Candle class lives

namespace BinanceTestnet.MarketAnalysis
{
    public class BTCIndicatorSet
    {
        public decimal Price { get; set; }
        public decimal EMA50 { get; set; }
        public decimal EMA100 { get; set; }
        public decimal EMA200 { get; set; }
        public decimal RSI { get; set; }
        public decimal ATR { get; set; }
        public decimal Volume { get; set; }
        public DateTime Timestamp { get; set; }
        public string Timeframe { get; set; } // 1h, 4h, 1d
        
        // Derived properties
        public decimal PriceVs200EMA => EMA200 > 0 ? ((Price - EMA200) / EMA200) * 100 : 0;
        public bool IsAlignedBullish => EMA50 > EMA100 && EMA100 > EMA200;
        public bool IsAlignedBearish => EMA50 < EMA100 && EMA100 < EMA200;
        public decimal EMASpread => EMA200 > 0 ? ((EMA50 - EMA200) / EMA200) * 100 : 0;
    }

    public class BTCTrendAnalysis
    {
        public BTCIndicatorSet Primary1H { get; set; }
        public BTCIndicatorSet Secondary4H { get; set; }
        public BTCIndicatorSet Daily { get; set; }
        
        public string DominantTrendTimeframe { get; set; }
        public decimal CompositeTrendScore { get; set; } // 0-100
        
        // Multi-timeframe confirmation
        public bool HasBullishConfirmation => 
            (Primary1H?.IsAlignedBullish == true || Secondary4H?.IsAlignedBullish == true) &&
            Primary1H?.PriceVs200EMA > 0;

        public bool HasBearishConfirmation =>
            (Primary1H?.IsAlignedBearish == true || Secondary4H?.IsAlignedBearish == true) &&
            Primary1H?.PriceVs200EMA < 0;
    }

    public static class BTCTrendCalculator
    {
        public static decimal CalculateEMA(IEnumerable<decimal> prices, int period)
        {
            var priceList = prices.ToList();
            if (priceList.Count < period) return 0;
            
            decimal multiplier = 2.0m / (period + 1);
            decimal ema = priceList.Take(period).Average();
            
            for (int i = period; i < priceList.Count; i++)
            {
                ema = (priceList[i] - ema) * multiplier + ema;
            }
            
            return ema;
        }

        public static decimal CalculateRSI(IEnumerable<decimal> prices, int period = 14)
        {
            var priceList = prices.ToList();
            if (priceList.Count <= period) return 50; // Neutral if insufficient data
            
            decimal avgGain = 0;
            decimal avgLoss = 0;
            
            // Initial calculation
            for (int i = 1; i <= period; i++)
            {
                decimal change = priceList[i] - priceList[i - 1];
                if (change > 0) avgGain += change;
                else avgLoss += Math.Abs(change);
            }
            
            avgGain /= period;
            avgLoss /= period;
            
            // Subsequent calculations
            for (int i = period + 1; i < priceList.Count; i++)
            {
                decimal change = priceList[i] - priceList[i - 1];
                decimal gain = change > 0 ? change : 0;
                decimal loss = change < 0 ? Math.Abs(change) : 0;
                
                avgGain = (avgGain * (period - 1) + gain) / period;
                avgLoss = (avgLoss * (period - 1) + loss) / period;
            }
            
            if (avgLoss == 0) return 100;
            decimal rs = avgGain / avgLoss;
            return 100 - (100 / (1 + rs));
        }

        public static List<decimal> CalculateATRValues(List<Kline> klines, int atrPeriod = 14, int count = 20)
        {
            var atrValues = new List<decimal>();
            
            if (klines.Count < atrPeriod + count) 
                return atrValues;

            // Calculate ATR for the last 'count' periods
            for (int i = klines.Count - count; i < klines.Count; i++)
            {
                if (i >= atrPeriod)
                {
                    var periodKlines = klines.Take(i + 1).ToList(); // All klines up to current
                    var atr = CalculateATR(periodKlines, atrPeriod);
                    atrValues.Add(atr);
                }
            }
            
            return atrValues;
        }

        // Overload for single ATR calculation that handles edge cases better
        public static decimal CalculateATR(List<Kline> klines, int period = 14)
        {
            if (klines == null || klines.Count < period + 1)
                return 0;

            var trueRanges = new List<decimal>();
            
            for (int i = 1; i < klines.Count; i++)
            {
                var current = klines[i];
                var previous = klines[i - 1];
                
                decimal highLow = current.High - current.Low;
                decimal highClose = Math.Abs(current.High - previous.Close);
                decimal lowClose = Math.Abs(current.Low - previous.Close);
                
                trueRanges.Add(Math.Max(highLow, Math.Max(highClose, lowClose)));
            }
            
            // Simple average for the period
            return trueRanges.TakeLast(period).Average();
        }
    }
}