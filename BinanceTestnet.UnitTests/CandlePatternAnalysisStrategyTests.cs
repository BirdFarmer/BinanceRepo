using System;
using System.Collections.Generic;
using System.Linq;
using BinanceTestnet.Models;
using BinanceTestnet.Strategies;
using Xunit;

namespace BinanceTestnet.UnitTests
{
    public class CandlePatternAnalysisStrategyTests
    {
        private static Kline MakeK(long openTime, decimal o, decimal h, decimal l, decimal c, decimal vol = 1m, string symbol = "BTCUSDT")
        {
            return new Kline
            {
                OpenTime = openTime,
                CloseTime = openTime + 60000,
                Open = o,
                High = h,
                Low = l,
                Close = c,
                Volume = vol,
                Symbol = symbol
            };
        }

        [Fact]
        public void DetectsBullishBreakoutAfterThreeIndecisive()
        {
            // previous three indecisive candles (small bodies)
            var klines = new List<Kline>
            {
                MakeK(0, 100, 102, 98, 100.5m, 10),
                MakeK(60000, 101, 103, 99, 101.2m, 12),
                MakeK(120000, 100.8m, 102.5m, 99.5m, 101m, 11),
                // breakout candle close > rangeHigh
                MakeK(180000, 101m, 106m, 100m, 106.5m, 50)
            };

            // ensure threshold considers previous three as indecisive
            CandlePatternAnalysisStrategy.IndecisiveThreshold = 0.5m; // loose so they're indecisive

            var ok = CandlePatternAnalysisStrategy.TryDetectSignal(klines, 3, out var signal, out var rangeHigh, out var rangeLow);
            Assert.True(ok);
            Assert.Equal("BULL", signal);
            Assert.True(rangeHigh > rangeLow);
        }

        [Fact]
        public void DetectsBearishBreakoutAfterThreeIndecisive()
        {
            var klines = new List<Kline>
            {
                MakeK(0, 100, 102, 98, 100.5m, 10),
                MakeK(60000, 101, 103, 99, 101.2m, 12),
                MakeK(120000, 100.8m, 102.5m, 99.5m, 101m, 11),
                // breakout candle close < rangeLow
                MakeK(180000, 100m, 101m, 95m, 94.5m, 60)
            };

            CandlePatternAnalysisStrategy.IndecisiveThreshold = 0.5m;

            var ok = CandlePatternAnalysisStrategy.TryDetectSignal(klines, 3, out var signal, out var rangeHigh, out var rangeLow);
            Assert.True(ok);
            Assert.Equal("BEAR", signal);
            Assert.True(rangeHigh > rangeLow);
        }

        [Fact]
        public void DoesNotTriggerWhenNotThreeIndecisive()
        {
            var klines = new List<Kline>
            {
                MakeK(0, 100, 110, 90, 105m, 10), // large body
                MakeK(60000, 105, 106, 104, 105.5m, 12),
                MakeK(120000, 105.4m, 106.5m, 104.8m, 106m, 11),
                MakeK(180000, 106m, 107m, 105m, 107.5m, 50)
            };

            CandlePatternAnalysisStrategy.IndecisiveThreshold = 0.3m;

            var ok = CandlePatternAnalysisStrategy.TryDetectSignal(klines, 3, out var signal, out var rangeHigh, out var rangeLow);
            Assert.False(ok);
        }
    }
}
