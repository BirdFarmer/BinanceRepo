using System.Collections.Generic;
using System.Linq;
using BinanceTestnet.Models;
using BinanceTestnet.Strategies.Helpers;
using Xunit;

namespace BinanceTestnet.UnitTests
{
    public class RuntimeFlagBehaviorTests
    {
        private List<Kline> BuildKlines(int count)
        {
            var list = new List<Kline>();
            long t = 1_700_000_000_000; // arbitrary base
            for (int i = 0; i < count; i++)
            {
                list.Add(new Kline
                {
                    Open = 100 + i,
                    High = 101 + i,
                    Low = 99 + i,
                    Close = 100 + i,
                    OpenTime = t + i * 60_000,
                    CloseTime = t + (i + 1) * 60_000,
                    Volume = 1 + i,
                    NumberOfTrades = i
                });
            }
            return list;
        }

        [Fact]
        public void UseClosedCandles_False_DefaultsToForming()
        {
            StrategyRuntimeConfig.UseClosedCandles = false;
            var klines = BuildKlines(4);
            var quotes = StrategyUtils.ToIndicatorQuotes(klines, StrategyRuntimeConfig.UseClosedCandles);
            Assert.Equal(klines.Count, quotes.Count);
            var (signal, prev) = StrategyUtils.SelectSignalPair(klines, StrategyRuntimeConfig.UseClosedCandles);
            Assert.Equal(klines[^1].CloseTime, signal!.CloseTime);
            Assert.Equal(klines[^2].CloseTime, prev!.CloseTime);
        }

        [Fact]
        public void UseClosedCandles_True_UsesLastClosed()
        {
            StrategyRuntimeConfig.UseClosedCandles = true;
            var klines = BuildKlines(5);
            var quotes = StrategyUtils.ToIndicatorQuotes(klines, StrategyRuntimeConfig.UseClosedCandles);
            Assert.Equal(klines.Count - 1, quotes.Count);
            var (signal, prev) = StrategyUtils.SelectSignalPair(klines, StrategyRuntimeConfig.UseClosedCandles);
            Assert.Equal(klines[^2].CloseTime, signal!.CloseTime);
            Assert.Equal(klines[^3].CloseTime, prev!.CloseTime);
        }
    }
}
