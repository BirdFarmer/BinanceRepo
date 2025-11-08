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
    public class MACDStandardStrategy : StrategyBase
    {
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
                        var quotes = Helpers.StrategyUtils.ToQuotes(klines, includeOpen:false, includeVolume:false)
                            .Select(q => new BinanceTestnet.Models.Quote { Date = q.Date, Close = q.Close })
                            .ToList();

                        var macdResults = Indicator.GetMacd(quotes, 12, 26, 9).ToList();

                        if (macdResults.Count > 1)
                        {
                            var lastMacd = macdResults[macdResults.Count - 1];
                            var prevMacd = macdResults[macdResults.Count - 2];

                            if (lastMacd.Macd > lastMacd.Signal && prevMacd.Macd <= prevMacd.Signal)
                            {
                                await OrderManager.PlaceLongOrderAsync(symbol, klines.Last().Close, "MAC-D", klines.Last().OpenTime);
                                LogTradeSignal("LONG", symbol, klines.Last().Close);
                            }
                            else if (lastMacd.Macd < lastMacd.Signal && prevMacd.Macd >= prevMacd.Signal)
                            {
                                await OrderManager.PlaceShortOrderAsync(symbol, klines.Last().Close, "MAC-D", klines.Last().OpenTime);
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

        public override async Task RunOnHistoricalDataAsync(IEnumerable<BinanceTestnet.Models.Kline> historicalData)
        {
            var quotes = historicalData.Select(k => new BinanceTestnet.Models.Quote
            {
                Date = DateTimeOffset.FromUnixTimeMilliseconds(k.OpenTime).UtcDateTime,
                Close = k.Close
            }).ToList();

            var macdResults = Indicator.GetMacd(quotes, 12, 26, 9).ToList();

            for (int i = 1; i < macdResults.Count; i++)
            {
                var lastMacd = macdResults[i];
                var prevMacd = macdResults[i - 1];
                var kline = historicalData.ElementAt(i);

                if(kline.Symbol == null) continue;

                if (lastMacd.Macd > lastMacd.Signal && prevMacd.Macd <= prevMacd.Signal)
                {
                    await OrderManager.PlaceLongOrderAsync(kline.Symbol, kline.Close, "MAC-D", kline.CloseTime);
                    LogTradeSignal("LONG", kline.Symbol, kline.Close);
                }
                else if (lastMacd.Macd < lastMacd.Signal && prevMacd.Macd >= prevMacd.Signal)
                {
                    await OrderManager.PlaceShortOrderAsync(kline.Symbol, kline.Close, "MAC-D", kline.CloseTime);
                    LogTradeSignal("SHORT", kline.Symbol, kline.Close);
                }
                
                var currentPrices = new Dictionary<string, decimal> { { kline.Symbol, kline.Close } };
                await OrderManager.CheckAndCloseTrades(currentPrices, kline.CloseTime);
            }
        }
    }
}
