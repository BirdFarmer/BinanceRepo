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
    public class FibonacciRetracementStrategy : StrategyBase
    {
        public FibonacciRetracementStrategy(RestClient client, string apiKey, OrderManager orderManager, Wallet wallet)
        : base(client, apiKey, orderManager, wallet)
        {
        }

        public override async Task RunAsync(string symbol, string interval)
        {
            try
            {
                // Fetch the latest 200 candles for the given symbol and interval
                var request = CreateRequest("/fapi/v1/klines");
                request.AddParameter("symbol", symbol, ParameterType.QueryString);
                request.AddParameter("interval", interval, ParameterType.QueryString);
                request.AddParameter("limit", "200", ParameterType.QueryString);

                var response = await Client.ExecuteGetAsync(request);
                if (response.IsSuccessful && response.Content != null)
                {
                    var klines = ParseKlines(response.Content);

                    if (klines != null && klines.Count > 0)
                    {
                        // Initialize fibHigh and fibLow with values from the first kline
                        decimal fibHigh = klines[0].High;
                        decimal fibLow = klines[0].Low;
                        int fibHighIndex = 0, fibLowIndex = 0;

                        // Find the highest high and lowest low within the lookback period
                        for (int i = 1; i < klines.Count; i++)
                        {
                            var kline = klines[i];

                            if (kline.High > fibHigh)
                            {
                                fibHigh = kline.High;
                                fibHighIndex = i;
                            }
                            if (kline.Low < fibLow)
                            {
                                fibLow = kline.Low;
                                fibLowIndex = i;
                            }
                        }

                        // Determine trade direction
                        bool lookForLongReversals = fibLowIndex > fibHighIndex;
                        bool lookForShortReversals = fibHighIndex > fibLowIndex;

                        // Calculate Stochastic Oscillator values
                        var stochasticResults = CalculateStochastic(klines, 14, 3, 3);

                        if (fibLow < fibHigh)
                        {
                            var fibLevels = GetFibonacciLevels(fibHigh, fibLow);

                            var currentKline = klines.Last();
                            var prevKline = klines[^2];

                            decimal lastPrice = currentKline.Close;
                            decimal candleLow = currentKline.Low;
                            decimal candleHigh = currentKline.High;
                            decimal prevLow = prevKline.Low;
                            decimal prevHigh = prevKline.High;

                            var currentStoch = stochasticResults.Last();
                            var prevStoch = stochasticResults[^2];

                            // Long Entry Condition
                            if (lookForLongReversals &&
                                candleLow <= fibLevels[61.8m] &&
                                prevLow > fibLevels[61.8m] &&
                                currentStoch.K < 10)
                            {
                                await OrderManager.PlaceLongOrderAsync(symbol, lastPrice, "Fibonacci-Stochastic", currentKline.CloseTime);
                                LogTradeSignal("LONG", symbol, lastPrice, fibHigh, fibLow);
                            }
                            // Short Entry Condition
                            else if (lookForShortReversals &&
                                    candleHigh >= fibLevels[38.2m] &&
                                    prevHigh < fibLevels[38.2m] &&
                                    currentStoch.K > 90)
                            {
                                await OrderManager.PlaceShortOrderAsync(symbol, lastPrice, "Fibonacci-Stochastic", currentKline.CloseTime);
                                LogTradeSignal("SHORT", symbol, lastPrice, fibHigh, fibLow);
                            }

                            // Check for trade closures
                            var currentPrices = new Dictionary<string, decimal> { { symbol, lastPrice } };
                            await OrderManager.CheckAndCloseTrades(currentPrices, currentKline.CloseTime);
                        }
                        else
                        {
                            Console.WriteLine($"No valid high/low levels found for {symbol} within the lookback period.");
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


        private List<StochasticResult> CalculateStochastic(List<Kline> klines, int kPeriod, int kSmoothing, int dSmoothing)
        {
            var stochasticResults = new List<StochasticResult>();
            var kValues = new List<decimal>();

            for (int i = 0; i < klines.Count; i++)
            {
                // Only calculate %K if we have enough data points for the kPeriod
                if (i >= kPeriod - 1)
                {
                    // Calculate the highest high and lowest low over the last kPeriod candles
                    decimal highestHigh = klines.Skip(i - kPeriod + 1).Take(kPeriod).Max(k => k.High);
                    decimal lowestLow = klines.Skip(i - kPeriod + 1).Take(kPeriod).Min(k => k.Low);

                    // Calculate the raw %K
                    decimal rawK = 100 * (klines[i].Close - lowestLow) / (highestHigh - lowestLow);
                    kValues.Add(rawK);
                }
                else
                {
                    kValues.Add(0);  // Placeholder until %K calculation is possible
                }

                // Smooth %K values with a moving average of kSmoothing period
                decimal smoothedK = 0;
                if (i >= kPeriod + kSmoothing - 2)
                {
                    smoothedK = kValues.Skip(i - kSmoothing + 1).Take(kSmoothing).Average();
                }

                // Calculate the %D (smoothed %K) using dSmoothing period
                decimal smoothedD = 0;
                if (i >= kPeriod + kSmoothing + dSmoothing - 3)
                {
                    smoothedD = kValues.Skip(i - kSmoothing - dSmoothing + 2).Take(dSmoothing).Average();
                }

                // Add the result for the current candle to the list
                stochasticResults.Add(new StochasticResult
                {
                    K = smoothedK,
                    D = smoothedD
                });
            }

            return stochasticResults;
        }

        public class StochasticResult
        {
            public decimal K { get; set; }  // Smoothed %K
            public decimal D { get; set; }  // Smoothed %D
        }

        public override async Task RunOnHistoricalDataAsync(IEnumerable<Kline> historicalData)
        {
            try
            {
                if (historicalData == null || !historicalData.Any())
                {
                    Console.WriteLine("No historical data provided.");
                    return;
                }

                var recentData = historicalData.Skip(Math.Max(0, historicalData.Count() - 300)).ToList();

                decimal fibHigh = recentData[0].High;
                decimal fibLow = recentData[0].Low;
                int fibHighIndex = 0, fibLowIndex = 0;

                for (int i = 1; i < recentData.Count; i++)
                {
                    var kline = recentData[i];
                    if (kline.High > fibHigh)
                    {
                        fibHigh = kline.High;
                        fibHighIndex = i;
                    }
                    if (kline.Low < fibLow)
                    {
                        fibLow = kline.Low;
                        fibLowIndex = i;
                    }
                }

                bool lookForLongReversals = fibLowIndex > fibHighIndex;
                bool lookForShortReversals = fibHighIndex > fibLowIndex;

                var stochasticResults = CalculateStochastic(recentData, 14, 3, 3);

                if (fibHigh > fibLow)
                {
                    var fibLevels = GetFibonacciLevels(fibHigh, fibLow);

                    for (int i = 1; i < recentData.Count; i++)
                    {
                        var currentKline = recentData[i];
                        var prevKline = recentData[i - 1];

                        decimal lastPrice = currentKline.Close;
                        decimal candleLow = currentKline.Low;
                        decimal candleHigh = currentKline.High;
                        decimal prevLow = prevKline.Low;
                        decimal prevHigh = prevKline.High;
                        string symbol = currentKline.Symbol;

                        var currentStoch = stochasticResults[i];
                        var prevStoch = stochasticResults[i - 1];

                        // Long Entry Condition
                        if (lookForLongReversals &&
                            candleLow <= fibLevels[61.8m] &&
                            prevLow > fibLevels[61.8m] &&
                            currentStoch.K < 10)
                        {
                            await OrderManager.PlaceLongOrderAsync(symbol, lastPrice, "Fibonacci-Stochastic", currentKline.CloseTime);
                            LogTradeSignal("LONG", symbol, lastPrice, fibHigh, fibLow);
                        }
                        // Short Entry Condition
                        else if (lookForShortReversals &&
                                candleHigh >= fibLevels[38.2m] &&
                                prevHigh < fibLevels[38.2m] &&
                                currentStoch.K > 90)
                        {
                            await OrderManager.PlaceShortOrderAsync(symbol, lastPrice, "Fibonacci-Stochastic", currentKline.CloseTime);
                            LogTradeSignal("SHORT", symbol, lastPrice, fibHigh, fibLow);
                        }
                    }
                }
                else
                {
                    Console.WriteLine($"Invalid Fibonacci levels for {historicalData.First().Symbol}: High is not greater than Low.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during historical data backtest: {ex.Message}");
            }
        }


        
        private List<decimal> CalculateRsi(List<BinanceTestnet.Models.Quote> quotes, int period)
        {
            List<decimal> rsiValues = new List<decimal>();
            decimal gain = 0, loss = 0;

            for (int i = 1; i < quotes.Count; i++)
            {
                decimal change = quotes[i].Close - quotes[i - 1].Close;
                if (i <= period)
                {
                    gain += change > 0 ? change : 0;
                    loss += change < 0 ? Math.Abs(change) : 0;
                    if (i == period)
                    {
                        gain /= period;
                        loss /= period;
                        rsiValues.Add(100 - (100 / (1 + (gain / loss))));
                    }
                }
                else
                {
                    gain = ((gain * (period - 1)) + (change > 0 ? change : 0)) / period;
                    loss = ((loss * (period - 1)) + (change < 0 ? Math.Abs(change) : 0)) / period;
                    rsiValues.Add(100 - (100 / (1 + (gain / loss))));
                }
            }
            return rsiValues;
        }

        private RestRequest CreateRequest(string resource)
        {
            var request = new RestRequest(resource, Method.Get);
            request.AddHeader("Content-Type", "application/json");
            request.AddHeader("Accept", "application/json");

            return request;
        }

        private void LogTradeSignal(string direction, string symbol, decimal price, decimal? fibHigh, decimal? fibLow)
        {
            // Log the trade signal to the console or your preferred logging system
            Console.WriteLine($"****** Fibonacci Retracement Strategy ******************");
            Console.WriteLine($"Go {direction} on {symbol} @ {price} at {DateTime.Now:HH:mm:ss}");
            Console.WriteLine($"FibHigh: {fibHigh} FibLow: {fibLow}");
            Console.WriteLine($"************************************************");
        }

        private void HandleErrorResponse(string symbol, RestResponse response)
        {
            // Console.WriteLine($"Error for {symbol}: {response.ErrorMessage}");
            // Console.WriteLine($"Status Code: {response.StatusCode}");
            // Console.WriteLine($"Content: {response.Content}");
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

        public Dictionary<decimal, decimal> GetFibonacciLevels(decimal highestHigh, decimal lowestLow)
        {
            // Calculate the Fibonacci retracement levels from the highest high and lowest low
            var fibLevels = new Dictionary<decimal, decimal>
            {
                { 0m, lowestLow },             // 0% Fibonacci level
                { 23.6m, lowestLow + (highestHigh - lowestLow) * 0.236m }, // 23.6% level
                { 38.2m, lowestLow + (highestHigh - lowestLow) * 0.382m }, // 38.2% level
                { 50m, lowestLow + (highestHigh - lowestLow) * 0.5m },    // 50% level
                { 61.8m, lowestLow + (highestHigh - lowestLow) * 0.618m }, // 61.8% level
                { 78.6m, lowestLow + (highestHigh - lowestLow) * 0.786m }, // 76.4% level
                { 100m, highestHigh }           // 100% Fibonacci level
            };
            return fibLevels;
        }

    }
}
