using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using RestSharp;
using Skender.Stock.Indicators;
using BinanceTestnet.Models;
using BinanceTestnet.Trading;
using System.Globalization;

namespace BinanceTestnet.Strategies
{
    public class SupportResistanceStrategy : StrategyBase
    {
        private readonly int _lookback;
        private readonly double _volumeMultiplier;
        
        // Track breakout states
        private bool _resistanceBreakoutActive = false;
        private bool _supportBreakoutActive = false;
        private decimal _brokenResistanceLevel = 0;
        private decimal _brokenSupportLevel = 0;
        
        // Track if entries have been taken
        private bool _entryTakenFromSupportBreakout = false;
        private bool _entryTakenFromResistanceBreakout = false;
        private readonly Dictionary<string, DateTime> _lastProcessTime = new Dictionary<string, DateTime>();
        private readonly int _adxPeriod = 14;
        private readonly double _minAdxValue = 25.0;

        public SupportResistanceStrategy(RestClient client, string apiKey, OrderManager orderManager, Wallet wallet,
            int lookback = 20, double volumeMultiplier = 1.5)
            : base(client, apiKey, orderManager, wallet)
        {
            _lookback = lookback;
            _volumeMultiplier = volumeMultiplier;

            //Console.WriteLine($"S/R Strategy initialized with Lookback: {_lookback}, Volume Multiplier: {_volumeMultiplier}");
        }

        public override async Task RunAsync(string symbol, string interval)
        {
            if (_lastProcessTime.ContainsKey(symbol) &&
                DateTime.UtcNow - _lastProcessTime[symbol] < TimeSpan.FromMinutes(GetIntervalMinutes(interval) * 0.8))
            {
                //Console.WriteLine($"Skipping {symbol} - processed recently");
                return;
            }
            
            try
            {
                //Console.WriteLine($"\n=== Processing {symbol} ({interval}) ===");
                
                var request = Helpers.StrategyUtils.CreateGet("/fapi/v1/klines", new Dictionary<string,string>
                {
                    {"symbol", symbol},
                    {"interval", interval},
                    {"limit", "500"}
                });

                var response = await Client.ExecuteGetAsync(request);
                if (response.IsSuccessful && response.Content != null)
                {
                    var klines = Helpers.StrategyUtils.ParseKlines(response.Content);

                    if (klines != null && klines.Count > 1)
                    {
                        //Console.WriteLine($"Retrieved {klines.Count} klines for {symbol}");
                        
                        // SIMPLE: Always use the LAST CLOSED candle (index -2)
                        var lastClosedKline = klines[klines.Count - 2];
                        var klineEndTime = DateTimeOffset.FromUnixTimeMilliseconds(lastClosedKline.CloseTime).UtcDateTime;
                        
                        //Console.WriteLine($"Using closed candle from: {klineEndTime:HH:mm:ss}");
                        
                        // REMOVE ALL TIMING CHECKS - just use the data!
                        var quotes = klines.Select(k => new Skender.Stock.Indicators.Quote
                        {
                            Date = DateTimeOffset.FromUnixTimeMilliseconds(k.OpenTime).UtcDateTime,
                            Open = k.Open,
                            High = k.High,
                            Low = k.Low,
                            Close = k.Close,
                            Volume = k.Volume
                        }).ToList();

                        // Calculate indicators
                        var volumeSma = CalculateVolumeSma(quotes, 20);
                        var (pivotHighs, pivotLows) = FindPivotsOptimized(klines, _lookback);

                        if (volumeSma.Count > 1 && pivotHighs.Any() && pivotLows.Any())
                        {
                            var currentVolume = lastClosedKline.Volume;
                            
                            // Use PREVIOUS period SMA (exclude current forming candle)
                            double? avgVolume = volumeSma[volumeSma.Count - 2].Sma ?? 0;
                            var highVolume = (decimal)((avgVolume ?? 0) * _volumeMultiplier);
                            var lowVolume = (decimal)(avgVolume ?? 0);


                            //Console.WriteLine($"=== VOLUME DEBUG for {symbol} ===");
                            //Console.WriteLine($"Closed candle volume: {currentVolume}");
                            //Console.WriteLine($"Previous SMA value: {avgVolume}");
                            //Console.WriteLine($"High volume threshold ({_volumeMultiplier}x SMA): {highVolume}");
                            //Console.WriteLine($"Comparison: {currentVolume} > {highVolume} = {currentVolume > highVolume}");
                            //Console.WriteLine("=== END VOLUME DEBUG ===");

                            // Get most recent support/resistance levels
                            var recentResistance = pivotHighs.LastOrDefault(p => p < lastClosedKline.Close);
                            var recentSupport = pivotLows.LastOrDefault(p => p > lastClosedKline.Close);

                            //Console.WriteLine($"Recent resistance: {recentResistance}, Recent support: {recentSupport}");
                            //Console.WriteLine($"Closed candle close: {lastClosedKline.Close}");

                            // Check for breakouts and entries using CLOSED candle
                            await CheckSignals(symbol, lastClosedKline, recentResistance, recentSupport, 
                                currentVolume, highVolume, lowVolume, klines);
                            
                            // Update last price for position management
                            var lastPrice = lastClosedKline.Close;
                            var currentPrices = new Dictionary<string, decimal> { { symbol, lastPrice } };
                            await OrderManager.CheckAndCloseTrades(currentPrices);
                        }
                        else
                        {
                            if (volumeSma.Count <= 1) Console.WriteLine("Not enough volume SMA data available");
                            if (!pivotHighs.Any()) Console.WriteLine("No pivot highs found");
                            if (!pivotLows.Any()) Console.WriteLine("No pivot lows found");
                        }
                        
                        // Update last process time only on successful completion
                        _lastProcessTime[symbol] = DateTime.UtcNow;
                    }
                    else
                    {
                        //Console.WriteLine($"Not enough klines data available for {symbol} (need at least 2).");
                    }
                }
                else
                {
                    HandleErrorResponse(symbol, response);
                }
            }
            catch (Exception ex)
            {
                //Console.WriteLine($"Error processing {symbol}: {ex.Message}");
            }
        }
                
        private async Task CheckSignals(string symbol, Kline lastClosedKline, decimal? recentResistance,
                    decimal? recentSupport, decimal currentVolume, decimal highVolume, decimal lowVolume,
                    List<Kline> klines)
        {

            var adx = CalculateADX(klines);
            if (adx == null || adx < _minAdxValue)
            {
                Console.WriteLine($"Too weak trend (ADX: {adx}) - skipping signal checks for {symbol} - at {lastClosedKline.CloseTime.ToDateTime()}");
                return;
            }
                

            // Update support/resistance levels when new pivots are found
            if (recentSupport.HasValue)
            {
                //Console.WriteLine($"New support level detected: {recentSupport.Value}");
                _supportBreakoutActive = false;
                _brokenSupportLevel = 0;
                _entryTakenFromSupportBreakout = false;
            }

            if (recentResistance.HasValue)
            {
                //Console.WriteLine($"New resistance level detected: {recentResistance.Value}");
                _resistanceBreakoutActive = false;
                _brokenResistanceLevel = 0;
                _entryTakenFromResistanceBreakout = false;
            }

            // Check for resistance breakout (full candle above with big volume) - using CLOSED candle
            if (recentResistance.HasValue &&
                lastClosedKline.Close > recentResistance.Value &&
                currentVolume > highVolume &&
                !_resistanceBreakoutActive)
            {
                //Console.WriteLine($"RESISTANCE BREAKOUT - Close: {lastClosedKline.Close}, Resistance: {recentResistance.Value}, Volume: {currentVolume} > {highVolume}");
                _resistanceBreakoutActive = true;
                _brokenResistanceLevel = recentResistance.Value;
                _entryTakenFromResistanceBreakout = false;
                LogBreakoutSignal("RESISTANCE", symbol, lastClosedKline.Close, recentResistance.Value);
            }
            else if (recentResistance.HasValue && lastClosedKline.Close > recentResistance.Value && !_resistanceBreakoutActive)
            {
                //Console.WriteLine($"Resistance broken but volume too low: {currentVolume} <= {highVolume}");
            }

            // Check for support breakout (full candle below with big volume) - using CLOSED candle
            if (recentSupport.HasValue &&
                lastClosedKline.Close < recentSupport.Value &&
                currentVolume > highVolume &&
                !_supportBreakoutActive)
            {
                //Console.WriteLine($"SUPPORT BREAKOUT - Close: {lastClosedKline.Close}, Support: {recentSupport.Value}, Volume: {currentVolume} > {highVolume}");
                _supportBreakoutActive = true;
                _brokenSupportLevel = recentSupport.Value;
                _entryTakenFromSupportBreakout = false;
                LogBreakoutSignal("SUPPORT", symbol, lastClosedKline.Close, recentSupport.Value);
            }
            else if (recentSupport.HasValue && lastClosedKline.Close < recentSupport.Value && !_supportBreakoutActive)
            {
                //Console.WriteLine($"Support broken but volume too low: {currentVolume} <= {highVolume}");
            }

            // Check for retest entries (need previous kline data)
            if (klines.Count >= 3) // Need at least 3 candles for proper retest detection
            {
                var prevClosedKline = klines[klines.Count - 3]; // Two candles back
                var prevVolume = prevClosedKline.Volume;

                // Check for bullish engulfing
                bool bullishEngulfing = lastClosedKline.Close > lastClosedKline.Open &&
                                      prevClosedKline.Close < prevClosedKline.Open &&
                                      lastClosedKline.Close > prevClosedKline.High &&
                                      lastClosedKline.Open < prevClosedKline.Low;

                // Check for bearish engulfing
                bool bearishEngulfing = lastClosedKline.Close < lastClosedKline.Open &&
                                       prevClosedKline.Close > prevClosedKline.Open &&
                                       lastClosedKline.Close < prevClosedKline.Low &&
                                       lastClosedKline.Open > prevClosedKline.High;

                //Console.WriteLine($"Previous closed volume: {prevVolume}, Low volume threshold: {lowVolume}");
                //Console.WriteLine($"Bullish engulfing: {bullishEngulfing}, Bearish engulfing: {bearishEngulfing}");

                // Add this debug right before the retest conditions:
                //Console.WriteLine($"=== RETEST DEBUG for {symbol} ===");
                if (_resistanceBreakoutActive)
                {
                    //Console.WriteLine($"Resistance breakout active: {_brokenResistanceLevel}");
                    //Console.WriteLine($"Current low: {lastClosedKline.Low} <= Resistance: {lastClosedKline.Low <= _brokenResistanceLevel}");
                    //Console.WriteLine($"Prev low: {prevClosedKline.Low} > Resistance: {prevClosedKline.Low > _brokenResistanceLevel}");
                    //Console.WriteLine($"Prev volume: {prevVolume} < Threshold: {prevVolume < lowVolume}");
                }

                if (_supportBreakoutActive)
                {
                    //Console.WriteLine($"Support breakout active: {_brokenSupportLevel}");
                    //Console.WriteLine($"Current high: {lastClosedKline.High} >= Support: {lastClosedKline.High >= _brokenSupportLevel}");
                    //Console.WriteLine($"Prev high: {prevClosedKline.High} < Support: {prevClosedKline.High < _brokenSupportLevel}");
                    //Console.WriteLine($"Prev volume: {prevVolume} < Threshold: {prevVolume < lowVolume}");
                }
                //Console.WriteLine("=== END RETEST DEBUG ===");                

                                // Long entry condition - resistance breakout + retest
                if (_resistanceBreakoutActive &&
                    lastClosedKline.Low <= _brokenResistanceLevel &&      // Current candle touches the level
                    prevClosedKline.Low > _brokenResistanceLevel &&       // Previous candle completely above
                    prevVolume < lowVolume &&                             // Low volume on retest
                    !_entryTakenFromResistanceBreakout)
                {
                    //Console.WriteLine($"LONG ENTRY - Price: {lastClosedKline.Close}, Broken Resistance: {_brokenResistanceLevel}");
                    await OrderManager.PlaceLongOrderAsync(symbol, lastClosedKline.Close, "S/R-Breakout", lastClosedKline.OpenTime);
                    _entryTakenFromResistanceBreakout = true;
                    LogEntrySignal("LONG", symbol, lastClosedKline.Close, _brokenResistanceLevel);
                }
                else if (_resistanceBreakoutActive && !_entryTakenFromResistanceBreakout)
                {
                    //Console.WriteLine("Resistance breakout active but entry conditions not met:");
                    if (prevVolume >= lowVolume) Console.WriteLine($"  - Volume ({prevVolume}) not < threshold ({lowVolume})");
                    if (!bullishEngulfing) Console.WriteLine("  - No bullish engulfing pattern");
                }

                // Short entry condition - support breakout + retest
                if (_supportBreakoutActive && 
                    lastClosedKline.High >= _brokenSupportLevel &&        // Current candle touches the level
                    prevClosedKline.High < _brokenSupportLevel &&         // Previous candle completely below
                    prevVolume < lowVolume &&                             // Low volume on retest
                    !_entryTakenFromSupportBreakout)
                {
                    //Console.WriteLine($"SHORT ENTRY - Price: {lastClosedKline.Close}, Broken Support: {_brokenSupportLevel}");
                    await OrderManager.PlaceShortOrderAsync(symbol, lastClosedKline.Close, "S/R-Breakout", lastClosedKline.OpenTime);
                    _entryTakenFromSupportBreakout = true;
                    LogEntrySignal("SHORT", symbol, lastClosedKline.Close, _brokenSupportLevel);
                }
                else if (_supportBreakoutActive && !_entryTakenFromSupportBreakout)
                {
                    //Console.WriteLine("Support breakout active but entry conditions not met:");
                    if (prevVolume >= lowVolume) Console.WriteLine($"  - Volume ({prevVolume}) not < threshold ({lowVolume})");
                    if (!bearishEngulfing) Console.WriteLine("  - No bearish engulfing pattern");
                }
            }

            // Check for invalidations
            if (_resistanceBreakoutActive && lastClosedKline.High < _brokenResistanceLevel)
            {
                //Console.WriteLine($"Resistance breakout invalidated - High: {lastClosedKline.High} < Resistance: {_brokenResistanceLevel}");
                _resistanceBreakoutActive = false;
                _brokenResistanceLevel = 0;
                _entryTakenFromResistanceBreakout = false;
            }

            if (_supportBreakoutActive && lastClosedKline.Low > _brokenSupportLevel)
            {
                //Console.WriteLine($"Support breakout invalidated - Low: {lastClosedKline.Low} > Support: {_brokenSupportLevel}");
                _supportBreakoutActive = false;
                _brokenSupportLevel = 0;
                _entryTakenFromSupportBreakout = false;
            }
        }
        public static (List<decimal> pivotHighs, List<decimal> pivotLows) FindPivotsOptimized(List<Kline> klines, int lookback)
        {
            var pivotHighs = new List<decimal>();
            var pivotLows = new List<decimal>();
            
            int n = klines.Count;
            if (n < 2 * lookback + 1)
                return (pivotHighs, pivotLows);

            decimal[] highs = klines.Select(k => k.High).ToArray();
            decimal[] lows = klines.Select(k => k.Low).ToArray();

            for (int i = lookback; i < n - lookback; i++)
            {
                decimal currentHigh = highs[i];
                decimal currentLow = lows[i];
                
                bool isPivotHigh = true;
                bool isPivotLow = true;

                // Check ALL surrounding candles (left and right)
                for (int j = i - lookback; j <= i + lookback; j++)
                {
                    if (j == i) continue; // Skip the current candle

                    if (highs[j] >= currentHigh) 
                    { 
                        isPivotHigh = false; 
                        if (!isPivotLow) break; // Early exit if both fail
                    }

                    if (lows[j] <= currentLow) 
                    { 
                        isPivotLow = false; 
                        if (!isPivotHigh) break; // Early exit if both fail
                    }
                }

                if (isPivotHigh && currentHigh > 0) pivotHighs.Add(currentHigh);
                if (isPivotLow && currentLow > 0) pivotLows.Add(currentLow);
            }

            return (pivotHighs, pivotLows);
        }     

        private double? CalculateADX(List<Kline> klines)
        {
            if (klines.Count < _adxPeriod * 2) return null;
            
            var quotes = klines.Select(k => new Skender.Stock.Indicators.Quote 
            {
                Date = DateTimeOffset.FromUnixTimeMilliseconds(k.OpenTime).UtcDateTime,
                Open = k.Open,
                High = k.High,
                Low = k.Low,
                Close = k.Close,
                Volume = k.Volume
            }).ToList();
            
            var adxResults = quotes.GetAdx(_adxPeriod).ToList();
            return adxResults.Last().Adx;
        }


        public override async Task RunOnHistoricalDataAsync(IEnumerable<Kline> historicalData)
        {
            var klines = historicalData.ToList();
            //Console.WriteLine($"\n=== Processing historical data: {klines.Count} klines ===");

            var quotes = klines.Select(k => new Skender.Stock.Indicators.Quote
            {
                Date = DateTimeOffset.FromUnixTimeMilliseconds(k.OpenTime).UtcDateTime,
                Open = k.Open,
                High = k.High,
                Low = k.Low,
                Close = k.Close,
                Volume = k.Volume
            }).ToList();


            var volumeSma = CalculateVolumeSma(quotes, 20);

            // Replace your current pivot detection with:
            var (pivotHighs, pivotLows) = FindPivotsOptimized(klines, _lookback);
            // var pivotHighs = FindPivotHighs(klines, _lookback, _lookback);
            // var pivotLows = FindPivotLows(klines, _lookback, _lookback);

            // //Console.WriteLine($"Found {pivotLows.Count} pivot lows and {pivotHighs.Count} pivot highs in historical data");

            // ADD DEBUG CODE HERE
            // //Console.WriteLine($"=== HISTORICAL VOLUME DEBUG ===");
            if (volumeSma.Count > 0)
            {
                // Check a few values to see the pattern
                for (int i = Math.Max(0, volumeSma.Count - 10); i < volumeSma.Count; i += 2)
                {
                    var smaValue = volumeSma[i].Sma;
                    var quoteIndex = quotes.FindIndex(q => q.Date == volumeSma[i].Date);
                    var actualVolume = quoteIndex >= 0 ? (double)quotes[quoteIndex].Volume : 0;

                    // //Console.WriteLine($"Index {i}: SMA={smaValue} vs Volume={actualVolume} (Ratio: {actualVolume / (smaValue ?? 1):F2})");
                }
            }
            // //Console.WriteLine("=== END HISTORICAL VOLUME DEBUG ===");

            // Reset breakout states for historical simulation
            _resistanceBreakoutActive = false;
            _supportBreakoutActive = false;
            _brokenResistanceLevel = 0;
            _brokenSupportLevel = 0;
            _entryTakenFromResistanceBreakout = false;
            _entryTakenFromSupportBreakout = false;

            for (int i = 2; i < klines.Count; i++)
            {
                var currentKline = klines[i];
                var prevKline = klines[i - 1];
                var currentVolume = currentKline.Volume;
                var prevVolume = prevKline.Volume;
                var avgVolume = volumeSma[i].Sma ?? 0;

                // FIX: Convert to decimal before comparison
                decimal highVolume = (decimal)(avgVolume * _volumeMultiplier);
                decimal lowVolume = (decimal)avgVolume;

                // Get most recent support/resistance levels available at this point in time
                var recentResistance = pivotHighs.LastOrDefault(p => p < currentKline.Close);
                var recentSupport = pivotLows.LastOrDefault(p => p > currentKline.Close);

                // Update support/resistance levels when new pivots are found
                if (recentSupport > 0)
                {
                    _supportBreakoutActive = false;
                    _brokenSupportLevel = 0;
                    _entryTakenFromSupportBreakout = false;
                }

                if (recentResistance > 0)
                {
                    _resistanceBreakoutActive = false;
                    _brokenResistanceLevel = 0;
                    _entryTakenFromResistanceBreakout = false;
                }

                // Check for breakouts
                if (recentResistance > 0 &&
                    currentKline.Close > recentResistance &&
                    currentVolume > highVolume &&
                    !_resistanceBreakoutActive)
                {
                    // //Console.WriteLine($"HISTORICAL RESISTANCE BREAKOUT - Close: {currentKline.Close}, Resistance: {recentResistance}, Volume: {currentVolume} > {highVolume}");
                    _resistanceBreakoutActive = true;
                    _brokenResistanceLevel = recentResistance;
                    _entryTakenFromResistanceBreakout = false;
                }

                if (recentSupport > 0 &&
                    currentKline.Close < recentSupport &&
                    currentVolume > highVolume &&
                    !_supportBreakoutActive)
                {
                    // //Console.WriteLine($"HISTORICAL SUPPORT BREAKOUT - Close: {currentKline.Close}, Support: {recentSupport}, Volume: {currentVolume} > {highVolume}");
                    _supportBreakoutActive = true;
                    _brokenSupportLevel = recentSupport;
                    _entryTakenFromSupportBreakout = false;
                }

                // Check for entries
                if (_resistanceBreakoutActive &&
                    currentKline.Low <= _brokenResistanceLevel &&
                    prevKline.Low > _brokenResistanceLevel && // Previous candle completely above
                    prevVolume < lowVolume &&
                    currentKline.Close > _brokenResistanceLevel &&
                    !_entryTakenFromResistanceBreakout &&
                    currentKline.Symbol != null)
                {
                    //Console.WriteLine($"HISTORICAL LONG ENTRY - Price: {currentKline.Close}, Broken Resistance: {_brokenResistanceLevel}");
                    await OrderManager.PlaceLongOrderAsync(currentKline.Symbol, currentKline.Close, "S/R-Breakout", currentKline.OpenTime);
                    _entryTakenFromResistanceBreakout = true;
                }

                if (_supportBreakoutActive &&
                    currentKline.High >= _brokenSupportLevel &&
                    prevKline.High < _brokenSupportLevel && // Previous candle completely below
                    prevVolume < lowVolume &&
                    currentKline.Close < _brokenSupportLevel &&
                    !_entryTakenFromSupportBreakout &&
                    currentKline.Symbol != null)
                {
                    //Console.WriteLine($"HISTORICAL SHORT ENTRY - Price: {currentKline.Close}, Broken Support: {_brokenSupportLevel}");
                    await OrderManager.PlaceShortOrderAsync(currentKline.Symbol, currentKline.Close, "S/R-Breakout", currentKline.OpenTime);
                    _entryTakenFromSupportBreakout = true;
                }

                // Check for invalidations
                if (_resistanceBreakoutActive && currentKline.High < _brokenResistanceLevel)
                {
                    //Console.WriteLine($"HISTORICAL Resistance breakout invalidated - High: {currentKline.High} < Resistance: {_brokenResistanceLevel}");
                    _resistanceBreakoutActive = false;
                    _brokenResistanceLevel = 0;
                    _entryTakenFromResistanceBreakout = false;
                }

                if (_supportBreakoutActive && currentKline.Low > _brokenSupportLevel)
                {
                    //Console.WriteLine($"HISTORICAL Support breakout invalidated - Low: {currentKline.Low} > Support: {_brokenSupportLevel}");
                    _supportBreakoutActive = false;
                    _brokenSupportLevel = 0;
                    _entryTakenFromSupportBreakout = false;
                }

                if (!string.IsNullOrEmpty(currentKline.Symbol))
                {
                    var currentPrices = new Dictionary<string, decimal> { { currentKline.Symbol, currentKline.Close } };
                    await OrderManager.CheckAndCloseTrades(currentPrices, currentKline.OpenTime);
                }
            }
        }

        private List<SmaResult> CalculateVolumeSma(List<Skender.Stock.Indicators.Quote> quotes, int period)
        {
            var results = new List<SmaResult>();
            
            for (int i = 0; i < quotes.Count; i++)
            {
                if (i < period - 1)
                {
                    // Not enough data for SMA
                    results.Add(new SmaResult 
                    { 
                        Date = quotes[i].Date, 
                        Sma = null 
                    });
                }
                else
                {
                    // Calculate SMA of volume
                    double sum = 0;
                    for (int j = 0; j < period; j++)
                    {
                        sum += (double)quotes[i - j].Volume;
                    }
                    double sma = sum / period;
                    
                    results.Add(new SmaResult 
                    { 
                        Date = quotes[i].Date, 
                        Sma = sma 
                    });
                }
            }
            
            return results;
        }

        private double GetIntervalMinutes(string interval)
        {
            switch (interval)
            {
                case "1m": return 1;
                case "3m": return 3;
                case "5m": return 5;
                case "15m": return 15;
                case "30m": return 30;
                case "1h": return 60;
                case "2h": return 120;
                case "4h": return 240;
                case "1d": return 1440;
                default: return 1; // default to 1 minute
            }
        }        

        // Add this helper class if needed
        public class SmaResult
        {
            public DateTime Date { get; set; }
            public double? Sma { get; set; }
        }        

        private void LogBreakoutSignal(string type, string symbol, decimal price, decimal level)
        {
            //Console.WriteLine($"****** S/R BREAKOUT ***************************");
            //Console.WriteLine($"{type} breakout on {symbol} @ {price} (Level: {level})");
            //Console.WriteLine($"Time: {DateTime.Now:HH:mm:ss}");
            //Console.WriteLine($"************************************************");
        }

        private void LogEntrySignal(string direction, string symbol, decimal price, decimal level)
        {
            //Console.WriteLine($"****** S/R ENTRY ******************************");
            //Console.WriteLine($"Go {direction} on {symbol} @ {price} (Retest of {level})");
            //Console.WriteLine($"Time: {DateTime.Now:HH:mm:ss}");
            if(direction == "LONG")
            {
                //Console.WriteLine("Entry condition: Resistance breakout + retest with low volume");
            }
            else
            {
                //Console.WriteLine("Entry condition: Support breakout + retest with low volume");
            }
            //Console.WriteLine($"************************************************");
        }

        // Request creation centralized in StrategyUtils

        private void HandleErrorResponse(string symbol, RestResponse response)
        {
            //Console.WriteLine($"Error for {symbol}: {response.ErrorMessage}");
            //Console.WriteLine($"Status Code: {response.StatusCode}");
            //Console.WriteLine($"Content: {response.Content}");
        }

        // Parsing helpers centralized in StrategyUtils
    }
}