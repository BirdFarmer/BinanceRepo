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
    public class EmaStochRsiStrategy : StrategyBase
    {
        protected override bool SupportsClosedCandles => true;
        public EmaStochRsiStrategy(RestClient client, string apiKey, OrderManager orderManager, Wallet wallet) 
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
                    {"limit", "400"}
                });

                decimal lastPrice = 0;

                var response = await Client.ExecuteGetAsync(request);
                if (response.IsSuccessful && response.Content != null)
                {
                    var klines = Helpers.StrategyUtils.ParseKlines(response.Content);

                    if (klines != null && klines.Count > 0)
                    {
                        var (signalKline, previousKline) = SelectSignalPair(klines);
                        if (signalKline == null || previousKline == null) return;
                        var lastKline = signalKline;
                        var quotes = ToIndicatorQuotes(klines);

                        var ema8 = Indicator.GetEma(quotes, 8).ToList();
                        var ema14 = Indicator.GetEma(quotes, 14).ToList();
                        var ema50 = Indicator.GetEma(quotes, 50).ToList();
                        var sma375 = Indicator.GetSma(quotes, 375).ToList();
                        var stochRsi = Indicator.GetStochRsi(quotes, 14, 3, 3).ToList();

                        if (ema8.Count > 0 && ema14.Count > 0 && ema50.Count > 0 && stochRsi.Count > 1)
                        {
                            var lastEma8 = ema8.Last();
                            var lastEma14 = ema14.Last();
                            var lastEma50 = ema50.Last();
                            var lastStochRsi = stochRsi.Last();
                            var prevStochRsi = stochRsi[stochRsi.Count - 2];
                            lastPrice = lastKline.Close;

                            // Guard for indicator values that may be undefined early in the series
                            if (lastEma8.Ema != null && lastEma14.Ema != null && lastEma50.Ema != null &&
                                lastStochRsi.StochRsi != null && lastStochRsi.Signal != null &&
                                prevStochRsi.StochRsi != null && prevStochRsi.Signal != null)
                            {
                                // Long Signal
                                if ((double)lastKline.Low > lastEma8.Ema &&
                                    lastEma8.Ema > lastEma14.Ema &&
                                    lastEma14.Ema > lastEma50.Ema &&
                                    lastStochRsi.StochRsi > lastStochRsi.Signal &&
                                    prevStochRsi.StochRsi <= prevStochRsi.Signal)
                                {
                                    await OrderManager.PlaceLongOrderAsync(symbol, lastKline.Close, "EMA-StochRSI", lastKline.OpenTime);
                                    LogTradeSignal("LONG", symbol, lastKline.Close);
                                }
                                // Short Signal
                                else if ((double)lastKline.High < lastEma8.Ema &&
                                         lastEma8.Ema < lastEma14.Ema &&
                                         lastEma14.Ema < lastEma50.Ema &&
                                         lastStochRsi.StochRsi < lastStochRsi.Signal &&
                                         prevStochRsi.StochRsi >= prevStochRsi.Signal)
                                {
                                    await OrderManager.PlaceShortOrderAsync(symbol, lastKline.Close, "EMA-StochRSI", lastKline.OpenTime);
                                    LogTradeSignal("SHORT", symbol, lastKline.Close);
                                }
                            }
                        }                        

                        if(lastPrice > 0)
                        {
                            var currentPrices = new Dictionary<string, decimal> { { symbol, lastPrice} };
                            await OrderManager.CheckAndCloseTrades(currentPrices);
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

            var ema8 = Indicator.GetEma(quotes, 8).ToList();
            var ema14 = Indicator.GetEma(quotes, 14).ToList();
            var ema50 = Indicator.GetEma(quotes, 50).ToList();
            var stochRsi = Indicator.GetStochRsi(quotes, 14, 3, 3).ToList();

            for (int i = 1; i < ema8.Count; i++)
            {
                var currentKline = historicalData.ElementAt(i);
                var currentEma8 = ema8[i];
                var currentEma14 = ema14[i];
                var currentEma50 = ema50[i];
                var currentStochRsi = stochRsi[i];
                var prevStochRsi = stochRsi[i - 1];

                if (currentKline != null && currentEma8 != null && currentEma14 != null 
                    && currentEma50 != null && currentStochRsi != null && currentKline.Symbol != null)
                {
                    // Long Signal
                    if ((double)currentKline.Close > currentEma8.Ema &&
                        currentEma8.Ema > currentEma14.Ema &&
                        currentEma14.Ema > currentEma50.Ema &&
                        currentStochRsi.StochRsi > currentStochRsi.Signal &&
                        prevStochRsi.StochRsi <= prevStochRsi.Signal)
                    {
                        await OrderManager.PlaceLongOrderAsync(currentKline.Symbol, currentKline.Close, "EMA-StochRSI", currentKline.OpenTime);
                        LogTradeSignal("LONG", currentKline.Symbol, currentKline.Close);
                    }
                    // Short Signal
                    else if ((double)currentKline.Close < currentEma8.Ema &&
                             currentEma8.Ema < currentEma14.Ema &&
                             currentEma14.Ema < currentEma50.Ema &&
                             currentStochRsi.StochRsi < currentStochRsi.Signal &&
                             prevStochRsi.StochRsi >= prevStochRsi.Signal)
                    {
                        await OrderManager.PlaceShortOrderAsync(currentKline.Symbol, currentKline.Close, "EMA-StochRSI", currentKline.OpenTime);
                        LogTradeSignal("SHORT", currentKline.Symbol, currentKline.Close);
                    }
                }   
                else continue;   
                
                var currentPrices = new Dictionary<string, decimal> { { currentKline.Symbol, currentKline.Close } };
                await OrderManager.CheckAndCloseTrades(currentPrices, currentKline.OpenTime);
            }
        }


        private void LogTradeSignal(string direction, string symbol, decimal price)
        {
            Console.WriteLine($"****** EMA-StochRSI Strategy ******************");
            Console.WriteLine($"Go {direction} on {symbol} @ {price} at {DateTime.Now:HH:mm:ss}");
            if(direction == "LONG")
            {
                Console.WriteLine("Long entry condition: ");
                Console.WriteLine("Price is above EMA8, which is above EMA14, which is above EMA50.");
                Console.WriteLine("Stochastic RSI crosses above its signal line from below.");

            }
            else
            {
                Console.WriteLine("Short entry condition: ");
                Console.WriteLine("Price is below EMA8, which is below EMA14, which is below EMA50.");
                Console.WriteLine("Stochastic RSI crosses below its signal line from above.");
            }
            Console.WriteLine($"************************************************");
        }

        private void HandleErrorResponse(string symbol, RestResponse response)
        {
            Console.WriteLine($"Error for {symbol}: {response.ErrorMessage}");
            Console.WriteLine($"Status Code: {response.StatusCode}");
            Console.WriteLine($"Content: {response.Content}");
        }

        // Request/parse centralized in StrategyUtils        
    }
}
