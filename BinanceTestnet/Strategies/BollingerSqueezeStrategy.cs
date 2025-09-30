using BinanceTestnet.Models;
using BinanceTestnet.Trading;
using Newtonsoft.Json;
using RestSharp;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Skender.Stock.Indicators;

namespace BinanceLive.Strategies
{
    public static class DateTimeExtensions
    {
        public static DateTime ToDateTime(this long unixTimeMilliseconds)
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(unixTimeMilliseconds).UtcDateTime;
        }
    }

    public class BollingerSqueezeStrategy : StrategyBase
    {
        private readonly int _bbPeriod = 25;       // Standard
        private readonly double _bbStdDev = 1.5;   // Tighter bands → More crosses
        private readonly int _atrPeriod = 14;      // Standard
        private readonly decimal _squeezeThreshold = 2.0M; // Lower → More squeezes
        private int _squeezeBarsCount = 0;
        private readonly int _minSqueezeBars = 3;  // Require 3 bars of squeeze

        public BollingerSqueezeStrategy(RestClient client, string apiKey, OrderManager orderManager, Wallet wallet)
            : base(client, apiKey, orderManager, wallet)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] BollingerSqueezeStrategy initialized");
        }

        public override async Task RunAsync(string symbol, string interval)
        {
            try
            {
                //Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Starting Bollinger Squeeze for {symbol} {interval}");

                var klines = await FetchKlinesAsync(symbol, interval, _bbPeriod + _atrPeriod + 10);

                if (klines == null)
                {
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] No klines data for {symbol}");
                    return;
                }

                //Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Retrieved {klines.Count} klines for {symbol}");

                if (klines.Count > _bbPeriod + _atrPeriod)
                {
                    var (upperBand, lowerBand, _) = CalculateBollingerBands(klines);
                    var atrValues = CalculateATR(klines, _atrPeriod);
                    var currentATR = atrValues.Last();
                    var bbWidth = upperBand.Last() - lowerBand.Last();
                    bool isSqueeze = bbWidth < currentATR * _squeezeThreshold;
                    _squeezeBarsCount = isSqueeze ? _squeezeBarsCount + 1 : 0;
                    bool validSqueeze = _squeezeBarsCount >= _minSqueezeBars;
                    if (isSqueeze)
                    { 
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {symbol} | " +
                                                                $"Close: {klines.Last().Close:F4} | " +
                                                                $"BB Width: {bbWidth:F4} | " +
                                                                $"ATR: {currentATR:F4} | " +
                                                                $"Ratio: {bbWidth / currentATR:F4} | " +
                                                                $"Squeeze: {isSqueeze} | " +
                                                                $"Squeeze Count: {_squeezeBarsCount} | " +
                                                                $"Valid: {validSqueeze} | " +
                                                                $"Upper: {upperBand.Last():F4} | " +
                                                                $"Lower: {lowerBand.Last():F4}");

                    }
                    
                    if (isSqueeze)//validSqueeze
                    {
                        var lastClose = klines.Last().Close;
                        var prevClose = klines[^2].Close;

                        if (lastClose > upperBand.Last() && prevClose <= upperBand.Last())
                        {
                            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ⚡ LONG SIGNAL ⚡ {symbol} @ {lastClose}");
                            await OrderManager.PlaceLongOrderAsync(symbol, lastClose, "BB Squeeze", klines.Last().CloseTime);
                        }
                        else if (lastClose < lowerBand.Last() && prevClose >= lowerBand.Last())
                        {
                            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ⚡ SHORT SIGNAL ⚡ {symbol} @ {lastClose}");
                            await OrderManager.PlaceShortOrderAsync(symbol, lastClose, "BB Squeeze", klines.Last().CloseTime);
                        }
                    }
                }
                else
                {
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Not enough data for {symbol}. Have {klines.Count}, need {_bbPeriod + _atrPeriod}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ERROR in {symbol}: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
        }
        
        private (List<decimal> upperBand, List<decimal> lowerBand, List<decimal> middleBand) CalculateBollingerBands(List<Kline> klines)
        {
            var quotes = klines.Select(k => new Skender.Stock.Indicators.Quote
            {
                Date = DateTimeOffset.FromUnixTimeMilliseconds(k.OpenTime).DateTime,
                Close = k.Close
            }).ToList();

            var bbResults = quotes.GetBollingerBands(_bbPeriod, (double)_bbStdDev).ToList();

            return (
                    bbResults.Select(b => (decimal)(b.UpperBand ?? 0)).ToList(),
                    bbResults.Select(b => (decimal)(b.LowerBand ?? 0)).ToList(),
                    bbResults.Select(b => (decimal)(b.Sma ?? 0)).ToList()
            );
        }

        private decimal CalculateStandardDeviation(List<decimal> values, decimal mean)
        {
            var sum = values.Sum(v => (v - mean) * (v - mean));
            var variance = sum / values.Count;
            return (decimal)Math.Sqrt((double)variance);
        }

        private List<decimal> CalculateATR(List<Kline> klines, int period)
        {
            var atrValues = new List<decimal>();
            var trueRanges = new List<decimal>();

            for (int i = 1; i < klines.Count; i++)
            {
                var current = klines[i];
                var previous = klines[i - 1];

                var highLow = current.High - current.Low;
                var highClose = Math.Abs(current.High - previous.Close);
                var lowClose = Math.Abs(current.Low - previous.Close);

                var trueRange = Math.Max(highLow, Math.Max(highClose, lowClose));
                trueRanges.Add(trueRange);

                if (i >= period)
                {
                    var atr = trueRanges.Skip(i - period).Take(period).Average();
                    atrValues.Add(atr);
                }
            }

            return atrValues;
        }


        public override async Task RunOnHistoricalDataAsync(IEnumerable<Kline> historicalData)
        {
            var klines = historicalData.ToList();
            Console.WriteLine($"\nStarting backtest for {klines.FirstOrDefault()?.Symbol} with {klines.Count} candles");

            if (klines.Count > _bbPeriod + _atrPeriod)
            {
                var (upperBand, lowerBand, middleBand) = CalculateBollingerBands(klines);
                var atrValues = CalculateATR(klines, _atrPeriod);

                // Console.WriteLine($"Indicator calculations complete:");
                // Console.WriteLine($"- BB Period: {_bbPeriod}, StdDev: {_bbStdDev}");
                // Console.WriteLine($"- ATR Period: {_atrPeriod}");
                // Console.WriteLine($"- Squeeze Threshold: {_squeezeThreshold}");

                int squeezeCount = 0;
                int signalCount = 0;

                for (int i = 0; i < upperBand.Count; i++) // Use the indicator array length
                {
                    int klineIndex = i + _bbPeriod - 1; // Match indicator index to kline index
                    if (klineIndex >= klines.Count) break;
                    
                    var currentKline = klines[klineIndex];
                    var currentUpper = upperBand[i];
                    var currentLower = lowerBand[i];
                    var currentMiddle = middleBand[i - _bbPeriod];
                    var currentATR = atrValues[i - _bbPeriod - _atrPeriod];
                    
                    // For ATR - need similar fix:
                    int atrIndex = i + _atrPeriod - 1; 
                    if (atrIndex < atrValues.Count)
                    {
                         currentATR = atrValues[atrIndex];
                    }

                    var bbWidth = currentUpper - currentLower;
                    bool isSqueeze = bbWidth < currentATR * _squeezeThreshold;
                    _squeezeBarsCount = isSqueeze ? _squeezeBarsCount + 1 : 0;
                    bool validSqueeze = _squeezeBarsCount >= _minSqueezeBars;

                    // Debug print current values
                    //if (i % 10 == 0 || isSqueeze) // Print every 10 candles or during squeezes
                    if (isSqueeze)
                    {
                        Console.WriteLine($"\nCandle {i} ({currentKline.CloseTime.ToDateTime()})");
                        Console.WriteLine($"- Close: {currentKline.Close}");
                        Console.WriteLine($"- BB Upper: {currentUpper:F4}, Middle: {currentMiddle:F4}, Lower: {currentLower:F4}");
                        Console.WriteLine($"- BB Width: {bbWidth:F4}, ATR: {currentATR:F4}, Ratio: {bbWidth / currentATR:F4}");
                        Console.WriteLine($"- Squeeze: {isSqueeze} (Count: {_squeezeBarsCount}, Valid: {validSqueeze})");
                        Console.WriteLine($"- Threshold: {_squeezeThreshold}");
                    }

                    if (isSqueeze)//validSqueeze
                    {
                        squeezeCount++;
                        var prevKline = klines[i - 1];

                        // Long signal
                        if (currentKline.Close > currentUpper && prevKline.Close <= currentUpper)
                        {
                            signalCount++;
                            Console.WriteLine($"\n>>> LONG SIGNAL <<<");
                            Console.WriteLine($"- Time: {currentKline.CloseTime.ToDateTime()}");
                            Console.WriteLine($"- Price: {currentKline.Close} (Crossed above Upper BB: {currentUpper})");
                            Console.WriteLine($"- BB Width: {bbWidth}, ATR: {currentATR}");
                            Console.WriteLine($"- Prev Close: {prevKline.Close}, Current Close: {currentKline.Close}");

                            await OrderManager.PlaceLongOrderAsync(
                                currentKline.Symbol,
                                currentKline.Close,
                                "BB Squeeze",
                                currentKline.CloseTime);
                        }
                        // Short signal
                        else if (currentKline.Close < currentLower && prevKline.Close >= currentLower)
                        {
                            signalCount++;
                            Console.WriteLine($"\n>>> SHORT SIGNAL <<<");
                            Console.WriteLine($"- Time: {currentKline.CloseTime.ToDateTime()}");
                            Console.WriteLine($"- Price: {currentKline.Close} (Crossed below Lower BB: {currentLower})");
                            Console.WriteLine($"- BB Width: {bbWidth}, ATR: {currentATR}");
                            Console.WriteLine($"- Prev Close: {prevKline.Close}, Current Close: {currentKline.Close}");

                            await OrderManager.PlaceShortOrderAsync(
                                currentKline.Symbol,
                                currentKline.Close,
                                "BB Squeeze",
                                currentKline.CloseTime);
                        }
                    }

                    // Check for open trade closing conditions
                    var currentPrices = new Dictionary<string, decimal> { { currentKline.Symbol, currentKline.Close } };
                    await OrderManager.CheckAndCloseTrades(currentPrices, currentKline.OpenTime);
                }

                Console.WriteLine($"\nBacktest complete for {klines.FirstOrDefault()?.Symbol}");
                Console.WriteLine($"- Total candles processed: {klines.Count}");
                Console.WriteLine($"- Squeeze conditions detected: {squeezeCount}");
                Console.WriteLine($"- Trading signals generated: {signalCount}");
                if (signalCount == 0)
                {
                    Console.WriteLine("\nPossible reasons for no signals:");
                    Console.WriteLine("- Price never crossed BB bands during squeeze conditions");
                    Console.WriteLine("- Squeeze threshold too strict (try lowering to 0.5-0.7)");
                    Console.WriteLine("- BB StdDev too wide (try reducing to 1.0-1.5)");
                    Console.WriteLine("- Timeframe too small (try 1h or 4h)");
                    Console.WriteLine("- Market conditions not volatile enough for squeezes");
                }
            }
            else
            {
                Console.WriteLine($"Not enough data for backtest. Need {_bbPeriod + _atrPeriod} candles, got {klines.Count}");
            }
        }

        private async Task<List<Kline>> FetchKlinesAsync(string symbol, string interval, int limit)
        {
            var request = CreateRequest("/fapi/v1/klines");
            request.AddParameter("symbol", symbol, ParameterType.QueryString);
            request.AddParameter("interval", interval, ParameterType.QueryString);
            request.AddParameter("limit", limit.ToString(), ParameterType.QueryString);

            var response = await Client.ExecuteGetAsync(request);
            if (response.IsSuccessful)
            {
                return ParseKlines(response.Content);
            }
            else
            {
                Console.WriteLine($"Failed to fetch klines for {symbol}: {response.ErrorMessage}");
                return null;
            }
        }

        private List<Kline> ParseKlines(string content)
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
    }
}