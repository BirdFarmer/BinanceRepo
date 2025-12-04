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

namespace BinanceTestnet.Strategies
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
        protected override bool SupportsClosedCandles => true;
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

                var request = Helpers.StrategyUtils.CreateGet("/fapi/v1/klines", new Dictionary<string,string>
                {
                    {"symbol", symbol},
                    {"interval", interval},
                    {"limit", (_bbPeriod + _atrPeriod + 10).ToString()}
                });
                var response = await Client.ExecuteGetAsync(request);
                var klines = response.IsSuccessful && response.Content != null
                    ? Helpers.StrategyUtils.ParseKlines(response.Content)
                    : null;

                if (klines == null)
                {
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] No klines data for {symbol}");
                    return;
                }

                //Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Retrieved {klines.Count} klines for {symbol}");

                // Apply closed-candle policy: work off finalized candles for indicators when enabled
                var workingKlines = UseClosedCandles ? Helpers.StrategyUtils.ExcludeForming(klines) : klines;

                if (workingKlines.Count > _bbPeriod + _atrPeriod)
                {
                    var (upperBand, lowerBand, _) = CalculateBollingerBands(workingKlines);
                    var atrValues = CalculateATR(workingKlines, _atrPeriod);
                    var currentATR = atrValues.Last();
                    var bbWidth = upperBand.Last() - lowerBand.Last();
                    bool isSqueeze = bbWidth < currentATR * _squeezeThreshold;
                    _squeezeBarsCount = isSqueeze ? _squeezeBarsCount + 1 : 0;
                    bool validSqueeze = _squeezeBarsCount >= _minSqueezeBars;
                    // Don't spam logs on every squeeze bar during live runs.
                    // Only log concise entry information when a signal triggers.
                    if (isSqueeze)
                    {
                        // Select signal/previous respecting policy (fallback to workingKlines end points)
                        var (signalKline, previousKline) = SelectSignalPair(klines);
                        if (signalKline == null || previousKline == null) return;
                        var lastClose = signalKline.Close;
                        var prevClose = previousKline.Close;
                        if (lastClose > upperBand.Last() && prevClose <= upperBand.Last() && symbol != null)
                        {
                            // Concise, readable entry log
                            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ⚡ LONG {symbol} @ {FormatPrice(lastClose)} | Upper: {FormatPrice(upperBand.Last())} | Lower: {FormatPrice(lowerBand.Last())} | ATR: {FormatPrice(currentATR)}");
                            await OrderManager.PlaceLongOrderAsync(symbol!, lastClose, "BB Squeeze", signalKline.CloseTime);
                        }
                        else if (lastClose < lowerBand.Last() && prevClose >= lowerBand.Last() && symbol != null)
                        {
                            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ⚡ SHORT {symbol} @ {FormatPrice(lastClose)} | Upper: {FormatPrice(upperBand.Last())} | Lower: {FormatPrice(lowerBand.Last())} | ATR: {FormatPrice(currentATR)}");
                            await OrderManager.PlaceShortOrderAsync(symbol!, lastClose, "BB Squeeze", signalKline.CloseTime);
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

                // Determine earliest index where both BB and ATR are available
                int startIndex = Math.Max(_bbPeriod - 1, _atrPeriod);

                // Use a local squeeze counter and track the start index for backtests
                int localSqueezeBars = 0;
                int squeezeStartIndex = -1;

                for (int k = startIndex; k < klines.Count; k++)
                {
                    var currentKline = klines[k];
                    var prevKline = klines[k - 1];

                    var currentUpper = upperBand[k];
                    var currentLower = lowerBand[k];
                    var currentMiddle = middleBand[k];

                    // ATR list is shorter: atrValues[0] corresponds to klines[_atrPeriod]
                    decimal currentATR = 0;
                    int atrIndex = k - _atrPeriod;
                    if (atrIndex >= 0 && atrIndex < atrValues.Count)
                    {
                        currentATR = atrValues[atrIndex];
                    }

                    var bbWidth = currentUpper - currentLower;
                    bool isSqueeze = currentATR > 0 ? bbWidth < currentATR * _squeezeThreshold : false;

                    // Track squeeze start and length but avoid verbose per-bar printing
                    if (isSqueeze)
                    {
                        if (localSqueezeBars == 0)
                        {
                            squeezeStartIndex = k;
                        }
                        localSqueezeBars++;
                    }
                    else
                    {
                        // reset when squeeze ends
                        localSqueezeBars = 0;
                        squeezeStartIndex = -1;
                    }

                    if (isSqueeze)
                    {
                        squeezeCount++;

                        // Long signal
                        if (currentKline.Close > currentUpper && prevKline.Close <= currentUpper && currentKline.Symbol != null)
                        {
                            signalCount++;
                            var squeezeStartTime = squeezeStartIndex >= 0 ? klines[squeezeStartIndex].CloseTime.ToDateTime() : (DateTime?)null;
                            Console.WriteLine($"\n>>> LONG SIGNAL <<<");
                            Console.WriteLine($"- Time: {currentKline.CloseTime.ToDateTime()}");
                            Console.WriteLine($"- Price: {currentKline.Close}");
                            Console.WriteLine($"- Squeeze start: {(squeezeStartTime.HasValue ? squeezeStartTime.Value.ToString() : "unknown")} (Bars: {localSqueezeBars})");
                            Console.WriteLine($"- BB Upper: {currentUpper:F4}, Lower: {currentLower:F4}, BB Width: {bbWidth:F4}");
                            Console.WriteLine($"- ATR: {currentATR:F4}, Ratio: {(currentATR>0? (bbWidth / currentATR):0):F4}");

                            await OrderManager.PlaceLongOrderAsync(
                                currentKline.Symbol!,
                                currentKline.Close,
                                "BB Squeeze",
                                currentKline.CloseTime);
                        }
                        // Short signal
                        else if (currentKline.Close < currentLower && prevKline.Close >= currentLower && currentKline.Symbol != null)
                        {
                            signalCount++;
                            var squeezeStartTime = squeezeStartIndex >= 0 ? klines[squeezeStartIndex].CloseTime.ToDateTime() : (DateTime?)null;
                            Console.WriteLine($"\n>>> SHORT SIGNAL <<<");
                            Console.WriteLine($"- Time: {currentKline.CloseTime.ToDateTime()}");
                            Console.WriteLine($"- Price: {currentKline.Close}");
                            Console.WriteLine($"- Squeeze start: {(squeezeStartTime.HasValue ? squeezeStartTime.Value.ToString() : "unknown")} (Bars: {localSqueezeBars})");
                            Console.WriteLine($"- BB Upper: {currentUpper:F4}, Lower: {currentLower:F4}, BB Width: {bbWidth:F4}");
                            Console.WriteLine($"- ATR: {currentATR:F4}, Ratio: {(currentATR>0? (bbWidth / currentATR):0):F4}");

                            await OrderManager.PlaceShortOrderAsync(
                                currentKline.Symbol!,
                                currentKline.Close,
                                "BB Squeeze",
                                currentKline.CloseTime);
                        }
                    }

                    // Check for open trade closing conditions
                    var currentPrices = currentKline.Symbol != null
                        ? new Dictionary<string, decimal> { { currentKline.Symbol!, currentKline.Close } }
                        : new Dictionary<string, decimal>();
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

        // Format prices with readable precision without overflowing with decimals.
        private static string FormatPrice(decimal price)
        {
            // Use fewer decimals for larger prices, more for small prices, but cap at 6 decimals
            if (price >= 100m) return price.ToString("F2");
            if (price >= 1m) return price.ToString("F4");
            if (price >= 0.01m) return price.ToString("F6");
            // very small prices: show up to 6 significant digits
            return price.ToString("G6");
        }

        // Parse and request helpers centralized in StrategyUtils
    }
}