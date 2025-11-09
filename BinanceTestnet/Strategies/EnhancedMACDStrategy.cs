using BinanceTestnet.Models;
using BinanceTestnet.Trading;
using Newtonsoft.Json;
using RestSharp;
using Skender.Stock.Indicators;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace BinanceTestnet.Strategies
{
    public class EnhancedMACDStrategy : StrategyBase
    {
        protected override bool SupportsClosedCandles => true;
        public EnhancedMACDStrategy(RestClient client, string apiKey, OrderManager orderManager, Wallet wallet)
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
                if (response.IsSuccessful)
                {
                    if (string.IsNullOrWhiteSpace(response.Content))
                    {
                        Console.WriteLine($"No content for {symbol}.");
                        return;
                    }
                    var klines = Helpers.StrategyUtils.ParseKlines(response.Content);

                    if (klines != null && klines.Count > 0)
                    {
                        // Build quotes respecting closed-candle policy (exclude forming candle when enabled)
                        var workingKlines = UseClosedCandles ? Helpers.StrategyUtils.ExcludeForming(klines) : klines;
                        var quotes = workingKlines.Select(k => new BinanceTestnet.Models.Quote
                        {
                            Date = DateTimeOffset.FromUnixTimeMilliseconds(k.OpenTime).UtcDateTime,
                            Close = k.Close
                        }).ToList();

                        var macdResults = Indicator.GetMacd(quotes, 12, 26, 9).ToList();
                        var rsiResults = Indicator.GetRsi(quotes, 14).ToList();
                        var bbResults = Indicator.GetBollingerBands(quotes, 20, 2).ToList();
                        var emaShort = Indicator.GetEma(quotes, 5).ToList();
                        var emaLong = Indicator.GetEma(quotes, 20).ToList();

                        if (macdResults.Count > 1)
                        {
                            var lastMacd = macdResults[macdResults.Count - 1];
                            var prevMacd = macdResults[macdResults.Count - 2];
                            var lastRsi = rsiResults[rsiResults.Count - 1];
                            var lastBb = bbResults[bbResults.Count - 1];
                            var lastEmaShort = emaShort[emaShort.Count - 1];
                            var lastEmaLong = emaLong[emaLong.Count - 1];

                            // Select signal candle respecting policy
                            var (signalKline, previousKline) = SelectSignalPair(klines);
                            if (signalKline == null || previousKline == null)
                                return;

                            // Long Signal: MACD bullish cross + EMA confirmation
                            if (lastMacd.Macd > lastMacd.Signal && prevMacd.Macd <= prevMacd.Signal
                                && lastEmaShort.Ema > lastEmaLong.Ema)
                            {
                                await OrderManager.PlaceLongOrderAsync(symbol, signalKline.Close, "Enhanced MACD", signalKline.OpenTime);
                                Helpers.StrategyUtils.TraceSignalCandle("EnhancedMACD", symbol, UseClosedCandles, signalKline, previousKline, "Bullish MACD cross + EMA alignment");
                                LogTradeSignal("LONG", symbol, signalKline.Close);
                            }
                            // Short Signal: MACD bearish cross + EMA confirmation
                            else if (lastMacd.Macd < lastMacd.Signal && prevMacd.Macd >= prevMacd.Signal
                                     && lastEmaShort.Ema < lastEmaLong.Ema)
                            {
                                await OrderManager.PlaceShortOrderAsync(symbol, signalKline.Close, "Enhanced MACD", signalKline.OpenTime);
                                Helpers.StrategyUtils.TraceSignalCandle("EnhancedMACD", symbol, UseClosedCandles, signalKline, previousKline, "Bearish MACD cross + EMA alignment");
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

        public override async Task RunOnHistoricalDataAsync(IEnumerable<BinanceTestnet.Models.Kline> historicalData)
        {
            var klines = historicalData.ToList();
            var workingKlines = UseClosedCandles ? Helpers.StrategyUtils.ExcludeForming(klines) : klines;
            var quotes = workingKlines.Select(k => new BinanceTestnet.Models.Quote
            {
                Date = DateTimeOffset.FromUnixTimeMilliseconds(k.OpenTime).UtcDateTime,
                Close = k.Close
            }).ToList();

            var macdResults = Indicator.GetMacd(quotes, 12, 26, 9).ToList();
            var rsiResults = Indicator.GetRsi(quotes, 14).ToList();
            var bbResults = Indicator.GetBollingerBands(quotes, 20, 2).ToList();
            var emaShort = Indicator.GetEma(quotes, 5).ToList();
            var emaLong = Indicator.GetEma(quotes, 20).ToList();

            for (int i = 1; i < macdResults.Count; i++)
            {
                var lastMacd = macdResults[i];
                var prevMacd = macdResults[i - 1];
                var prevRsi = rsiResults[i - 1];
                var lastRsi = rsiResults[i];
                var lastBb = bbResults[i];
                var lastEmaShort = emaShort[i];
                var lastEmaLong = emaLong[i];
                var kline = workingKlines.ElementAt(i);
                
                // Long Signal
                if (lastMacd.Macd > lastMacd.Signal && prevMacd.Macd <= prevMacd.Signal 
                    && lastRsi.Rsi > prevRsi.Rsi
                    //(double)kline.Low < lastBb.LowerBand 
                    && (double)kline.Low > lastEmaLong.Ema
                    )
                {
                    if (!string.IsNullOrEmpty(kline.Symbol))
                    {
                        await OrderManager.PlaceLongOrderAsync(kline.Symbol, kline.Close, "Enhanced MACD", kline.OpenTime);
                        LogTradeSignal("LONG", kline.Symbol!, kline.Close);
                    }
                }

                // Short Signal
                else if (lastMacd.Macd < lastMacd.Signal && prevMacd.Macd >= prevMacd.Signal 
                         && lastRsi.Rsi < prevRsi.Rsi
                         //(double)kline.High > lastBb.UpperBand
                         && (double)kline.High < lastEmaLong.Ema
                         )
                {
                    if (!string.IsNullOrEmpty(kline.Symbol))
                    {
                        await OrderManager.PlaceShortOrderAsync(kline.Symbol, kline.Close, "Enhanced MACD", kline.OpenTime);
                        LogTradeSignal("SHORT", kline.Symbol!, kline.Close);
                    }
                }

                if (!string.IsNullOrEmpty(kline.Symbol))
                {
                    var currentPrices = new Dictionary<string, decimal> { { kline.Symbol, kline.Close } };
                    await OrderManager.CheckAndCloseTrades(currentPrices, kline.OpenTime);
                }
            }
        }

        // Parsing now delegated to StrategyUtils.

        // Request creation now delegated to StrategyUtils.

        private void LogTradeSignal(string direction, string symbol, decimal price)
        {

            Console.WriteLine($"****** Enhanced MACD Strategy ******************");
            Console.WriteLine($"Go {direction} on {symbol} @ {price} at {DateTime.Now:HH:mm:ss}");
            if(direction == "LONG")
                Console.WriteLine($"MACd crossed over Signal and fast EMA is above slow EMA, {symbol} trying to go LONG");
            else   
                Console.WriteLine($"MACd crossed below Signal and fast EMA is below slow EMA, {symbol} trying to go SHORT");
            Console.WriteLine($"************************************************");

        }

        private void HandleErrorResponse(string symbol, RestResponse response)
        {
            Console.WriteLine($"Error for {symbol}: {response.ErrorMessage}");
            Console.WriteLine($"Status Code: {response.StatusCode}");
            Console.WriteLine($"Content: {response.Content}");
        }
    }
}
