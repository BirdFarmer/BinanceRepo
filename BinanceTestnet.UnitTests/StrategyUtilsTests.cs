using System;
using System.Collections.Generic;
using System.Linq;
using BinanceTestnet.Models;
using BinanceTestnet.Strategies.Helpers;
using Xunit;

namespace BinanceTestnet.UnitTests
{
    public class StrategyUtilsTests
    {
        [Fact]
        public void ParseKlines_BadJson_ReturnsEmpty()
        {
            var result = StrategyUtils.ParseKlines("not-json");
            Assert.Empty(result);
        }

        [Fact]
        public void ParseKlines_GoodJson_ParsesFields()
        {
            // minimal two klines
            var json = "[[1609459200000,\"100\",\"105\",\"95\",\"102\",\"123\",1609459260000,\"0\",10],[1609459260000,\"102\",\"106\",\"98\",\"104\",\"125\",1609459320000,\"0\",11]]";
            var list = StrategyUtils.ParseKlines(json);
            Assert.Equal(2, list.Count);
            Assert.Equal(100m, list[0].Open);
            Assert.Equal(104m, list[1].Close);
            Assert.Equal(10, list[0].NumberOfTrades);
        }

        [Fact]
        public void CalculateEHMA_EdgeLengths_ReturnsAlignedList()
        {
            var quotes = Enumerable.Range(0, 20).Select(i => new Quote
            {
                Date = DateTime.UtcNow.AddMinutes(i),
                Open = i,
                High = i + 1,
                Low = i - 1,
                Close = i,
                Volume = 1
            }).ToList();

            var res = StrategyUtils.CalculateEHMA(quotes, 0);
            Assert.Equal(quotes.Count, res.Count);
            Assert.All(res, r => Assert.Equal(0m, r.EHMA));
        }

        [Fact]
        public void BucketOrders_RoundsAndAggregates()
        {
            var orders = new List<List<decimal>>
            {
                new() { 100.12345m, 1m },
                new() { 100.12346m, 2m },
                new() { 101.9999m, 1.5m }
            };
            var bucketed = StrategyUtils.BucketOrders(orders, significantDigits: 4);
            Assert.True(bucketed.Count >= 2);
            var sumNear100 = bucketed.Where(kv => Math.Floor((double)kv.Key) == 100).Sum(kv => kv.Value);
            Assert.True(sumNear100 >= 3m);
        }

        [Fact]
        public void ToIndicatorQuotes_ClosedMode_TrimsForming()
        {
            var klines = Enumerable.Range(0, 5).Select(i => new Kline
            {
                OpenTime = i * 60_000,
                CloseTime = (i + 1) * 60_000,
                Open = i,
                High = i + 1,
                Low = i - 1,
                Close = i,
                Volume = i
            }).ToList();

            var quotesClosed = StrategyUtils.ToIndicatorQuotes(klines, useClosedCandle: true);
            var quotesForming = StrategyUtils.ToIndicatorQuotes(klines, useClosedCandle: false);
            Assert.Equal(klines.Count - 1, quotesClosed.Count);
            Assert.Equal(klines.Count, quotesForming.Count);
        }
    }
}
