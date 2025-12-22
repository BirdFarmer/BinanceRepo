using BinanceTestnet.Models;
using Newtonsoft.Json;
using RestSharp;
using Skender.Stock.Indicators;
using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using BinanceTestnet.Trading;

namespace BinanceTestnet.Strategies
{
    public class MACDStandardStrategy : StrategyBase, ISnapshotAwareStrategy
    {
        protected override bool SupportsClosedCandles => true;
        public MACDStandardStrategy(RestClient client, string apiKey, OrderManager orderManager, Wallet wallet) 
        : base(client, apiKey, orderManager, wallet)
        {
        }

        public override async Task RunAsync(string symbol, string interval)
        {
            try
            {
                var request = Helpers.StrategyUtils.CreateGet("/fapi/v1/klines", new Dictionary<string,string>
                {
                    {"symbol", symbol},
                    {"interval", interval},
                    {"limit", "401"}
                });

                var response = await Client.ExecuteGetAsync(request);
                if (response.IsSuccessful && response.Content != null)
                {
                    var klines = Helpers.StrategyUtils.ParseKlines(response.Content);

                    if (klines != null && klines.Count > 0)
                    {
                        // Build indicator quotes respecting closed-candle policy
                        var quotes = ToIndicatorQuotes(klines)
                            .Select(q => new BinanceTestnet.Models.Quote { Date = q.Date, Close = q.Close })
                            .ToList();

                        // Core MACD
                        var macdResults = Indicator.GetMacd(quotes, 12, 26, 9).ToList();
                        // Added minimal tuning: light trend filter using EMA(50).
                        // Rationale: reduce whipsaw during flat / counter-trend phases while preserving closed-candle policy.
                        var emaTrend = Indicator.GetEma(quotes, 50).ToList();

                        if (macdResults.Count > 1)
                        {
                            var lastMacd = macdResults[macdResults.Count - 1];
                            var prevMacd = macdResults[macdResults.Count - 2];

                            var (signalKline, previousKline) = SelectSignalPair(klines);
                            if (signalKline == null || previousKline == null) return;

                            // Map latest EMA value (same indexing as quotes/macd) for trend confirmation
                            var lastEma = emaTrend.Count == quotes.Count ? emaTrend[emaTrend.Count - 1].Ema : null;

                            bool trendFilterLong = lastEma == null || signalKline.Close > (decimal)lastEma; // allow if EMA unavailable
                            bool trendFilterShort = lastEma == null || signalKline.Close < (decimal)lastEma;

                            if (lastMacd.Macd > lastMacd.Signal && prevMacd.Macd <= prevMacd.Signal && trendFilterLong)
                            {
                                await OrderManager.PlaceLongOrderAsync(symbol, signalKline.Close, "MAC-D", signalKline.OpenTime);
                                Helpers.StrategyUtils.TraceSignalCandle("MACDStandard", symbol, UseClosedCandles, signalKline, previousKline, "Bullish MACD cross");
                                LogTradeSignal("LONG", symbol, signalKline.Close);
                            }
                            else if (lastMacd.Macd < lastMacd.Signal && prevMacd.Macd >= prevMacd.Signal && trendFilterShort)
                            {
                                await OrderManager.PlaceShortOrderAsync(symbol, signalKline.Close, "MAC-D", signalKline.OpenTime);
                                Helpers.StrategyUtils.TraceSignalCandle("MACDStandard", symbol, UseClosedCandles, signalKline, previousKline, "Bearish MACD cross");
                                LogTradeSignal("SHORT", symbol, signalKline.Close);
                            }
                        }
                    }
                    else
                    {
                        Console.WriteLine($"No klines data available for {symbol}.");
                    }
                }
                else
                {
                    HandleErrorResponse(symbol, response);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing {symbol}: {ex.Message}");
            }
        }

        public async Task RunAsyncWithSnapshot(string symbol, string interval, Dictionary<string, List<Kline>> snapshot)
        {
            try
            {
                List<Kline> klines = null;
                if (snapshot != null && snapshot.TryGetValue(symbol, out var s) && s != null && s.Count > 0)
                {
                    klines = s;
                }

                if (klines == null)
                {
                    var request = Helpers.StrategyUtils.CreateGet("/fapi/v1/klines", new Dictionary<string,string>
                    {
                        {"symbol", symbol},
                        {"interval", interval},
                        {"limit", "401"}
                    });

                    var response = await Client.ExecuteGetAsync(request);
                    if (response.IsSuccessful && response.Content != null)
                    {
                        klines = Helpers.StrategyUtils.ParseKlines(response.Content);
                    }
                    else
                    {
                        HandleErrorResponse(symbol, response);
                        return;
                    }
                }

                if (klines != null && klines.Count > 0)
                {
                    await ProcessKlinesAsync(symbol, klines);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing {symbol} with snapshot: {ex.Message}");
            }
        }


        // Parsing and request creation centralized in StrategyUtils

        private void LogTradeSignal(string direction, string symbol, decimal price)
        {
            Console.WriteLine($"****** MACD Standard Strategy ******************");
            Console.WriteLine($"Go {direction} on {symbol} @ {price} at {DateTime.Now:HH:mm:ss}");
            if(direction == "LONG")
                Console.WriteLine($"MAC-D crosses over signal line");
            else
                Console.WriteLine($"MAC-D crosses below signal line");
            Console.WriteLine($"************************************************");
        
        }

        private void HandleErrorResponse(string symbol, RestResponse response)
        {
            Console.WriteLine($"Error for {symbol}: {response.ErrorMessage}");
            Console.WriteLine($"Status Code: {response.StatusCode}");
            Console.WriteLine($"Content: {response.Content}");
        }

        private async Task ProcessKlinesAsync(string symbol, List<Kline> klines)
        {
            // Build indicator quotes respecting closed-candle policy
            var quotes = ToIndicatorQuotes(klines)
                .Select(q => new BinanceTestnet.Models.Quote { Date = q.Date, Close = q.Close })
                .ToList();

            // Core MACD
            var macdResults = Indicator.GetMacd(quotes, 12, 26, 9).ToList();
            var emaTrend = Indicator.GetEma(quotes, 50).ToList();

            if (macdResults.Count > 1)
            {
                var lastMacd = macdResults[macdResults.Count - 1];
                var prevMacd = macdResults[macdResults.Count - 2];

                var (signalKline, previousKline) = SelectSignalPair(klines);
                if (signalKline == null || previousKline == null) return;

                // Map latest EMA value (same indexing as quotes/macd) for trend confirmation
                var lastEma = emaTrend.Count == quotes.Count ? emaTrend[emaTrend.Count - 1].Ema : null;

                bool trendFilterLong = lastEma == null || signalKline.Close > (decimal)lastEma; // allow if EMA unavailable
                bool trendFilterShort = lastEma == null || signalKline.Close < (decimal)lastEma;

                if (lastMacd.Macd > lastMacd.Signal && prevMacd.Macd <= prevMacd.Signal && trendFilterLong)
                {
                    await OrderManager.PlaceLongOrderAsync(symbol, signalKline.Close, "MAC-D", signalKline.OpenTime);
                    Helpers.StrategyUtils.TraceSignalCandle("MACDStandard", symbol, UseClosedCandles, signalKline, previousKline, "Bullish MACD cross");
                    LogTradeSignal("LONG", symbol, signalKline.Close);
                }
                else if (lastMacd.Macd < lastMacd.Signal && prevMacd.Macd >= prevMacd.Signal && trendFilterShort)
                {
                    await OrderManager.PlaceShortOrderAsync(symbol, signalKline.Close, "MAC-D", signalKline.OpenTime);
                    Helpers.StrategyUtils.TraceSignalCandle("MACDStandard", symbol, UseClosedCandles, signalKline, previousKline, "Bearish MACD cross");
                    LogTradeSignal("SHORT", symbol, signalKline.Close);
                }
            }
        }

        public override async Task RunOnHistoricalDataAsync(IEnumerable<BinanceTestnet.Models.Kline> historicalData)
        {
            var quotes = historicalData.Select(k => new BinanceTestnet.Models.Quote
            {
                Date = DateTimeOffset.FromUnixTimeMilliseconds(k.OpenTime).UtcDateTime,
                Close = k.Close
            }).ToList();

            var macdResults = Indicator.GetMacd(quotes, 12, 26, 9).ToList();
            var emaTrend = Indicator.GetEma(quotes, 50).ToList();

            for (int i = 1; i < macdResults.Count; i++)
            {
                var lastMacd = macdResults[i];
                var prevMacd = macdResults[i - 1];
                var kline = historicalData.ElementAt(i);

                if(kline.Symbol == null) continue;

                var ema = emaTrend.Count > i ? emaTrend[i].Ema : null;
                bool trendFilterLong = ema == null || kline.Close > (decimal)ema;
                bool trendFilterShort = ema == null || kline.Close < (decimal)ema;

                if (lastMacd.Macd > lastMacd.Signal && prevMacd.Macd <= prevMacd.Signal && trendFilterLong)
                {
                    Helpers.StrategyUtils.TraceSignalCandle("MACDStandard-Hist", kline.Symbol, true, kline, null, "Bullish MACD cross (historical)");
                    await OrderManager.PlaceLongOrderAsync(kline.Symbol, kline.Close, "MAC-D", kline.CloseTime);
                    LogTradeSignal("LONG", kline.Symbol, kline.Close);
                }
                else if (lastMacd.Macd < lastMacd.Signal && prevMacd.Macd >= prevMacd.Signal && trendFilterShort)
                {
                    Helpers.StrategyUtils.TraceSignalCandle("MACDStandard-Hist", kline.Symbol, true, kline, null, "Bearish MACD cross (historical)");
                    await OrderManager.PlaceShortOrderAsync(kline.Symbol, kline.Close, "MAC-D", kline.CloseTime);
                    LogTradeSignal("SHORT", kline.Symbol, kline.Close);
                }
                
                var currentPrices = new Dictionary<string, decimal> { { kline.Symbol, kline.Close } };
                await OrderManager.CheckAndCloseTrades(currentPrices, kline.CloseTime);
            }
        }
    }
}
