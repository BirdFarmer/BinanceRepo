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
    public class RsiDivergenceStrategy : StrategyBase
    {
        public RsiDivergenceStrategy(RestClient client, string apiKey, OrderManager orderManager, Wallet wallet)
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
                request.AddParameter("limit", "200", ParameterType.QueryString);

                var response = await Client.ExecuteGetAsync(request);
                if (response.IsSuccessful)
                {
                    var klines = ParseKlines(response.Content);

                    if (klines != null && klines.Count > 0)
                    {
                        var quotes = klines.Select(k => new BinanceTestnet.Models.Quote
                        {
                            Date = DateTimeOffset.FromUnixTimeMilliseconds(k.OpenTime).UtcDateTime,
                            Close = k.Close,
                            High = k.High,
                            Low = k.Low
                        }).ToList();

                        var rsiResults = Indicator.GetRsi(quotes, 20).ToList();
                        var stochasticResults = Indicator.GetStoch(quotes, 100, 3, 3).ToList(); // Use StochResult type consistently

                        if (rsiResults.Count > 2 && stochasticResults.Count > 2)
                        {
                            var lastRsi = rsiResults.Last();
                            var lastStochastic = stochasticResults.Last();

                            // Check for bullish RSI divergence and Stochastic <= 10 for LONG
                            if (IsBullishDivergence(klines, rsiResults, stochasticResults) && lastStochastic.K <= 10)
                            {
                                Console.WriteLine($"Bullish RSI divergence and Stochastic is below 10. Going LONG");
                                await OrderManager.PlaceLongOrderAsync(symbol, klines.Last().Close, "RSI Divergence", klines.Last().CloseTime);
                                LogTradeSignal("LONG", symbol, klines.Last().Close);
                            }

                            // Check for bearish RSI divergence and Stochastic >= 90 for SHORT
                            else if (IsBearishDivergence(klines, rsiResults, stochasticResults) && lastStochastic.K >= 90)
                            {
                                Console.WriteLine($"Bearish RSI divergence and Stochastic is above 90. Going SHORT");
                                await OrderManager.PlaceShortOrderAsync(symbol, klines.Last().Close, "RSI Divergence", klines.Last().CloseTime);
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

        private bool IsBearishDivergence(List<Kline> klines, List<RsiResult> rsiResults, List<StochResult> stochResults, int lookback = 170)
        {
            if (klines.Count < lookback + 1 || rsiResults.Count < lookback + 1) return false;

            var latestKline = klines.Last();
            var latestRsi = rsiResults.Last().Rsi;

            // Check if current RSI is above 65 (potential bearish setup)
            if (latestRsi < 50) return false;

            // Look back 100 candles to find the highest price top and corresponding RSI
            decimal highestPrice = decimal.MinValue;
            decimal highestRsi = decimal.MinValue;

            for (int i = klines.Count - 21; i >= klines.Count - 1 - lookback; i--)
            {
                if (rsiResults[i].Rsi == null) continue;
                
                // Update the highest RSI and high price in the period
                if ((decimal)rsiResults[i].Rsi > highestRsi || highestRsi == null)
                {
                    highestRsi = (decimal)rsiResults[i].Rsi;
                    highestPrice = klines[i].High;
                }
            }
            
            if(highestRsi == decimal.MinValue) return false; 

            // Check if current price is higher but RSI is lower
            //if (latestKline.High > highestPrice && (decimal)latestRsi < highestRsi && highestRsi >= 80)
            if (highestRsi >= 80 
                && (decimal)latestRsi < highestRsi 
                && klines[klines.Count - 1].High > highestPrice 
                && klines[klines.Count - 1].Close < klines[klines.Count - 1].Open)
            {
                return true;             
            }

            return false;
        }

        public bool IsBullishDivergence(List<Kline> klines, List<RsiResult> rsiResults, List<StochResult> stochasticResults, int lookback = 170)
        {
            if (klines.Count < lookback + 1 || rsiResults.Count < lookback + 1) return false;

            var latestKline = klines.Last();
            var latestRsi = rsiResults.Last().Rsi;

            // Check if current RSI is below 35 (potential bullish setup)
            if (latestRsi > 50) return false;

            // Look back 100 candles to find the lowest price bottom and corresponding RSI
            decimal lowestPrice = decimal.MaxValue;
            decimal lowestRsi = decimal.MaxValue;
                 
            // Find the lowest RSI and corresponding low price in the lookback period
            for (int i = klines.Count - 21; i >= klines.Count - 1 - lookback; i--)
            {
                if (rsiResults[i].Rsi == null) continue;
                
                // Update the lowest RSI and low price in the period
                if ((decimal)rsiResults[i].Rsi < lowestRsi || lowestRsi == null)
                {
                    lowestRsi = (decimal)rsiResults[i].Rsi;
                    lowestPrice = klines[i].Low;
                }
            }

            if(lowestRsi == decimal.MinValue) return false;

            // Check if current price is lower but RSI is higher
            // if (latestKline.Low < lowestPrice && (decimal)latestRsi > lowestRsi && lowestRsi <= 20)
            if (lowestRsi <= 20     
                && (decimal)latestRsi > lowestRsi 
                && klines[klines.Count - 1].Low < lowestPrice
                && klines[klines.Count - 1].Close > klines[klines.Count - 1].Open)
            {
                return true;             
            }

            return false;
        }

        public override async Task RunOnHistoricalDataAsync(IEnumerable<Kline> historicalData)
        {
            // Convert historical data to a list of quotes
            var klines = historicalData.ToList();
            var quotes = klines.Select(k => new BinanceTestnet.Models.Quote
            {
                Date = DateTimeOffset.FromUnixTimeMilliseconds(k.OpenTime).UtcDateTime,
                Close = k.Close,
                High = k.High,
                Low = k.Low
            }).ToList();

            // Calculate RSI and Stochastic indicators
            var rsiResults = Indicator.GetRsi(quotes, 20).ToList();
            var stochasticResults = Indicator.GetStoch(quotes, 100, 3, 3).ToList();

            for (int i = 100; i < rsiResults.Count; i++)
            {
                var kline = klines[i];
                var rsiValue = rsiResults[i].Rsi;
                var stochasticK = stochasticResults[i].K;

                if (rsiValue.HasValue && stochasticK.HasValue)
                {
                    // Only pass data up to the current index 'i' for divergence detection
                    var klinesSubset = klines.Take(i + 1).ToList();
                    var rsiSubset = rsiResults.Take(i + 1).ToList();
                    var stochasticSubset = stochasticResults.Take(i + 1).ToList();

                    // Check for Bullish Divergence and place Long order
                    if (IsBullishDivergence(klinesSubset, rsiSubset, stochasticSubset) && stochasticK <= 10)
                    {
                        await OrderManager.PlaceLongOrderAsync(kline.Symbol, kline.Close, "RSI Bullish Divergence", kline.CloseTime);
                        LogTradeSignal("LONG", kline.Symbol, kline.Close);
                    }

                    // Check for Bearish Divergence and place Short order
                    else if (IsBearishDivergence(klinesSubset, rsiSubset, stochasticSubset) && stochasticK >= 90)
                    {
                        await OrderManager.PlaceShortOrderAsync(kline.Symbol, kline.Close, "RSI Bearish Divergence", kline.CloseTime);
                        LogTradeSignal("SHORT", kline.Symbol, kline.Close);
                    }
                }

                // Check and close active trades after placing new orders
                var currentPrices = new Dictionary<string, decimal> { { kline.Symbol, kline.Close } };
                await OrderManager.CheckAndCloseTrades(currentPrices);
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

        private void LogTradeSignal(string direction, string symbol, decimal price)
        {
            Console.WriteLine($"****** RSI Divergence Strategy ******************");
            Console.WriteLine($"Go {direction} on {symbol} @ {price} at {DateTime.Now:HH:mm:ss}");
            Console.WriteLine($"************************************************");
        }

        private void HandleErrorResponse(string symbol, RestResponse response)
        {
            Console.WriteLine($"Error for {symbol}: {response.ErrorMessage}");
            Console.WriteLine($"Status code: {response.StatusCode}");
            if (!string.IsNullOrEmpty(response.Content))
                Console.WriteLine($"Response content: {response.Content}");
        }
    }
}
