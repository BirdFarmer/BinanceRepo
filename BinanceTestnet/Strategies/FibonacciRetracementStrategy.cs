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
     public class FibonacciRetracementStrategy : StrategyBase
    {
        private int _trendBars;
        private bool _isUptrend;
        private bool _isDowntrend;
        
        // Using your PineScript parameters
        private const int LookbackPeriod = 100;
        private const int EmaPeriod = 50;
        private const int RsiLength = 14;
        private const decimal RsiUpper = 65m;
        private const decimal RsiLower = 35m;
        private const int MinTrendBars = 5;

        public FibonacciRetracementStrategy(RestClient client, string apiKey, OrderManager orderManager, Wallet wallet)
            : base(client, apiKey, orderManager, wallet)
        {
        }

        public override async Task RunAsync(string symbol, string interval)
        {
            try
            {
                // Fetch enough candles for indicators
                var request = Helpers.StrategyUtils.CreateGet("/fapi/v1/klines", new Dictionary<string,string>
                {
                    {"symbol", symbol},
                    {"interval", interval},
                    {"limit", "150"}
                });
                var response = await Client.ExecuteGetAsync(request);
                if (!response.IsSuccessful || response.Content == null) return;

                var klines = Helpers.StrategyUtils.ParseKlines(response.Content);
                if (klines == null || klines.Count < LookbackPeriod) return;

                // Calculate indicators
                var emaValues = CalculateEMA(klines.Select(k => k.Close).ToList(), EmaPeriod);
                var rsiValues = CalculateRSI(klines.Select(k => k.Close).ToList(), RsiLength);
                
                var currentClose = klines.Last().Close;
                var currentEMA = emaValues.Last();
                var currentRSI = rsiValues.Last();

                // Update trend state
                UpdateTrendState(currentClose, currentEMA, currentRSI);

                // Get Fibonacci levels
                var (fibHigh, fibLow) = GetFibHighLow(klines);
                var fibLevels = GetFibonacciLevels(fibHigh, fibLow);

                var currentKline = klines.Last();
                var prevKline = klines[^2];

                // Generate signals (matches PineScript exactly)
                if (_isUptrend && 
                    currentKline.Low <= fibLevels[61.8m] && 
                    prevKline.Low > fibLevels[61.8m])
                {
                    await OrderManager.PlaceLongOrderAsync(symbol, currentKline.Close, 
                        "Fib-Long", currentKline.CloseTime);
                    LogTradeSignal("LONG", symbol, currentKline.Close, fibHigh, fibLow);
                }
                else if (_isDowntrend && 
                         currentKline.High >= fibLevels[38.2m] && 
                         prevKline.High < fibLevels[38.2m])
                {
                    await OrderManager.PlaceShortOrderAsync(symbol, currentKline.Close, 
                        "Fib-Short", currentKline.CloseTime);
                    LogTradeSignal("SHORT", symbol, currentKline.Close, fibHigh, fibLow);
                }

                // Check exits
                await OrderManager.CheckAndCloseTrades(
                    new Dictionary<string, decimal> { { symbol, currentKline.Close } },
                    currentKline.CloseTime);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in FibonacciRetracementStrategy: {ex.Message}");
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
                if (historicalData == null || !historicalData.Any()) return;

                var klines = historicalData.Skip(Math.Max(0, historicalData.Count() - 150)).ToList();
                if (klines.Count < LookbackPeriod) return;

                // Calculate indicators
                var emaValues = CalculateEMA(klines.Select(k => k.Close).ToList(), EmaPeriod);
                var rsiValues = CalculateRSI(klines.Select(k => k.Close).ToList(), RsiLength);

                // Process each historical candle
                for (int i = LookbackPeriod; i < klines.Count; i++)
                {
                    var currentKline = klines[i];
                    var prevKline = klines[i-1];
                    var currentClose = currentKline.Close;
                    var currentEMA = emaValues[i];
                    var currentRSI = rsiValues[i];

                    // Update trend state for historical data
                    UpdateTrendState(currentClose, currentEMA, currentRSI);

                    // Get Fibonacci levels from lookback window
                    var lookbackWindow = klines.Skip(i - LookbackPeriod).Take(LookbackPeriod).ToList();
                    var (fibHigh, fibLow) = GetFibHighLow(lookbackWindow);
                    var fibLevels = GetFibonacciLevels(fibHigh, fibLow);

                    // Generate signals (identical to RunAsync)
                    if (_isUptrend && 
                        currentKline.Low <= fibLevels[61.8m] && 
                        prevKline.Low > fibLevels[61.8m])
                    {
                        if (currentKline.Symbol != null)
                        {
                        await OrderManager.PlaceLongOrderAsync(currentKline.Symbol!, currentKline.Close, 
                            "Fib-Long-Hist", currentKline.CloseTime);
                        LogTradeSignal("LONG", currentKline.Symbol!, currentKline.Close, fibHigh, fibLow);
                        }
                    }
                    else if (_isDowntrend && 
                             currentKline.High >= fibLevels[38.2m] && 
                             prevKline.High < fibLevels[38.2m])
                    {
                        if (currentKline.Symbol != null)
                        {
                        await OrderManager.PlaceShortOrderAsync(currentKline.Symbol!, currentKline.Close, 
                            "Fib-Short-Hist", currentKline.CloseTime);
                        LogTradeSignal("SHORT", currentKline.Symbol!, currentKline.Close, fibHigh, fibLow);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during historical backtest: {ex.Message}");
            }
        }

                private void UpdateTrendState(decimal currentClose, decimal currentEMA, decimal currentRSI)
        {
            bool potentialUptrend = currentClose > currentEMA && currentRSI > RsiUpper;
            bool potentialDowntrend = currentClose < currentEMA && currentRSI < RsiLower;

            if (potentialUptrend && !_isUptrend)
            {
                _trendBars++;
                if (_trendBars >= MinTrendBars)
                {
                    _isUptrend = true;
                    _isDowntrend = false;
                    _trendBars = 0;
                }
            }
            else if (potentialDowntrend && !_isDowntrend)
            {
                _trendBars++;
                if (_trendBars >= MinTrendBars)
                {
                    _isDowntrend = true;
                    _isUptrend = false;
                    _trendBars = 0;
                }
            }

            // Reset if trend breaks
            if ((currentClose <= currentEMA && _isUptrend) || 
                (currentClose >= currentEMA && _isDowntrend))
            {
                _isUptrend = false;
                _isDowntrend = false;
                _trendBars = 0;
            }
        }

        private (decimal High, decimal Low) GetFibHighLow(List<Kline> klines)
        {
            decimal fibHigh = klines[0].High;
            decimal fibLow = klines[0].Low;

            for (int i = 1; i < klines.Count; i++)
            {
                if (klines[i].High > fibHigh) fibHigh = klines[i].High;
                if (klines[i].Low < fibLow) fibLow = klines[i].Low;
            }

            return (fibHigh, fibLow);
        }
        
        private List<decimal> CalculateEMA(List<decimal> closes, int period)
        {
            var ema = new List<decimal>();
            decimal multiplier = 2m / (period + 1);
            
            // Start with SMA as first EMA value
            decimal firstEMA = closes.Take(period).Average();
            ema.Add(firstEMA);
            
            // Calculate subsequent EMAs
            for (int i = period; i < closes.Count; i++)
            {
                decimal currentEMA = (closes[i] - ema.Last()) * multiplier + ema.Last();
                ema.Add(currentEMA);
            }
            
            return ema;
        }
        
        private List<decimal> CalculateRSI(List<decimal> closes, int period)
        {
            var rsiValues = new List<decimal>();
            decimal avgGain = 0;
            decimal avgLoss = 0;

            // Calculate initial average gain/loss
            for (int i = 1; i <= period; i++)
            {
                decimal change = closes[i] - closes[i-1];
                if (change > 0)
                    avgGain += change;
                else
                    avgLoss += Math.Abs(change);
            }

            avgGain /= period;
            avgLoss /= period;

            // First RSI value
            decimal rs = avgLoss == 0 ? 100 : avgGain / avgLoss;
            rsiValues.Add(100 - (100 / (1 + rs)));

            // Subsequent RSI values
            for (int i = period + 1; i < closes.Count; i++)
            {
                decimal change = closes[i] - closes[i-1];
                decimal gain = change > 0 ? change : 0;
                decimal loss = change < 0 ? Math.Abs(change) : 0;

                avgGain = (avgGain * (period - 1) + gain) / period;
                avgLoss = (avgLoss * (period - 1) + loss) / period;

                rs = avgLoss == 0 ? 100 : avgGain / avgLoss;
                rsiValues.Add(100 - (100 / (1 + rs)));
            }

            return rsiValues;
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

        // Request & parse centralized in StrategyUtils

        public Dictionary<decimal, decimal> GetFibonacciLevels(decimal highestHigh, decimal lowestLow)
        {
            // Calculate the Fibonacci retracement levels from the highest high and lowest low
            var fibLevels = new Dictionary<decimal, decimal>
            {
                { 0m, lowestLow },             // 0% Fibonacci level
                { 23.6m, lowestLow + (highestHigh - lowestLow) * 0.236m }, // 23.6% level
                { 38.2m, lowestLow + (highestHigh - lowestLow) * 0.382m }, // 38.2% level
                { 50m, lowestLow + (highestHigh - lowestLow) * 0.5m },     // 50% level
                { 61.8m, lowestLow + (highestHigh - lowestLow) * 0.618m }, // 61.8% level
                { 78.6m, lowestLow + (highestHigh - lowestLow) * 0.786m }, // 76.4% level
                { 100m, highestHigh }           // 100% Fibonacci level
            };
            return fibLevels;
        }

    }
}
