// MACDDivergenceStrategy.cs
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

namespace BinanceLive.Strategies
{
    public class MACDDivergenceStrategy : StrategyBase
    {
        public MACDDivergenceStrategy(RestClient client, string apiKey, OrderManager orderManager, Wallet wallet) 
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
                if (response.IsSuccessful && response.Content != null)
                {
                    var klines = ParseKlines(response.Content);

                    if (klines != null && klines.Count > 0)
                    {
                        var quotes = klines.Select(k => new BinanceTestnet.Models.Quote
                        {
                            Date = DateTimeOffset.FromUnixTimeMilliseconds(k.OpenTime).UtcDateTime,
                            Close = k.Close
                        }).ToList();

                        var macdResults = Indicator.GetMacd(quotes, 25, 125, 9).ToList();
                        var divergence = IdentifyDivergence(macdResults);                       

                        if (divergence != 0)
                        {
                            if (divergence == 1)
                            {
                                await OrderManager.PlaceLongOrderAsync(symbol, klines.Last().Close, "MAC-D", klines.Last().CloseTime);
                                //LogTradeSignal("LONG", symbol, klines.Last().Close);
                            }
                            else if (divergence == -1)
                            {
                                await OrderManager.PlaceShortOrderAsync(symbol, klines.Last().Close, "MAC-D", klines.Last().CloseTime);
                                //LogTradeSignal("SHORT", symbol, klines.Last().Close);
                            }
                        }
                        else
                        {
                            //Console.WriteLine($"No MACD divergence identified for {symbol}.");
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

        public override async Task RunOnHistoricalDataAsync(IEnumerable<Kline> historicalData)
        {
            var quotes = historicalData.Select(k => new BinanceTestnet.Models.Quote
            {
                Date = DateTimeOffset.FromUnixTimeMilliseconds(k.OpenTime).UtcDateTime,
                Close = k.Close
            }).ToList();

            var macdResults = Indicator.GetMacd(quotes, 12, 26, 9).ToList();

            foreach (var kline in historicalData)
            {
                if(kline.Symbol == null)    continue;
                
                var currentQuotes = quotes.TakeWhile(q => q.Date <= DateTimeOffset.FromUnixTimeMilliseconds(kline.OpenTime).UtcDateTime).ToList();
                var divergence = IdentifyDivergence(macdResults);

                if (divergence != 0)
                {
                    if (divergence == 1)
                    {
                        await OrderManager.PlaceLongOrderAsync(kline.Symbol, kline.Close, "MAC-D", kline.CloseTime);
                        //LogTradeSignal("LONG", kline.Symbol, kline.Close);
                    }
                    else if (divergence == -1)
                    {
                        await OrderManager.PlaceShortOrderAsync(kline.Symbol, kline.Close, "MAC-D", kline.CloseTime);
                        //LogTradeSignal("SHORT", kline.Symbol, kline.Close);
                    }
                }

                // Update MACD results for the next iteration
                macdResults = Indicator.GetMacd(currentQuotes, 25, 125, 9).ToList();
            }
        }

        private List<Kline>? ParseKlines(string content)
        {
            try
            {
                return JsonConvert.DeserializeObject<List<List<object>>>(content)
                    ?.Select(k =>
                    {
                        var kline = new Kline();
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

        private int IdentifyDivergence(List<MacdResult> macdResults)
        {
            if (macdResults.Count < 2)
                return 0;

            var lastMacd = macdResults[macdResults.Count - 1];
            var prevMacd = macdResults[macdResults.Count - 2];

            if (lastMacd.Macd > lastMacd.Signal && prevMacd.Macd < prevMacd.Signal)
            {
                return 1; // Bullish divergence
            }
            else if (lastMacd.Macd < lastMacd.Signal && prevMacd.Macd > prevMacd.Signal)
            {
                return -1; // Bearish divergence
            }

            return 0; // No divergence
        }

        private void LogTradeSignal(string direction, string symbol, decimal price)
        {
            Console.WriteLine($"******MACD Divergence Strategy******************");
            Console.WriteLine($"Go {direction} on {symbol} @ {price} at {DateTime.Now:HH:mm:ss}");
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
