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

namespace BinanceLive.Strategies
{
    public class EnhancedMACDStrategy : StrategyBase
    {
        public EnhancedMACDStrategy(RestClient client, string apiKey, OrderManager orderManager, Wallet wallet)
            : base(client, apiKey, orderManager, wallet)
        {
        }

        public override async Task RunAsync(string symbol, string interval)
        {
            try
            {
                var request = CreateRequest("/fapi/v1/klines");
                request.AddParameter("symbol", symbol, ParameterType.QueryString);
                request.AddParameter("interval", interval, ParameterType.QueryString);
                request.AddParameter("limit", "401", ParameterType.QueryString);

                var response = await Client.ExecuteGetAsync(request);
                if (response.IsSuccessful)
                {
                    var klines = ParseKlines(response.Content);

                    if (klines != null && klines.Count > 0)
                    {
                        var quotes = klines.Select(k => new BinanceTestnet.Models.Quote
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

                            // Long Signal
                            if (lastMacd.Macd > lastMacd.Signal && prevMacd.Macd <= prevMacd.Signal 
                                && lastEmaShort.Ema > lastEmaLong.Ema
                                )
                            {
                                Console.WriteLine($"MACd crossed over Signal and fast EMA is above slow EMA, {symbol} trying to go LONG");
                                await OrderManager.PlaceLongOrderAsync(symbol, klines.Last().Close, "Enhanced MACD", klines.Last().CloseTime);
                                LogTradeSignal("LONG", symbol, klines.Last().Close);
                            }

                            // Short Signal
                            else if (lastMacd.Macd < lastMacd.Signal && prevMacd.Macd >= prevMacd.Signal 
                                     && lastEmaShort.Ema < lastEmaLong.Ema
                                     )
                            {   
                                Console.WriteLine($"MACd crossed below Signal and fast EMA is below slow EMA, {symbol} trying to go SHORT");
                                await OrderManager.PlaceShortOrderAsync(symbol, klines.Last().Close, "Enhanced MACD", klines.Last().CloseTime);
                                LogTradeSignal("SHORT", symbol, klines.Last().Close);
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
            var quotes = historicalData.Select(k => new BinanceTestnet.Models.Quote
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
                var kline = historicalData.ElementAt(i);



                // Long Signal
                if (lastMacd.Macd > lastMacd.Signal && prevMacd.Macd <= prevMacd.Signal 
                    && lastRsi.Rsi > prevRsi.Rsi
                    //(double)kline.Low < lastBb.LowerBand 
                    && (double)kline.Low > lastEmaLong.Ema
                    )
                {
                    await OrderManager.PlaceLongOrderAsync(kline.Symbol, kline.Close, "Enhanced MACD", kline.CloseTime);
                    LogTradeSignal("LONG", kline.Symbol, kline.Close);
                }

                // Short Signal
                else if (lastMacd.Macd < lastMacd.Signal && prevMacd.Macd >= prevMacd.Signal 
                         && lastRsi.Rsi < prevRsi.Rsi
                         //(double)kline.High > lastBb.UpperBand
                         && (double)kline.High < lastEmaLong.Ema
                         )
                {
                    await OrderManager.PlaceShortOrderAsync(kline.Symbol, kline.Close, "Enhanced MACD", kline.CloseTime);
                    LogTradeSignal("SHORT", kline.Symbol, kline.Close);
                }

                var currentPrices = new Dictionary<string, decimal> { { kline.Symbol, kline.Close } };
                await OrderManager.CheckAndCloseTrades(currentPrices);
            }
        }

        private List<BinanceTestnet.Models.Kline>? ParseKlines(string content)
        {
            try
            {
                return JsonConvert.DeserializeObject<List<List<object>>>(content)
                    ?.Select(k =>
                    {
                        var kline = new BinanceTestnet.Models.Kline();
                        if (k.Count >= 9)
                        {
                            kline.Open = k[1] != null && decimal.TryParse(k[1].ToString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var open) ? open : 0;
                            kline.High = k[2] != null && decimal.TryParse(k[2].ToString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var high) ? high : 0;
                            kline.Low = k[3] != null && decimal.TryParse(k[3].ToString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var low) ? low : 0;
                            kline.Close = k[4] != null && decimal.TryParse(k[4].ToString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var close) ? close : 0;
                            kline.OpenTime = Convert.ToInt64(k[0]);
                            kline.CloseTime = Convert.ToInt64(k[6]);
                            kline.NumberOfTrades = Convert.ToInt32(k[8]);
                        }
                        return kline;
                    })
                    .ToList();
            }
            catch (JsonException ex)
            {
                Console.WriteLine($"JSON Deserialization error: {ex.Message}");
                return null;
            }
        }

        private RestRequest CreateRequest(string resource)
        {
            var request = new RestRequest(resource, Method.Get);
            request.AddHeader("Content-Type", "application/json");
            request.AddHeader("Accept", "application/json");

            return request;
        }

        private void LogTradeSignal(string direction, string symbol, decimal price)
        {
/*
            Console.WriteLine($"****** Enhanced MACD Strategy ******************");
            Console.WriteLine($"Go {direction} on {symbol} @ {price} at {DateTime.Now:HH:mm:ss}");
            Console.WriteLine($"************************************************");
*/
        }

        private void HandleErrorResponse(string symbol, RestResponse response)
        {
            Console.WriteLine($"Error for {symbol}: {response.ErrorMessage}");
            Console.WriteLine($"Status Code: {response.StatusCode}");
            Console.WriteLine($"Content: {response.Content}");
        }
    }
}
