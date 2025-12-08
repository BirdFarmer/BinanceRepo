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

    public class BollingerNoSqueezeStrategy : StrategyBase
    {
        protected override bool SupportsClosedCandles => true;
        // Read settings via BollingerSqueezeSettings so UI can edit them at runtime
        private int _bbPeriod => BollingerSqueezeSettings.BBPeriod;       // Match TradingView example
        private double _bbStdDev => BollingerSqueezeSettings.BBStdDev;   // Match TradingView example
        private int _atrPeriod => BollingerSqueezeSettings.ATRPeriod;      // Standard
        // runtime-configurable settings (set via BollingerSqueezeSettings)
        private int _squeezeBarsCount = 0;
        // Track squeeze end/window for live eligibility
        private int _barsSinceSqueezeEnd = int.MaxValue;
        private int _lastSqueezeLength = 0;
        // RSI-window state fields (kept for backward compatibility, not used)

        public BollingerNoSqueezeStrategy(RestClient client, string apiKey, OrderManager orderManager, Wallet wallet)
            : base(client, apiKey, orderManager, wallet)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] BollingerNoSqueezeStrategy initialized");
        }

        // Lightweight runtime settings holder to allow optimizer or UI to inject parameters per-run
        public enum TrendFilter { None = 0, EMA = 1, ADX = 2, RSI = 3 }

        public static class BollingerSqueezeSettings
        {
            // General/backtest settings (compat)
            public static decimal SqueezeThreshold { get; set; } = 2.0M;
            public static int MinSqueezeBars { get; set; } = 3;
            public static int PostSqueezeDelay { get; set; } = 2;
            public static int MaxWalkBars { get; set; } = 10;
            public static bool DebugMode { get; set; } = true;
            public static int RSICrossWindowBars { get; set; } = 5;

            // Minimal editable parameters (used by the strategy)
            public static int BBPeriod { get; set; } = 200;
            public static double BBStdDev { get; set; } = 2.0;
            public static int ATRPeriod { get; set; } = 14;
            public static decimal SqueezeMin { get; set; } = 1.2M;       // normalized bbWidth/ATR threshold
            public static decimal VolumeMultiplier { get; set; } = 1.2M; // volume > avgVolume * multiplier
            public static int AvgVolumePeriod { get; set; } = 20;        // window for average volume

            // Delayed re-entry: previous N candles must be outside the band
            public static int DelayedReentryPrevBars { get; set; } = 3;

            // Single trend filter selection (only one applies)
            public static TrendFilter TrendGate { get; set; } = TrendFilter.EMA;
            public static int TrendPeriod { get; set; } = 50; // used for EMA/ADX/RSI depending on TrendGate
            public static decimal AdxThreshold { get; set; } = 20m; // used only when TrendGate == ADX

            // Strategy guidance (not enforced by strategy; OrderManager handles exits)
            public static decimal TPmult { get; set; } = 2.0M;

            public static void Apply(decimal squeezeThreshold, int minSqueezeBars, int postDelay, int maxWalk)
            {
                SqueezeThreshold = squeezeThreshold;
                MinSqueezeBars = minSqueezeBars;
                PostSqueezeDelay = postDelay;
                MaxWalkBars = maxWalk;
            }
        }

        // Live RSI cross window state (per strategy instance)
        private int _rsiCrossRemaining = 0;
        private bool _rsiCrossIsUp = false;

        public override async Task RunAsync(string symbol, string interval)
        {
            try
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Starting ChartArt RSI + Bollinger Bands for {symbol} {interval}");

                var request = Helpers.StrategyUtils.CreateGet("/fapi/v1/klines", new Dictionary<string,string>
                {
                    {"symbol", symbol},
                    {"interval", interval},
                    {"limit", (_bbPeriod + _atrPeriod + 10).ToString()}
                });
                var response = await Client.ExecuteGetAsync(request);
                var klines = response.IsSuccessful && response.Content != null
                    ? Helpers.StrategyUtils.ParseKlines(response.Content, symbol)
                    : null;

                if (klines == null)
                {
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] No klines data for {symbol}");
                    return;
                }

                // Apply closed-candle policy: work off finalized candles for indicators when enabled
                var workingKlines = UseClosedCandles ? Helpers.StrategyUtils.ExcludeForming(klines) : klines;

                if (workingKlines.Count <= _bbPeriod)
                {
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Not enough data for {symbol}. Have {klines.Count}, need {_bbPeriod + _atrPeriod}");
                    return;
                }

                var (upperBand, lowerBand, middleBand) = CalculateBollingerBands(workingKlines);
                var atrValues = CalculateATR(workingKlines, _atrPeriod);
                var emaList = CalculateEMA(workingKlines, BollingerSqueezeSettings.TrendPeriod);

                int lastIdx = workingKlines.Count - 1;
                var eval = EvaluateSignalAtIndex(workingKlines, upperBand, lowerBand, middleBand, atrValues, emaList, lastIdx);

                if (BollingerSqueezeSettings.DebugMode)
                {
                    Console.WriteLine("[DEBUG LIVE] " + eval.debug);
                }

                if (eval.longSignal && workingKlines[lastIdx].Symbol != null)
                {
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ⚡ LONG NoSqueezeBB (delayed) {symbol} @ {FormatPrice(workingKlines[lastIdx].Close)} (bbNorm={(eval.currentATR>0? (eval.bbWidth/eval.currentATR):0):F2})");
                    await OrderManager.PlaceLongOrderAsync(workingKlines[lastIdx].Symbol!, workingKlines[lastIdx].Close, "NoSqueeze_BB_L", workingKlines[lastIdx].CloseTime);
                }
                else if (eval.shortSignal && workingKlines[lastIdx].Symbol != null)
                {
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ⚡ SHORT NoSqueezeBB (delayed) {symbol} @ {FormatPrice(workingKlines[lastIdx].Close)} (bbNorm={(eval.currentATR>0? (eval.bbWidth/eval.currentATR):0):F2})");
                    await OrderManager.PlaceShortOrderAsync(workingKlines[lastIdx].Symbol!, workingKlines[lastIdx].Close, "NoSqueeze_BB_S", workingKlines[lastIdx].CloseTime);
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
        
        private List<decimal> CalculateRSI(List<Kline> klines, int period)
        {
            if (klines == null) return new List<decimal>();
            var n = klines.Count;
            var rsi = Enumerable.Repeat(0m, n).ToList();
            if (n <= period) return rsi;

            // Use double for intermediate math to avoid decimal overflow on extreme price values
            var gains = new double[n - 1];
            var losses = new double[n - 1];
            for (int i = 1; i < n; i++)
            {
                var cur = klines[i];
                var prevk = klines[i - 1];
                if (cur == null || prevk == null)
                {
                    gains[i - 1] = 0.0;
                    losses[i - 1] = 0.0;
                    continue;
                }

                double change = (double)(cur.Close - prevk.Close);
                gains[i - 1] = change > 0 ? change : 0.0;
                losses[i - 1] = change < 0 ? Math.Abs(change) : 0.0;
            }

            double avgGain = gains.Take(period).Average();
            double avgLoss = losses.Take(period).Average();

            double firstRsi;
            if (avgLoss == 0.0)
            {
                firstRsi = avgGain == 0.0 ? 50.0 : 100.0;
            }
            else
            {
                var rs = avgGain / avgLoss;
                firstRsi = 100.0 - (100.0 / (1.0 + rs));
            }

            rsi[period] = (decimal)firstRsi;

            for (int i = period + 1; i < n; i++)
            {
                double gain = gains[i - 1];
                double loss = losses[i - 1];

                avgGain = ((avgGain * (period - 1)) + gain) / period;
                avgLoss = ((avgLoss * (period - 1)) + loss) / period;

                double v;
                if (avgLoss == 0.0)
                {
                    v = avgGain == 0.0 ? 50.0 : 100.0;
                }
                else
                {
                    var rs = avgGain / avgLoss;
                    v = 100.0 - (100.0 / (1.0 + rs));
                }

                // clamp to [0,100] and store as decimal
                if (double.IsNaN(v) || double.IsInfinity(v)) v = 50.0;
                if (v < 0.0) v = 0.0;
                if (v > 100.0) v = 100.0;

                rsi[i] = (decimal)v;
            }

            return rsi;
        }

        private List<decimal> CalculateEMA(List<Kline> klines, int period)
        {
            var quotes = klines.Select(k => new Skender.Stock.Indicators.Quote
            {
                Date = DateTimeOffset.FromUnixTimeMilliseconds(k.OpenTime).DateTime,
                Close = k.Close
            }).ToList();

            var ema = quotes.GetEma(period).ToList();
            return ema.Select(e => (decimal)(e.Ema ?? 0)).ToList();
        }

        private List<decimal> CalculateADX(List<Kline> klines, int period)
        {
            var quotes = klines.Select(k => new Skender.Stock.Indicators.Quote
            {
                Date = DateTimeOffset.FromUnixTimeMilliseconds(k.OpenTime).DateTime,
                High = k.High,
                Low = k.Low,
                Close = k.Close
            }).ToList();

            var adx = quotes.GetAdx(period).ToList();
            return adx.Select(a => (decimal)(a.Adx ?? 0)).ToList();
        }

        // Centralized evaluation used by both live and historical flows to guarantee parity
        private (bool longSignal, bool shortSignal, bool isNoSqueeze, string debug, decimal currentATR, decimal bbWidth) EvaluateSignalAtIndex(
            List<Kline> klines,
            List<decimal> upperBand,
            List<decimal> lowerBand,
            List<decimal> middleBand,
            List<decimal> atrValues,
            List<decimal> emaList,
            int idx)
        {
            var k = klines[idx];
            decimal currClose = k.Close;
            decimal currUpper = upperBand[idx];
            decimal currLower = lowerBand[idx];

            // ATR mapping
            decimal currentATR = 0;
            int atrIndex = idx - _atrPeriod;
            if (atrIndex >= 0 && atrIndex < atrValues.Count) currentATR = atrValues[atrIndex];

            var bbWidth = currUpper - currLower;
            bool isNoSqueeze = currentATR > 0 ? (bbWidth / currentATR) >= BollingerSqueezeSettings.SqueezeMin : false;

            int volStart = Math.Max(0, idx - BollingerSqueezeSettings.AvgVolumePeriod + 1);
            var avgVol = klines.Skip(volStart).Take(BollingerSqueezeSettings.AvgVolumePeriod).Select(x => x.Volume).DefaultIfEmpty(0m).Average();
            decimal currVol = k.Volume;
            bool volOk = avgVol > 0 ? currVol > avgVol * BollingerSqueezeSettings.VolumeMultiplier : true;

            bool trendOk = true;

            bool adxOk = true;
            if (BollingerSqueezeSettings.TrendGate == TrendFilter.ADX)
            {
                var adxList = CalculateADX(klines, BollingerSqueezeSettings.TrendPeriod);
                decimal currAdx = adxList.Count > idx ? adxList[idx] : 0;
                adxOk = currAdx >= BollingerSqueezeSettings.AdxThreshold;
                trendOk = adxOk;
            }
            else if (BollingerSqueezeSettings.TrendGate == TrendFilter.RSI)
            {
                var rsiList = CalculateRSI(klines, BollingerSqueezeSettings.TrendPeriod);
                decimal currRsi = rsiList.Count > idx ? rsiList[idx] : 50m;
                trendOk = currRsi > 50m;
            }
            else if (BollingerSqueezeSettings.TrendGate == TrendFilter.EMA)
            {
                decimal currEMA = emaList.Count > idx ? emaList[idx] : 0;
                trendOk = currClose > currEMA;
            }

            bool prev3LowsBelow = false;
            bool prev3HighsAbove = false;
            int prevN = BollingerSqueezeSettings.DelayedReentryPrevBars;
            if (idx >= prevN)
            {
                bool allLowsBelow = true;
                bool allHighsAbove = true;
                for (int i = 1; i <= prevN; i++)
                {
                    if (!(klines[idx - i].Low < lowerBand[idx - i])) allLowsBelow = false;
                    if (!(klines[idx - i].High > upperBand[idx - i])) allHighsAbove = false;
                }

                prev3LowsBelow = allLowsBelow;
                prev3HighsAbove = allHighsAbove;
            }

            // Base delayed re-entry conditions (previous N bars outside then current back inside)
            bool baseLong = prev3LowsBelow && klines[idx].Low > currLower && trendOk;
            bool baseShort = prev3HighsAbove && klines[idx].High < currUpper && trendOk;

            // Final entry requires no-squeeze, volume filter and (if applicable) ADX gate
            bool longEntryCond = baseLong && isNoSqueeze && volOk && adxOk;
            bool shortEntryCond = baseShort && isNoSqueeze && volOk && adxOk;

            string debug = $"idx={idx} sym={k.Symbol} close={currClose:F6}, upper={currUpper:F6}, lower={currLower:F6}, bbWidth={bbWidth:F6}, ATR={currentATR:F6}, bbNorm={(currentATR>0? (bbWidth/currentATR):0):F4}, isNoSqueeze={isNoSqueeze}, baseLong={baseLong}, baseShort={baseShort}, longAllowed={longEntryCond}, shortAllowed={shortEntryCond}, trendOk={trendOk}, volOk={volOk}, adxOk={adxOk}, avgVol={avgVol:F4}, currVol={currVol:F4}";

            return (longEntryCond, shortEntryCond, isNoSqueeze, debug, currentATR, bbWidth);
        }
        public override async Task RunOnHistoricalDataAsync(IEnumerable<Kline> historicalData)
        {
            var klines = historicalData.ToList();
            // Diagnostic: report whether Symbols are present in historical data
            try
            {
                int total = klines.Count;
                int nullSymbols = klines.Count(k => string.IsNullOrEmpty(k.Symbol));
                var sample = string.Join(",", klines.Take(5).Select(k => k.Symbol ?? "(null)"));
                Console.WriteLine($"[HIST INIT] Received {total} klines; null Symbol count={nullSymbols}; sampleSymbols={sample}");
            }
            catch { /* non-fatal diagnostics */ }
            // Minimal logging for backtests: entries only. Suppress per-run diagnostics to speed up bulk runs.

            if (klines.Count > _bbPeriod + _atrPeriod)
            {
                var (upperBand, lowerBand, middleBand) = CalculateBollingerBands(klines);
                var atrValues = CalculateATR(klines, _atrPeriod);
                // Precompute RSI and EMA for historical debug checks
                var rsiHist = CalculateRSI(klines, period: 6);
                var emaList = CalculateEMA(klines, BollingerSqueezeSettings.TrendPeriod);

                int squeezeCount = 0;
                int signalCount = 0;

                // Determine earliest index where both BB and ATR are available
                int startIndex = Math.Max(_bbPeriod - 1, _atrPeriod);

                // Use a local squeeze counter and track the start/end index for backtests
                int localSqueezeBars = 0;
                int squeezeStartIndex = -1;
                int squeezeEndIndex = -1;
                int lastSqueezeLength = 0;

                

                for (int k = startIndex; k < klines.Count; k++)
                {
                    var currentKline = klines[k];

                    // Evaluate using centralized helper
                    var eval = EvaluateSignalAtIndex(klines, upperBand, lowerBand, middleBand, atrValues, emaList, k);

                    // Track squeeze start/end for stats (eval.isNoSqueeze == true means NOT a squeeze)
                    if (!eval.isNoSqueeze)
                    {
                        if (localSqueezeBars == 0)
                        {
                            squeezeStartIndex = k;
                            squeezeEndIndex = -1;
                        }
                        localSqueezeBars++;
                    }
                    else
                    {
                        if (localSqueezeBars > 0)
                        {
                            squeezeEndIndex = k - 1;
                            lastSqueezeLength = localSqueezeBars;
                        }
                        localSqueezeBars = 0;
                    }

                    if (BollingerSqueezeSettings.DebugMode)
                    {
                        Console.WriteLine("[DEBUG HIST] " + eval.debug);
                    }

                    if (eval.longSignal && currentKline.Symbol != null)
                    {
                        signalCount++;
                        var squeezeStartTime = squeezeStartIndex >= 0 ? klines[squeezeStartIndex].CloseTime.ToDateTime() : (DateTime?)null;
                        Console.WriteLine("\n>>> LONG (No-Squeeze Delayed Re-entry) <<<");
                        Console.WriteLine($"- Time: {currentKline.CloseTime.ToDateTime()}");
                        Console.WriteLine($"- Price: {currentKline.Close}");
                        Console.WriteLine($"- Squeeze start: {(squeezeStartTime.HasValue ? squeezeStartTime.Value.ToString() : "unknown")} (Last squeeze bars: {lastSqueezeLength})");
                        Console.WriteLine($"- BB Upper: {upperBand[k]:F4}, Lower: {lowerBand[k]:F4}, BB Width: {eval.bbWidth:F4}");
                        Console.WriteLine($"- ATR: {eval.currentATR:F4}, Norm: {(eval.currentATR>0? (eval.bbWidth / eval.currentATR):0):F4}");

                        await OrderManager.PlaceLongOrderAsync(
                            currentKline.Symbol!,
                            currentKline.Close,
                            "NoSqueeze_BB_L",
                            currentKline.CloseTime);
                    }
                    else if (eval.shortSignal && currentKline.Symbol != null)
                    {
                        signalCount++;
                        var squeezeStartTime = squeezeStartIndex >= 0 ? klines[squeezeStartIndex].CloseTime.ToDateTime() : (DateTime?)null;
                        Console.WriteLine("\n>>> SHORT (No-Squeeze Delayed Re-entry) <<<");
                        Console.WriteLine($"- Time: {currentKline.CloseTime.ToDateTime()}");
                        Console.WriteLine($"- Price: {currentKline.Close}");
                        Console.WriteLine($"- Squeeze start: {(squeezeStartTime.HasValue ? squeezeStartTime.Value.ToString() : "unknown")} (Last squeeze bars: {lastSqueezeLength})");
                        Console.WriteLine($"- BB Upper: {upperBand[k]:F4}, Lower: {lowerBand[k]:F4}, BB Width: {eval.bbWidth:F4}");
                        Console.WriteLine($"- ATR: {eval.currentATR:F4}, Norm: {(eval.currentATR>0? (eval.bbWidth / eval.currentATR):0):F4}");

                        await OrderManager.PlaceShortOrderAsync(
                            currentKline.Symbol!,
                            currentKline.Close,
                            "NoSqueeze_BB_S",
                            currentKline.CloseTime);
                    }

                    // increment squeezeCount when squeeze condition is true (for stats)
                    if (!eval.isNoSqueeze) squeezeCount++;

                    // Check for open trade closing conditions
                    var currentPrices = currentKline.Symbol != null
                        ? new Dictionary<string, decimal> { { currentKline.Symbol!, currentKline.Close } }
                        : new Dictionary<string, decimal>();
                    await OrderManager.CheckAndCloseTrades(currentPrices, currentKline.OpenTime);
                }

                // Suppress verbose backtest summary for bulk optimizer runs. Entries are logged at signal time.
                // Console.WriteLine($"\nBacktest complete for {klines.FirstOrDefault()?.Symbol}");
                // Console.WriteLine($"- Total candles processed: {klines.Count}");
                // Console.WriteLine($"- Squeeze conditions detected: {squeezeCount}");
                // Console.WriteLine($"- Trading signals generated: {signalCount}");
                // if (signalCount == 0)
                // {
                //     Console.WriteLine("\nPossible reasons for no signals:");
                //     Console.WriteLine("- Price never crossed BB bands during squeeze conditions");
                //     Console.WriteLine("- Squeeze threshold too strict (try lowering to 0.5-0.7)");
                //     Console.WriteLine("- BB StdDev too wide (try reducing to 1.0-1.5)");
                //     Console.WriteLine("- Timeframe too small (try 1h or 4h)");
                //     Console.WriteLine("- Market conditions not volatile enough for squeezes");
                // }
            }
            else
            {
                // Console.WriteLine($"Not enough data for backtest. Need {_bbPeriod + _atrPeriod} candles, got {klines.Count}");
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
