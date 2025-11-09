using System;
using System.Collections.Generic;
using BinanceTestnet.Models;
using BinanceTestnet.Strategies.Helpers;
using Xunit;

namespace BinanceTestnet.UnitTests
{
    public class StrategySignalPairTests
    {
        private List<Kline> BuildKlines(int count)
        {
            var list = new List<Kline>();
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            for (int i = 0; i < count; i++)
            {
                list.Add(new Kline
                {
                    Open = 100 + i,
                    High = 101 + i,
                    Low = 99 + i,
                    Close = 100 + i,
                    OpenTime = now + i * 60000,
                    CloseTime = now + (i * 60000) + 60000,
                    Volume = 10 + i,
                    NumberOfTrades = i
                });
            }
            return list;
        }

        [Fact]
        public void SelectSignalPair_FormingMode_UsesLastAndPrevious()
        {
            var klines = BuildKlines(5);
            var (signal, previous) = StrategyUtils.SelectSignalPair(klines, useClosedCandle: false);
            Assert.NotNull(signal);
            Assert.NotNull(previous);
            Assert.Equal(klines[^1].CloseTime, signal!.CloseTime);
            Assert.Equal(klines[^2].CloseTime, previous!.CloseTime);
        }

        [Fact]
        public void SelectSignalPair_ClosedMode_UsesLastClosedAndPreviousClosed()
        {
            var klines = BuildKlines(6); // need >=3 for closed selection
            var (signal, previous) = StrategyUtils.SelectSignalPair(klines, useClosedCandle: true);
            Assert.NotNull(signal);
            Assert.NotNull(previous);
            Assert.Equal(klines[^2].CloseTime, signal!.CloseTime); // last closed
            Assert.Equal(klines[^3].CloseTime, previous!.CloseTime); // previous closed
        }

        [Fact]
        public void SelectSignalPair_ClosedMode_InsufficientKlines_ReturnsNulls()
        {
            var klines = BuildKlines(2); // insufficient for closed mode
            var (signal, previous) = StrategyUtils.SelectSignalPair(klines, useClosedCandle: true);
            Assert.Null(signal);
            Assert.Null(previous);
        }
    }
}
