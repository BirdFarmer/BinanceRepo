using BinanceTestnet.Models;
using Newtonsoft.Json;
using RestSharp;
using Skender.Stock.Indicators;
using System.Globalization;
using BinanceTestnet.Trading;

namespace BinanceTestnet.Strategies
{
    public class HullSMAStrategy : StrategyBase, ISnapshotAwareStrategy
    {
        protected override bool SupportsClosedCandles => true;
        private const int HullShortLength = 35; // Short Hull length
        private const int HullLongLength = 100; // Long Hull length
        private const int RsiLength = 20;
        private const int RsiLower = 40;
        private const int RsiUpper = 60;
        
        // NEW: Optimized settings from PineScript testing
        // Enforce volume confirmation: do not take trades when recent volume is below average.
        // recentVolume = avg(last 5) ; avgVolume = avg(last 20)
        // Condition: recentVolume >= avgVolume * MinVolumeRatio
        // Setting MinVolumeRatio = 1.0 requires recent >= avg (blocks below-average volume)
        private const bool UseVolumeConfirmation = true;
        private const decimal MinVolumeRatio = 1.0m;
        private const bool UseMomentumConfirmation = true;
        private const int MomentumPeriod = 5;
        private const bool UseTrendStrength = false; // Disabled as per your testing
        // Toggle to reduce UI spam — enable only for debugging.
        // Made public so you can toggle at runtime from tests or the runner.
        public static bool EnableDebugLogging = false;

        public HullSMAStrategy(RestClient client, string apiKey, OrderManager orderManager, Wallet wallet)
            : base(client, apiKey, orderManager, wallet)
        {
        }

        // Diagnostic counters (aggregated across symbols per runner cycle)
        private static int _diagTotalSymbols = 0;
        private static int _diagHullCandidates = 0;
        private static int _diagRsiPassed = 0;
        private static int _diagVolumePassed = 0;
        private static int _diagMomentumPassed = 0;
        private static int _diagTradesPlaced = 0;

        public static string DumpAndResetDiagnostics()
        {
            var s = $"[HullSMAStrategy Diagnostics] Symbols={_diagTotalSymbols}, HullCandidates={_diagHullCandidates}, RSIpassed={_diagRsiPassed}, VolPassed={_diagVolumePassed}, MomPassed={_diagMomentumPassed}, TradesPlaced={_diagTradesPlaced}";
            _diagTotalSymbols = 0;
            _diagHullCandidates = 0;
            _diagRsiPassed = 0;
            _diagVolumePassed = 0;
            _diagMomentumPassed = 0;
            _diagTradesPlaced = 0;
            return s;
        }

        private enum SignalDirection
        {
            Long = 1,
            Short = -1
        }

        public override async Task RunAsync(string symbol, string interval)
        {
            try
            {
                LogDebug($"RunAsync start for {symbol} interval={interval}");
                var request = Helpers.StrategyUtils.CreateGet("/fapi/v1/klines", new Dictionary<string,string>
                {
                    {"symbol", symbol},
                    {"interval", interval},
                    {"limit", "210"}
                });

                var response = await Client.ExecuteGetAsync(request);
                LogDebug($"HTTP request completed for {symbol}. Success={response.IsSuccessful}");
                if (response.IsSuccessful && response.Content != null)
                {
                    var klines = Helpers.StrategyUtils.ParseKlines(response.Content);
                    LogDebug($"Parsed klines for {symbol}: count={(klines==null?0:klines.Count)}");
                    if (klines != null && klines.Count > 1)
                    {
                        await ProcessKlinesAsync(klines, symbol);
                    }
                    else
                    {
                        LogError($"Not enough klines data available for {symbol}.");
                    }
                }
                else
                {
                    HandleErrorResponse(symbol, response);
                }
            }
            catch (Exception ex)
            {
                LogError($"Error processing {symbol}: {ex.Message}");
            }
        }

        public async Task RunAsyncWithSnapshot(string symbol, string interval, Dictionary<string, List<Kline>> snapshot)
        {
            if (snapshot != null && snapshot.TryGetValue(symbol, out var klines) && klines != null && klines.Count > 0)
            {
                await ProcessKlinesAsync(klines, symbol);
            }
            else
            {
                await RunAsync(symbol, interval);
            }
        }

        private async Task ProcessKlinesAsync(List<Kline> klines, string symbol)
        {
            // Build indicator quotes using policy-aware helper
            var quotes = ToIndicatorQuotes(klines);

            var rsiResult = Indicator.GetRsi(quotes, RsiLength).LastOrDefault();
            var rsi = rsiResult?.Rsi ?? 50; // neutral fallback

            // Add a filter for RSI to avoid trades in a "boring" zone (between 40 and 60)
            bool rsiNotInBoringZone = rsi < RsiLower || rsi > RsiUpper;

            var hullShortResults = Helpers.StrategyUtils.CalculateEHMA(quotes, HullShortLength)
                .Select(x => new HullSuiteResult { Date = x.Date, EHMA = x.EHMA, EHMAPrev = x.EHMAPrev}).ToList();
            var hullLongResults = Helpers.StrategyUtils.CalculateEHMA(quotes, HullLongLength)
                .Select(x => new HullSuiteResult { Date = x.Date, EHMA = x.EHMA, EHMAPrev = x.EHMAPrev}).ToList();

            var (signalKline, previousKline) = SelectSignalPair(klines);
            LogDebug($"Selected signal pair for {symbol}: signalTime={signalKline?.CloseTime}, prevTime={previousKline?.CloseTime}");
            if (signalKline == null || previousKline == null) return;
            var currentKline = signalKline;
            var prevKline = previousKline;
            var currentHullShort = hullShortResults.LastOrDefault();
            var currentHullLong = hullLongResults.LastOrDefault();
            var prevHullShort = hullShortResults[hullShortResults.Count - 2];
            var prevHullLong = hullLongResults[hullLongResults.Count - 2];
                        
            // NEW: Apply volume confirmation
            bool volumeOk = true;
            if (UseVolumeConfirmation)
            {
                volumeOk = HasVolumeConfirmation(klines, MinVolumeRatio);
            }

            LogDebug($"Filters for {symbol}: RSIok={rsiNotInBoringZone}, VolumeOk={volumeOk}");

            // NOTE: momentum confirmation is direction-specific and evaluated
            // only after we detect a hull crossover to avoid unnecessary work/logs.

            if (currentHullShort != null && currentHullLong != null && prevHullShort != null && prevHullLong != null)
            {
                System.Threading.Interlocked.Increment(ref _diagTotalSymbols);
                bool isHullCrossingUp = currentHullShort.EHMA > currentHullLong.EHMA 
                                        && prevHullShort.EHMA <= prevHullLong.EHMA
                                        && currentHullLong.EHMA > prevHullLong.EHMA;
                bool isHullCrossingDown = currentHullShort.EHMA < currentHullLong.EHMA 
                                          && prevHullShort.EHMA >= prevHullLong.EHMA
                                          && currentHullLong.EHMA < prevHullLong.EHMA;
                if (isHullCrossingUp || isHullCrossingDown)
                {
                    System.Threading.Interlocked.Increment(ref _diagHullCandidates);
                }
                
                if (isHullCrossingUp)
                {
                    // Momentum check for long direction
                    bool momentumOkDir = true;
                    if (UseMomentumConfirmation)
                    {
                        momentumOkDir = HasMomentumConfirmation(klines, MomentumPeriod, SignalDirection.Long);
                    }

                    if (rsiNotInBoringZone) System.Threading.Interlocked.Increment(ref _diagRsiPassed);
                    if (volumeOk) System.Threading.Interlocked.Increment(ref _diagVolumePassed);
                    if (momentumOkDir) System.Threading.Interlocked.Increment(ref _diagMomentumPassed);

                    if (!rsiNotInBoringZone || !volumeOk || !momentumOkDir)
                    {
                        LogDebug($"Long signal blocked by filters: RSIok={rsiNotInBoringZone}, VolOk={volumeOk}, MomOk={momentumOkDir}");
                    }
                    else
                    {
                        LogDebug($"Hull {HullShortLength} crossing above Hull {HullLongLength} — placing LONG");
                        await OrderManager.PlaceLongOrderAsync(symbol, currentKline.Close, $"Hull {HullShortLength}/{HullLongLength}", currentKline.CloseTime);
                        System.Threading.Interlocked.Increment(ref _diagTradesPlaced);
                    }
                }
                else if (isHullCrossingDown)
                {
                    // Momentum check for short direction
                    bool momentumOkDir = true;
                    if (UseMomentumConfirmation)
                    {
                        momentumOkDir = HasMomentumConfirmation(klines, MomentumPeriod, SignalDirection.Short);
                    }

                    if (rsiNotInBoringZone) System.Threading.Interlocked.Increment(ref _diagRsiPassed);
                    if (volumeOk) System.Threading.Interlocked.Increment(ref _diagVolumePassed);
                    if (momentumOkDir) System.Threading.Interlocked.Increment(ref _diagMomentumPassed);

                    if (!rsiNotInBoringZone || !volumeOk || !momentumOkDir)
                    {
                        LogDebug($"Short signal blocked by filters: RSIok={rsiNotInBoringZone}, VolOk={volumeOk}, MomOk={momentumOkDir}");
                    }
                    else
                    {
                        LogDebug($"Hull {HullShortLength} crossing below Hull {HullLongLength} — placing SHORT");
                        await OrderManager.PlaceShortOrderAsync(symbol, currentKline.Close, $"Hull {HullShortLength}/{HullLongLength}", currentKline.CloseTime);
                        System.Threading.Interlocked.Increment(ref _diagTradesPlaced);
                    }
                }
            }
            else
            {
                LogError($"Required indicators data is not available for {symbol}.");
            }
        }

        public override async Task RunOnHistoricalDataAsync(IEnumerable<Kline> historicalCandles)
        {            
            LogDebug($"RunOnHistoricalDataAsync start: candles={historicalCandles.Count()}");
            // Process quotes for signals
            var quotes = historicalCandles.Select(k => new BinanceTestnet.Models.Quote
            {
                Date = DateTimeOffset.FromUnixTimeMilliseconds(k.OpenTime).UtcDateTime,
                High = k.High,
                Low = k.Low,
                Close = k.Close
            }).ToList();

            var rsiHistResult = Indicator.GetRsi(quotes, RsiLength).LastOrDefault();
            var rsi = rsiHistResult?.Rsi ?? 50; // neutral fallback

            var hullShortResults = Helpers.StrategyUtils.CalculateEHMA(quotes, HullShortLength)
                .Select(x => new HullSuiteResult { Date = x.Date, EHMA = x.EHMA, EHMAPrev = x.EHMAPrev}).ToList();
            var hullLongResults = Helpers.StrategyUtils.CalculateEHMA(quotes, HullLongLength)
                .Select(x => new HullSuiteResult { Date = x.Date, EHMA = x.EHMA, EHMAPrev = x.EHMAPrev}).ToList();
            
            // Iterate over candles to generate signals
            for (int i = HullLongLength - 1; i < historicalCandles.Count(); i++)
            {               
                
                // Add a filter for RSI to avoid trades in a "boring" zone (between 40 and 60)
                bool rsiNotInBoringZone = rsi < RsiLower || rsi > RsiUpper;
                
                // NEW: Apply volume confirmation
                bool volumeOk = true;
                if (UseVolumeConfirmation)
                {
                    var recentKlines = historicalCandles.Take(i + 1).ToList();
                    volumeOk = HasVolumeConfirmation(recentKlines, MinVolumeRatio);
                }

                // NOTE: momentum confirmation is direction-specific and evaluated
                // only when a crossover is detected below to avoid heavy logging.

                if(!rsiNotInBoringZone || !volumeOk)
                    continue;

                var currentKline = historicalCandles.ElementAt(i);
                var prevKline = historicalCandles.ElementAt(i - 1);
                var currentHullShort = hullShortResults[i];
                var prevHullShort = hullShortResults[i - 1];
                var currentHullLong = hullLongResults[i];
                var prevHullLong = hullLongResults[i - 1];

                if (currentHullShort != null && currentHullLong != null && prevHullShort != null && prevHullLong != null)
                {
                    bool isHullCrossingUp = currentHullShort.EHMA > currentHullLong.EHMA && prevHullShort.EHMA <= prevHullLong.EHMA;
                    bool isHullCrossingDown = currentHullShort.EHMA < currentHullLong.EHMA && prevHullShort.EHMA >= prevHullLong.EHMA;

                    System.Threading.Interlocked.Increment(ref _diagTotalSymbols);

                    if (isHullCrossingUp || isHullCrossingDown)
                    {
                        System.Threading.Interlocked.Increment(ref _diagHullCandidates);
                    }

                    LogDebug($"Historical signal at index={i} for symbol={historicalCandles.ElementAt(i).Symbol}: crossingUp={isHullCrossingUp}, crossingDown={isHullCrossingDown}");

                    if (isHullCrossingUp)
                    {
                        if (!string.IsNullOrEmpty(currentKline.Symbol))
                        {
                            await OrderManager.PlaceLongOrderAsync(currentKline.Symbol, currentKline.Close, $"Hull {HullShortLength}/{HullLongLength}", currentKline.CloseTime);
                        }
                    }
                    else if (isHullCrossingDown)
                    {
                        if (!string.IsNullOrEmpty(currentKline.Symbol))
                        {
                            await OrderManager.PlaceShortOrderAsync(currentKline.Symbol, currentKline.Close, $"Hull {HullShortLength}/{HullLongLength}", currentKline.CloseTime);
                        }
                    }

                    if (!string.IsNullOrEmpty(currentKline.Symbol) && currentKline.Close > 0)
                    {
                        var currentPrices = new Dictionary<string, decimal> { { currentKline.Symbol, currentKline.Close } };
                        await OrderManager.CheckAndCloseTrades(currentPrices, currentKline.CloseTime);
                    }
                }
            }
        }

        // NEW: Volume Confirmation Method
        private bool HasVolumeConfirmation(List<Kline> klines, decimal minVolumeRatio)
        {
            if (klines.Count < 21) return false;
            
            // Get recent volume (last 5 candles)
            var recentVolume = klines.TakeLast(5).Average(k => k.Volume);
            // Get average volume (last 20 candles)
            var avgVolume = klines.TakeLast(20).Average(k => k.Volume);
            
            bool volumeOk = recentVolume >= avgVolume * minVolumeRatio;
            if (!volumeOk)
            {
                LogDebug($"Volume filter blocked: Recent={recentVolume:F0}, Avg={avgVolume:F0}, Ratio={(recentVolume/avgVolume):F2}, Required={minVolumeRatio:F1}");
            }
            
            return volumeOk;
        }
        // NEW: Momentum Confirmation Method (direction-aware)
        private bool HasMomentumConfirmation(List<Kline> klines, int momentumPeriod, SignalDirection direction)
        {
            if (klines.Count <= momentumPeriod) return false;

            var currentClose = klines.Last().Close;
            var previousClose = klines[klines.Count - momentumPeriod - 1].Close;

            decimal momentumPercent = (currentClose - previousClose) / previousClose * 100;

            LogDebug($"Momentum: {momentumPercent:F2}% over {momentumPeriod} periods (dir={direction})");

            if (direction == SignalDirection.Long)
                return momentumPercent > 0m;
            else
                return momentumPercent < 0m;
        }

        private void LogDebug(string message)
        {
            if (!EnableDebugLogging) return;
            Console.WriteLine(message);
        }

        private void LogError(string message)
        {
            Console.WriteLine($"Error: {message}");
        }

        private void HandleErrorResponse(string symbol, RestResponse response)
        {
            LogError($"Error for {symbol}: {response.ErrorMessage}");
            LogError($"Status Code: {response.StatusCode}");
            LogError($"Content: {response.Content}");
        }

        static async Task<decimal> GetCurrentPrice(RestClient client, string symbol)
        {
            var request = new RestRequest("/fapi/v1/ticker/price", Method.Get);
            request.AddParameter("symbol", symbol);
            var response = await client.ExecuteAsync<Dictionary<string, string>>(request);

            if (response.IsSuccessful && response.Content != null)
            {
                var priceData = JsonConvert.DeserializeObject<Dictionary<string, string>>(response.Content);
                if (priceData?.ContainsKey("price") == true)
                {
                    return decimal.Parse(priceData["price"], CultureInfo.InvariantCulture);
                }
            }

            throw new Exception("Failed to get current price");
        }
    }
}