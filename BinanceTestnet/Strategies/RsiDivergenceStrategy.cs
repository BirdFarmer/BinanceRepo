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
        // Internal entry tuning (not exposed as strategy parameters)
        private const decimal SmallValue = 0.000001m;
        private const int EntryLookaheadBarsDefault = 6;
        private const int VolumeLookback = 20;
        private const decimal VolumeMultiplier = 1.0m;
        private const decimal MinBreakPct = 0.001m; // 0.1%
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
                            var (signalKline, previousKline) = SelectSignalPair(workingKlines);
                            if (signalKline == null || previousKline == null) return;

                            // Evaluate divergence on the same candle set used for indicators (workingKlines)
                            if (IsBullishDivergence(workingKlines, rsiResults, stochasticResults))
                            {
                                // Bullish divergence detected. In live runs we only have the current candles,
                                // so require the most recent (signal) candle to be a qualifying "turn" candle.
                                var latest = signalKline;
                                var prev = previousKline;
                                if (latest != null && prev != null && IsGoodTurnCandle(latest, prev, workingKlines, rsiResults))
                                {
                                    Console.WriteLine("Bullish RSI divergence + turn candle detected. Going LONG");
                                    await OrderManager.PlaceLongOrderAsync(symbol, latest.Close, "RSI Divergence", latest.CloseTime);
                                    Helpers.StrategyUtils.TraceSignalCandle("RSIDivergence", symbol, UseClosedCandles, latest, prev, "Bullish divergence + turn candle");
                                    LogTradeSignal("LONG", symbol, latest.Close);
                                }
                                else
                                {
                                    Console.WriteLine("Bullish divergence detected but no qualifying turn candle yet.");
                                }
                            }

                            // Check for bearish RSI divergence and Stochastic >= 90 for SHORT
                            else if (IsBearishDivergence(workingKlines, rsiResults, stochasticResults))
                            {
                                // Bearish divergence detected. Require the latest signal candle to be a qualifying bearish turn.
                                var latest = signalKline;
                                var prev = previousKline;
                                if (latest != null && prev != null && IsGoodTurnCandle(latest, prev, workingKlines, rsiResults, isBullish: false))
                                {
                                    Console.WriteLine("Bearish RSI divergence + turn candle detected. Going SHORT");
                                    await OrderManager.PlaceShortOrderAsync(symbol, latest.Close, "RSI Divergence", latest.CloseTime);
                                    Helpers.StrategyUtils.TraceSignalCandle("RSIDivergence", symbol, UseClosedCandles, latest, prev, "Bearish divergence + turn candle");
                                    LogTradeSignal("SHORT", symbol, latest.Close);
                                }
                                else
                                {
                                    Console.WriteLine("Bearish divergence detected but no qualifying turn candle yet.");
                                }
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

                    // Check for Bullish Divergence and search ahead for a qualifying turn candle to place Long order
                    if (IsBullishDivergence(klinesSubset, rsiSubset, stochasticSubset))
                    {
                        // pivot is at current index i; search the next few bars for a turn candle
                        int pivotIndex = i;
                        int maxLook = Math.Min(klines.Count - 1, pivotIndex + EntryLookaheadBarsDefault);

                        // compute average volume baseline
                        decimal avgVolume = 0m;
                        int volStart = Math.Max(0, pivotIndex - VolumeLookback);
                        int volCount = pivotIndex - volStart + 1;
                        if (volCount > 0)
                        {
                            for (int v = volStart; v <= pivotIndex; v++) avgVolume += klines[v].Volume;
                            avgVolume = avgVolume / Math.Max(1, volCount);
                        }

                        for (int j = pivotIndex + 1; j <= maxLook; j++)
                        {
                            var candidate = klines[j];
                            var prevBar = klines[j - 1];
                            if (IsGoodTurnCandle(candidate, prevBar, klines, rsiResults, true, avgVolume * VolumeMultiplier))
                            {
                                if (!string.IsNullOrEmpty(candidate.Symbol))
                                {
                                    await OrderManager.PlaceLongOrderAsync(candidate.Symbol, candidate.Close, "RSI Divergence", candidate.CloseTime);
                                    LogTradeSignal("LONG", candidate.Symbol!, candidate.Close);
                                }
                                break;
                            }
                        }
                        // if not placed, skip this divergence (no qualifying turn found within lookahead)
                    }

                    // Check for Bearish Divergence and search ahead for a qualifying bearish turn candle
                    else if (IsBearishDivergence(klinesSubset, rsiSubset, stochasticSubset))
                    {
                        int pivotIndex = i;
                        int maxLook = Math.Min(klines.Count - 1, pivotIndex + EntryLookaheadBarsDefault);

                        // compute average volume baseline
                        decimal avgVolume = 0m;
                        int volStart = Math.Max(0, pivotIndex - VolumeLookback);
                        int volCount = pivotIndex - volStart + 1;
                        if (volCount > 0)
                        {
                            for (int v = volStart; v <= pivotIndex; v++) avgVolume += klines[v].Volume;
                            avgVolume = avgVolume / Math.Max(1, volCount);
                        }

                        for (int j = pivotIndex + 1; j <= maxLook; j++)
                        {
                            var candidate = klines[j];
                            var prevBar = klines[j - 1];
                            if (IsGoodTurnCandle(candidate, prevBar, klines, rsiResults, false, avgVolume * VolumeMultiplier))
                            {
                                if (!string.IsNullOrEmpty(candidate.Symbol))
                                {
                                    await OrderManager.PlaceShortOrderAsync(candidate.Symbol, candidate.Close, "RSI Divergence", candidate.CloseTime);
                                    LogTradeSignal("SHORT", candidate.Symbol!, candidate.Close);
                                }
                                break;
                            }
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

        // Determine whether a candidate bar is a good turn candle for entry.
        // If isBullish=true, require bullish turn (lower wick >= body OR small breakout above prev high).
        // If isBullish=false, require bearish turn (upper wick >= body OR small breakdown below prev low).
        // Optional volume baseline (if > 0) is applied symmetrically.
        private bool IsGoodTurnCandle(Kline bar, Kline prevBar, List<Kline> klines, List<RsiResult> rsiResults, bool isBullish = true, decimal volumeBaseline = 0m)
        {
            // require directional close
            if (isBullish)
            {
                if (bar.Close <= bar.Open) return false;
            }
            else
            {
                if (bar.Close >= bar.Open) return false;
            }

            decimal body = Math.Abs(bar.Close - bar.Open);
            decimal denom = Math.Max(body, SmallValue);

            // Lower wick for bullish, upper wick for bearish
            decimal lowerWick = Math.Min(bar.Open, bar.Close) - bar.Low;
            decimal upperWick = bar.High - Math.Max(bar.Open, bar.Close);

            bool wickOk = isBullish
                ? (lowerWick / denom) >= 1.0m    // lower wick >= body
                : (upperWick / denom) >= 1.0m;   // upper wick >= body

            // small-breakout/breakdown acceptance
            bool breakoutOk = isBullish
                ? bar.Close >= prevBar.High * (1 + MinBreakPct)
                : bar.Close <= prevBar.Low * (1 - MinBreakPct);

            // volume check if baseline provided
            bool volOk = true;
            if (volumeBaseline > 0m)
            {
                volOk = bar.Volume >= volumeBaseline;
            }

            return volOk && (wickOk || breakoutOk);
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
