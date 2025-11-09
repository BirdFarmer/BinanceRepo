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
                var request = Helpers.StrategyUtils.CreateGet("/fapi/v1/klines", new Dictionary<string,string>
                {
                    {"symbol", symbol},
                    {"interval", interval},
                    {"limit", "200"}
                });

                var response = await Client.ExecuteGetAsync(request);
                if (response.IsSuccessful && response.Content != null)
                {
                    var klines = Helpers.StrategyUtils.ParseKlines(response.Content);

                    if (klines != null && klines.Count > 0)
                    {
                        // Respect closed-candle policy for calculations
                        var workingKlines = UseClosedCandles ? Helpers.StrategyUtils.ExcludeForming(klines) : klines;
                        var quotes = workingKlines.Select(k => new BinanceTestnet.Models.Quote
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
                            // If using closed candles, drop the forming indicator value
                            var lastRsi = rsiResults.Last();
                            var lastStochastic = stochasticResults.Last();

                            // Check for bullish RSI divergence and Stochastic <= 10 for LONG
                            var (signalKline, previousKline) = SelectSignalPair(klines);
                            if (signalKline == null || previousKline == null) return;

                            // Evaluate divergence on the same candle set used for indicators (workingKlines)
                            if (IsBullishDivergence(workingKlines, rsiResults, stochasticResults) && lastStochastic.K <= 10)
                            {
                                Console.WriteLine($"Bullish RSI divergence and Stochastic is below 10. Going LONG");
                                await OrderManager.PlaceLongOrderAsync(symbol, signalKline.Close, "RSI Divergence", signalKline.OpenTime);
                                Helpers.StrategyUtils.TraceSignalCandle("RSIDivergence", symbol, UseClosedCandles, signalKline, previousKline, "Bullish divergence + Stoch<=10");
                                LogTradeSignal("LONG", symbol, signalKline.Close);
                            }

                            // Check for bearish RSI divergence and Stochastic >= 90 for SHORT
                            else if (IsBearishDivergence(workingKlines, rsiResults, stochasticResults) && lastStochastic.K >= 90)
                            {
                                Console.WriteLine($"Bearish RSI divergence and Stochastic is above 90. Going SHORT");
                                await OrderManager.PlaceShortOrderAsync(symbol, signalKline.Close, "RSI Divergence", signalKline.OpenTime);
                                Helpers.StrategyUtils.TraceSignalCandle("RSIDivergence", symbol, UseClosedCandles, signalKline, previousKline, "Bearish divergence + Stoch>=90");
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

        private bool IsBearishDivergence(List<Kline> klines, List<RsiResult> rsiResults, List<StochResult> stochResults, int lookback = 170)
        {
            if (klines.Count < lookback + 1 || rsiResults.Count < lookback + 1) return false;

            var latestKline = klines.Last();
            var latestRsi = rsiResults.Last().Rsi;

            // Check if current RSI is above 65 (potential bearish setup)
            if (latestRsi < 50) return false;

            // Look back 100 candles to find the highest price top and corresponding RSI
            decimal highestPrice = decimal.MinValue;
            double highestRsi = double.MinValue;

            for (int i = klines.Count - 21; i >= klines.Count - 1 - lookback; i--)
            {
                if (rsiResults[i].Rsi == null) continue;
                
                // Update the highest RSI and high price in the period
                var rsiVal = rsiResults[i].Rsi;
                if (rsiVal.HasValue && rsiVal.Value > highestRsi)
                {
                    highestRsi = rsiVal.Value;
                    highestPrice = klines[i].High;
                }
            }
            
            if(highestRsi == double.MinValue) return false; 

            // Check if current price is higher but RSI is lower
            //if (latestKline.High > highestPrice && (decimal)latestRsi < highestRsi && highestRsi >= 80)
            if (highestRsi >= 80 
                && latestRsi.HasValue && latestRsi.Value < highestRsi 
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
            double lowestRsi = double.MaxValue;
                 
            // Find the lowest RSI and corresponding low price in the lookback period
            for (int i = klines.Count - 21; i >= klines.Count - 1 - lookback; i--)
            {
                if (rsiResults[i].Rsi == null) continue;
                
                // Update the lowest RSI and low price in the period
                var rsiVal = rsiResults[i].Rsi;
                if (rsiVal.HasValue && rsiVal.Value < lowestRsi)
                {
                    lowestRsi = rsiVal.Value;
                    lowestPrice = klines[i].Low;
                }
            }

            if(lowestRsi == double.MaxValue) return false;

            // Check if current price is lower but RSI is higher
            // if (latestKline.Low < lowestPrice && (decimal)latestRsi > lowestRsi && lowestRsi <= 20)
            if (lowestRsi <= 20     
                && latestRsi.HasValue && latestRsi.Value > lowestRsi 
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
                        if (!string.IsNullOrEmpty(kline.Symbol)) {
                            await OrderManager.PlaceLongOrderAsync(kline.Symbol, kline.Close, "RSI Divergence", kline.CloseTime);
                            LogTradeSignal("LONG", kline.Symbol!, kline.Close);
                        }
                    }

                    // Check for Bearish Divergence and place Short order
                    else if (IsBearishDivergence(klinesSubset, rsiSubset, stochasticSubset) && stochasticK >= 90)
                    {
                        if (!string.IsNullOrEmpty(kline.Symbol)) {
                            await OrderManager.PlaceShortOrderAsync(kline.Symbol, kline.Close, "RSI Divergence", kline.CloseTime);
                            LogTradeSignal("SHORT", kline.Symbol!, kline.Close);
                        }
                    }
                }

                // Check and close active trades after placing new orders
                if (!string.IsNullOrEmpty(kline.Symbol)) {
                    var currentPrices = new Dictionary<string, decimal> { { kline.Symbol, kline.Close } };
                    await OrderManager.CheckAndCloseTrades(currentPrices, kline.CloseTime);
                }
            }
        }



        // Parsing and request creation centralized in StrategyUtils

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
