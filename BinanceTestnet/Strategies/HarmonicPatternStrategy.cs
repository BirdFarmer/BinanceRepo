using BinanceTestnet.Models;
using BinanceTestnet.Config;
using System.IO;
using System.Collections.Concurrent;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using RestSharp;
using BinanceTestnet.Trading;

namespace BinanceTestnet.Strategies
{
    public class HarmonicPatternStrategy : StrategyBase
    {
        protected override bool SupportsClosedCandles => true;

        private const int MinLookback = 100;
        // Entry expiry: allow entries up to multiplier * patternLengthBars after D
        private const double PatternExpiryMultiplier = 1.2; // user requested 1.2
        // Max allowed distance (currentPrice vs D price) to still enter
        private const decimal MaxEntryPricePct = 0.05m; // user requested 5%

        public HarmonicPatternStrategy(RestClient client, string apiKey, OrderManager orderManager, Wallet wallet)
            : base(client, apiKey, orderManager, wallet)
        {
        }

        // CSV logging for detections (appends across runs)
        private static readonly string DetectionCsvPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "harmonic_detections.csv");
        private static readonly object CsvLock = new object();
        private void DumpDetectionCsv(string symbol, Tools.HarmonicPattern pattern, bool? isBullish, double confidence, double abxa, double bcab, double cdxa,
            DateTime? tx, decimal? px, DateTime? td, decimal? pd, decimal? signalPrice, decimal? priceDistPct, string result, string notes)
        {
            try
            {
                lock (CsvLock)
                {
                    bool writeHeader = !File.Exists(DetectionCsvPath);
                    using (var sw = new StreamWriter(DetectionCsvPath, append: true))
                    {
                        if (writeHeader)
                        {
                            sw.WriteLine("ts,symbol,pattern,direction,confidence,ab_xa,bc_ab,cd_xa,t_x,p_x,t_d,p_d,signal_price,price_dist, result,notes");
                        }
                        var line = string.Format("{0:u},{1},{2},{3},{4:F4},{5:F4},{6:F4},{7:F4},{8:u},{9},{10:u},{11},{12},{13},{14}",
                            DateTime.UtcNow,
                            symbol,
                            pattern,
                            isBullish.HasValue ? (isBullish.Value ? "BULL" : "BEAR") : "NA",
                            confidence,
                            abxa,
                            bcab,
                            cdxa,
                            tx ?? DateTime.MinValue,
                            px ?? 0m,
                            td ?? DateTime.MinValue,
                            pd ?? 0m,
                            signalPrice ?? 0m,
                            priceDistPct.HasValue ? priceDistPct.Value.ToString("P2") : "",
                            result + "," + (notes ?? "")
                        );
                        sw.WriteLine(line);
                    }
                }
            }
            catch
            {
                // Best-effort logging, do not fail strategy on CSV write errors
            }
        }

        // Track patterns that have already resulted in an entry to enforce "1 pattern -> 1 trade"
        // Key format: "SYMBOL|PATTERN|D_ISO|DPRICE" (uses D timestamp + price to uniquely identify)
        private readonly ConcurrentDictionary<string, bool> _tradedPatterns = new ConcurrentDictionary<string, bool>();

        private string BuildPatternKey(string symbol, Tools.HarmonicPattern pattern, DateTime? dTime, decimal? dPrice)
        {
            var dt = dTime.HasValue ? dTime.Value.ToString("o") : "nodt";
            var dp = dPrice.HasValue ? dPrice.Value.ToString("F8") : "nop";
            return $"{symbol}|{pattern}|{dt}|{dp}";
        }

        public override async Task RunAsync(string symbol, string interval)
        {
            try
            {
                var request = Helpers.StrategyUtils.CreateGet("/fapi/v1/klines", new Dictionary<string, string>
                {
                    { "symbol", symbol },
                    { "interval", interval },
                    { "limit", (MinLookback + 200).ToString() }
                });

                var response = await Client.ExecuteGetAsync(request);
                if (!response.IsSuccessful || response.Content == null)
                {
                    HandleErrorResponse(symbol, response);
                    return;
                }

                var klines = Helpers.StrategyUtils.ParseKlines(response.Content);
                if (klines == null || klines.Count < MinLookback)
                {
                    LogError($"Not enough klines data available for {symbol} ({klines?.Count ?? 0}).");
                    return;
                }

                var (signalKline, previousKline) = SelectSignalPair(klines);
                if (signalKline == null || previousKline == null) return;

                // Convert to quotes for detector
                var quotes = klines.Select(k => new BinanceTestnet.Models.Quote
                {
                    Date = DateTimeOffset.FromUnixTimeMilliseconds(k.OpenTime).UtcDateTime,
                    High = k.High,
                    Low = k.Low,
                    Close = k.Close
                }).ToList();

                // detection: use a small validation window to confirm point D (like Pine's t_b)
                int validationBars = 3;
                bool useTrendFilter = false;
                try
                {
                    var us = UserSettingsReader.Load();
                    validationBars = us.HarmonicValidationBars >= 1 ? us.HarmonicValidationBars : validationBars;
                    useTrendFilter = us.HarmonicUseTrendFilter;
                }
                catch { }

                var detection = Tools.HarmonicPatternDetector.Detect(quotes, pivotStrength: 3, validationBars: validationBars);

                if (detection == null || detection.Pattern == Tools.HarmonicPattern.None)
                {
                    // No pattern detected
                    return;
                }

                // Respect user-configured allowed patterns (if a user.settings.json is present)
                try
                {
                    var us = UserSettingsReader.Load();
                    bool allowed = detection.Pattern switch
                    {
                        Tools.HarmonicPattern.Gartley => us.HarmonicEnableGartley,
                        Tools.HarmonicPattern.Butterfly => us.HarmonicEnableButterfly,
                        Tools.HarmonicPattern.Bat => us.HarmonicEnableBat,
                        Tools.HarmonicPattern.Crab => us.HarmonicEnableCrab,
                        Tools.HarmonicPattern.Cypher => us.HarmonicEnableCypher,
                        Tools.HarmonicPattern.Shark => us.HarmonicEnableShark,
                        _ => true
                    };
                    if (!allowed)
                    {
                        DumpDetectionCsv(symbol, detection.Pattern, detection.IsBullish, detection.Confidence, detection.AbXa, detection.BcAb, detection.CdXa,
                            null, null, null, null, signalKline.Close, null, "skipped_pattern_disabled", detection.Notes);
                        return;
                    }
                }
                catch { /* best-effort: if settings can't be read, default to allowing patterns */ }

                // (No per-symbol cooldown enforced here; OrderManager already prevents duplicate active trades.)

                // quick references to pivot points (may be null)
                var pX = detection.Points != null && detection.Points.Count > 0 ? detection.Points.First() : default;
                var pD = detection.Points != null && detection.Points.Count > 0 ? detection.Points.Last() : default;
                DateTime? tX = detection.Points != null && detection.Points.Count > 0 ? detection.Points.First().time : (DateTime?)null;
                decimal? pXprice = detection.Points != null && detection.Points.Count > 0 ? detection.Points.First().price : (decimal?)null;
                DateTime? tD = detection.Points != null && detection.Points.Count > 0 ? detection.Points.Last().time : (DateTime?)null;
                decimal? pDprice = detection.Points != null && detection.Points.Count > 0 ? detection.Points.Last().price : (decimal?)null;

                // Simple trend filter: SMA(50) bias. When market is up (price > SMA50) avoid opening shorts.
                decimal smaPeriod = 50;
                decimal sma50 = quotes.Skip(Math.Max(0, quotes.Count - (int)smaPeriod)).Select(q => q.Close).Average();
                bool marketIsUp = signalKline.Close > sma50;

                // Per-pattern minimum confidence thresholds (raise Butterfly threshold to reduce overtrading of shorts)
                var minConfidenceByPattern = new Dictionary<Tools.HarmonicPattern, double>
                {
                    { Tools.HarmonicPattern.Gartley, 0.40 },
                    { Tools.HarmonicPattern.Butterfly, 0.60 },
                    { Tools.HarmonicPattern.Bat, 0.40 }
                };

                double requiredConfidence = minConfidenceByPattern.ContainsKey(detection.Pattern) ? minConfidenceByPattern[detection.Pattern] : 0.45;

                if (detection.Confidence < requiredConfidence)
                {
                    // Console.WriteLine($"Skipping {detection.Pattern} due to low confidence ({detection.Confidence:F3} < {requiredConfidence:F3})");
                    DumpDetectionCsv(symbol, detection.Pattern, detection.IsBullish, detection.Confidence, detection.AbXa, detection.BcAb, detection.CdXa,
                        tX, pXprice, tD, pDprice, signalKline.Close, null, "skipped_low_confidence", detection.Notes);
                    return;
                }

                // symmetric trend filtering: avoid shorts in uptrend and longs in downtrend (configurable)
                if (useTrendFilter)
                {
                    if (detection.IsBearish && marketIsUp)
                    {
                        DumpDetectionCsv(symbol, detection.Pattern, detection.IsBullish, detection.Confidence, detection.AbXa, detection.BcAb, detection.CdXa,
                            tX, pXprice, tD, pDprice, signalKline.Close, null, "skipped_trend", detection.Notes);
                        return;
                    }
                    if (detection.IsBullish && !marketIsUp)
                    {
                        DumpDetectionCsv(symbol, detection.Pattern, detection.IsBullish, detection.Confidence, detection.AbXa, detection.BcAb, detection.CdXa,
                            tX, pXprice, tD, pDprice, signalKline.Close, null, "skipped_trend", detection.Notes);
                        return;
                    }
                }

                // Debug output: print detection details so user can understand why trades are placed
                Console.WriteLine("====== Harmonic Detection ======");
                Console.WriteLine($"Symbol: {symbol}");
                Console.WriteLine($"Pattern: {detection.Pattern} | Direction: {(detection.IsBullish ? "BULLISH" : "BEARISH")} | Confidence: {detection.Confidence:F3}");
                Console.WriteLine($"Ratios: AB/XA={detection.AbXa:F3}, BC/AB={detection.BcAb:F3}, CD/XA={detection.CdXa:F3}");
                Console.WriteLine($"Notes: {detection.Notes}");
                Console.WriteLine("Points:");
                if (detection.Points != null)
                {
                    foreach (var p in detection.Points)
                    {
                        Console.WriteLine($"  {p.time:yyyy-MM-dd HH:mm} -> {p.price}");
                    }
                }
                Console.WriteLine("===============================");
                // Expiry checks: bar-based expiry (patternLengthBars * multiplier) and price proximity
                if (detection.Points != null && detection.Points.Count >= 2)
                {
                    // reuse outer pX/pD variables declared earlier

                    // helper: find closest index in quotes for a timestamp
                    int FindClosestIndex(List<BinanceTestnet.Models.Quote> qlist, DateTime t)
                    {
                        int idx = qlist.FindIndex(q => q.Date == t);
                        if (idx >= 0) return idx;
                        // fallback to nearest by absolute time difference
                        long bestDiff = long.MaxValue;
                        int bestIdx = -1;
                        for (int ii = 0; ii < qlist.Count; ii++)
                        {
                            var diff = Math.Abs((qlist[ii].Date - t).Ticks);
                            if (diff < bestDiff)
                            {
                                bestDiff = diff;
                                bestIdx = ii;
                            }
                        }
                        return bestIdx;
                    }

                    int idxX = FindClosestIndex(quotes, pX.time);
                    int idxD = FindClosestIndex(quotes, pD.time);
                        if (idxX >= 0 && idxD >= 0 && idxD > idxX)
                        {
                            int patternLengthBars = idxD - idxX;
                            int barsSinceD = (quotes.Count - 1) - idxD;
                            double allowedBars = patternLengthBars * PatternExpiryMultiplier;
                            if (barsSinceD > allowedBars)
                            {
                                DumpDetectionCsv(symbol, detection.Pattern, detection.IsBullish, detection.Confidence, detection.AbXa, detection.BcAb, detection.CdXa,
                                    tX, pXprice, tD, pDprice, signalKline.Close, null, "skipped_expired", detection.Notes);
                                return;
                            }
                        }

                    // price proximity check
                    decimal dPrice = pD.price;
                    decimal signalPrice = signalKline.Close;
                    decimal priceDistPct = dPrice == 0 ? 0 : Math.Abs(signalPrice - dPrice) / dPrice;
                    Console.WriteLine($"D at {pD.time:u} price={dPrice:F6}; signal time {DateTimeOffset.FromUnixTimeMilliseconds(signalKline.OpenTime).UtcDateTime:u}; ageBars={(quotes.Count-1) - (idxD>=0?idxD:-1)}; priceDist={priceDistPct:P2}");
                    if (priceDistPct > MaxEntryPricePct)
                    {
                        // Console.WriteLine($"Skipping {symbol}: signal price {signalPrice:F6} is {priceDistPct:P2} away from D {dPrice:F6} (allowed {MaxEntryPricePct:P2})");
                        DumpDetectionCsv(symbol, detection.Pattern, detection.IsBullish, detection.Confidence, detection.AbXa, detection.BcAb, detection.CdXa,
                            tX, pXprice, tD, pDprice, signalKline.Close, priceDistPct, "skipped_price_distance", detection.Notes);
                        return;
                    }
                    // Structural invalidation: if C was breached after D, skip the pattern
                    if (detection.Points != null && detection.Points.Count >= 3)
                    {
                        var pC = detection.Points[detection.Points.Count - 2];
                        // idxD already computed above
                        if (idxD >= 0)
                        {
                            bool broken = false;
                            for (int ii = idxD + 1; ii < quotes.Count; ii++)
                            {
                                if (detection.IsBearish)
                                {
                                    if (quotes[ii].High > pC.price)
                                    {
                                        broken = true; break;
                                    }
                                }
                                else
                                {
                                    if (quotes[ii].Low < pC.price)
                                    {
                                        broken = true; break;
                                    }
                                }
                            }
                            if (broken)
                            {
                                DumpDetectionCsv(symbol, detection.Pattern, detection.IsBullish, detection.Confidence, detection.AbXa, detection.BcAb, detection.CdXa,
                                    tX, pXprice, tD, pDprice, signalKline.Close, null, "skipped_broken_structure", detection.Notes);
                                return;
                            }
                        }
                    }
                }

                // Build a unique pattern key and skip if we've already taken an entry on this pattern
                var patternKeyLive = BuildPatternKey(symbol, detection.Pattern, tD, pDprice);
                if (_tradedPatterns.ContainsKey(patternKeyLive))
                {
                    DumpDetectionCsv(symbol, detection.Pattern, detection.IsBullish, detection.Confidence, detection.AbXa, detection.BcAb, detection.CdXa,
                        tX, pXprice, tD, pDprice, signalKline.Close, null, "skipped_already_traded", detection.Notes);
                    return;
                }

                // Decision: long for bullish patterns, short for bearish
                if (detection.IsBullish)
                {
                    await OrderManager.PlaceLongOrderAsync(symbol, signalKline.Close, $"Harmonic:{detection.Pattern}:{detection.Confidence:F2}", signalKline.OpenTime);
                    DumpDetectionCsv(symbol, detection.Pattern, detection.IsBullish, detection.Confidence, detection.AbXa, detection.BcAb, detection.CdXa,
                        tX, pXprice, tD, pDprice, signalKline.Close, null, "placed_long", detection.Notes);
                    // If an active harmonic trade for this symbol exists, mark the pattern as traded
                    try
                    {
                        var placed = OrderManager.GetActiveTrades().Any(t => t.Symbol == symbol && t.IsInTrade && t.Signal != null && t.Signal.StartsWith("Harmonic"));
                        if (placed)
                        {
                            _tradedPatterns[patternKeyLive] = true;
                        }
                    }
                    catch { }
                }
                else if (detection.IsBearish)
                {
                    await OrderManager.PlaceShortOrderAsync(symbol, signalKline.Close, $"Harmonic:{detection.Pattern}:{detection.Confidence:F2}", signalKline.OpenTime);
                    DumpDetectionCsv(symbol, detection.Pattern, detection.IsBullish, detection.Confidence, detection.AbXa, detection.BcAb, detection.CdXa,
                        tX, pXprice, tD, pDprice, signalKline.Close, null, "placed_short", detection.Notes);
                    try
                    {
                        var placed = OrderManager.GetActiveTrades().Any(t => t.Symbol == symbol && t.IsInTrade && t.Signal != null && t.Signal.StartsWith("Harmonic"));
                        if (placed)
                        {
                            _tradedPatterns[patternKeyLive] = true;
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                LogError($"Error processing {symbol}: {ex.Message}");
            }
        }

        public override async Task RunOnHistoricalDataAsync(IEnumerable<Kline> historicalData)
        {
            var dataList = historicalData.ToList();
            if (dataList.Count < MinLookback) return;

            var quotes = dataList.Select(k => new BinanceTestnet.Models.Quote
            {
                Date = DateTimeOffset.FromUnixTimeMilliseconds(k.OpenTime).UtcDateTime,
                High = k.High,
                Low = k.Low,
                Close = k.Close
            }).ToList();

            // Walk through historical bars and run detection at each step (simple backtest hook)
            for (int i = MinLookback; i < quotes.Count; i++)
            {
                var window = quotes.Take(i + 1).ToList();
                // Use validationBars from user settings (fall back to 3)
                int validationBars = 3;
                bool useTrendFilter = false;
                try
                {
                    var us = UserSettingsReader.Load();
                    validationBars = us.HarmonicValidationBars >= 1 ? us.HarmonicValidationBars : validationBars;
                    useTrendFilter = us.HarmonicUseTrendFilter;
                }
                catch { }

                var detection = Tools.HarmonicPatternDetector.Detect(window, pivotStrength: 3, validationBars: validationBars);
                var kline = dataList[i];

                if (detection != null && detection.Pattern != Tools.HarmonicPattern.None)
                {
                    // Respect user-configured allowed patterns for historical/backtest runs
                    try
                    {
                        var us = UserSettingsReader.Load();
                        bool allowed = detection.Pattern switch
                        {
                            Tools.HarmonicPattern.Gartley => us.HarmonicEnableGartley,
                            Tools.HarmonicPattern.Butterfly => us.HarmonicEnableButterfly,
                            Tools.HarmonicPattern.Bat => us.HarmonicEnableBat,
                            Tools.HarmonicPattern.Crab => us.HarmonicEnableCrab,
                            Tools.HarmonicPattern.Cypher => us.HarmonicEnableCypher,
                            Tools.HarmonicPattern.Shark => us.HarmonicEnableShark,
                            _ => true
                        };
                        if (!allowed)
                        {
                            var symSkip = kline.Symbol ?? string.Empty;
                            DumpDetectionCsv(symSkip, detection.Pattern, detection.IsBullish, detection.Confidence, detection.AbXa, detection.BcAb, detection.CdXa,
                                null, null, null, null, kline.Close, null, "skipped_pattern_disabled", detection.Notes);
                            continue;
                        }
                    }
                    catch { }
                    // ensure symbol is present
                    var sym = kline.Symbol ?? string.Empty;
                    if (string.IsNullOrEmpty(sym))
                    {
                        continue;
                    }
                    // quick pivot refs for CSV reporting
                    DateTime? htX = detection.Points != null && detection.Points.Count > 0 ? detection.Points.First().time : (DateTime?)null;
                    decimal? hpXprice = detection.Points != null && detection.Points.Count > 0 ? detection.Points.First().price : (decimal?)null;
                    DateTime? htD = detection.Points != null && detection.Points.Count > 0 ? detection.Points.Last().time : (DateTime?)null;
                    decimal? hpDprice = detection.Points != null && detection.Points.Count > 0 ? detection.Points.Last().price : (decimal?)null;

                    // trend filter + per-pattern confidence similar to live RunAsync path
                    decimal smaPeriod = 50;
                    decimal sma50 = window.Skip(Math.Max(0, window.Count - (int)smaPeriod)).Select(q => q.Close).Average();
                    bool marketIsUp = kline.Close > sma50;
                    var minConfidenceByPattern = new Dictionary<Tools.HarmonicPattern, double>
                    {
                        { Tools.HarmonicPattern.Gartley, 0.40 },
                        { Tools.HarmonicPattern.Butterfly, 0.60 },
                        { Tools.HarmonicPattern.Bat, 0.40 }
                    };
                    double requiredConfidence = minConfidenceByPattern.ContainsKey(detection.Pattern) ? minConfidenceByPattern[detection.Pattern] : 0.45;
                    if (detection.Confidence < requiredConfidence)
                    {
                        // Console.WriteLine($"[Hist] Skipping {detection.Pattern} at {kline.OpenTime} due to low confidence ({detection.Confidence:F3} < {requiredConfidence:F3})");
                        DumpDetectionCsv(sym, detection.Pattern, detection.IsBullish, detection.Confidence, detection.AbXa, detection.BcAb, detection.CdXa,
                            htX, hpXprice, htD, hpDprice, kline.Close, null, "skipped_low_confidence", detection.Notes);
                    }
                    else if (useTrendFilter && detection.IsBearish && marketIsUp)
                    {
                        DumpDetectionCsv(sym, detection.Pattern, detection.IsBullish, detection.Confidence, detection.AbXa, detection.BcAb, detection.CdXa,
                            htX, hpXprice, htD, hpDprice, kline.Close, null, "skipped_trend", detection.Notes);
                    }
                    else if (useTrendFilter && detection.IsBullish && !marketIsUp)
                    {
                        DumpDetectionCsv(sym, detection.Pattern, detection.IsBullish, detection.Confidence, detection.AbXa, detection.BcAb, detection.CdXa,
                            htX, hpXprice, htD, hpDprice, kline.Close, null, "skipped_trend", detection.Notes);
                    }
                    else
                    {
                        // historical/backtest expiry checks (bar-based using 'window')
                        if (detection.Points != null && detection.Points.Count >= 2)
                        {
                            var h_pX = detection.Points.First();
                            var h_pD = detection.Points.Last();

                            int FindClosestIndex(List<BinanceTestnet.Models.Quote> qlist, DateTime t)
                            {
                                int idx = qlist.FindIndex(q => q.Date == t);
                                if (idx >= 0) return idx;
                                long bestDiff = long.MaxValue;
                                int bestIdx = -1;
                                for (int ii = 0; ii < qlist.Count; ii++)
                                {
                                    var diff = Math.Abs((qlist[ii].Date - t).Ticks);
                                    if (diff < bestDiff)
                                    {
                                        bestDiff = diff;
                                        bestIdx = ii;
                                    }
                                }
                                return bestIdx;
                            }

                            int idxX = FindClosestIndex(window, h_pX.time);
                            int idxD = FindClosestIndex(window, h_pD.time);
                            if (idxX >= 0 && idxD >= 0 && idxD > idxX)
                            {
                                int patternLengthBars = idxD - idxX;
                                int barsSinceD = (window.Count - 1) - idxD;
                                double allowedBars = patternLengthBars * PatternExpiryMultiplier;
                                if (barsSinceD > allowedBars)
                                {
                                    DumpDetectionCsv(sym, detection.Pattern, detection.IsBullish, detection.Confidence, detection.AbXa, detection.BcAb, detection.CdXa,
                                        htX, hpXprice, htD, hpDprice, kline.Close, null, "skipped_expired", detection.Notes);
                                    continue;
                                }
                            }

                            decimal dPrice = h_pD.price;
                            decimal signalPrice = kline.Close;
                            decimal priceDistPct = dPrice == 0 ? 0 : Math.Abs(signalPrice - dPrice) / dPrice;
                            Console.WriteLine($"[Hist] D at {h_pD.time:u} price={dPrice:F6}; signal time {DateTimeOffset.FromUnixTimeMilliseconds(kline.OpenTime).UtcDateTime:u}; priceDist={priceDistPct:P2}");
                            if (priceDistPct > MaxEntryPricePct)
                            {
                                // Console.WriteLine($"[Hist] Skipping {sym}: signal price {signalPrice:F6} is {priceDistPct:P2} away from D {dPrice:F6} (allowed {MaxEntryPricePct:P2})");
                                DumpDetectionCsv(sym, detection.Pattern, detection.IsBullish, detection.Confidence, detection.AbXa, detection.BcAb, detection.CdXa,
                                    htX, hpXprice, htD, hpDprice, kline.Close, priceDistPct, "skipped_price_distance", detection.Notes);
                                continue;
                            }
                            // Structural invalidation (historical): if C was breached after D in the window, skip the pattern
                            if (detection.Points != null && detection.Points.Count >= 3)
                            {
                                var h_pC = detection.Points[detection.Points.Count - 2];
                                if (idxD >= 0)
                                {
                                    bool brokenHist = false;
                                    for (int ii = idxD + 1; ii < window.Count; ii++)
                                    {
                                        if (detection.IsBearish)
                                        {
                                            if (window[ii].High > h_pC.price)
                                            {
                                                brokenHist = true; break;
                                            }
                                        }
                                        else
                                        {
                                            if (window[ii].Low < h_pC.price)
                                            {
                                                brokenHist = true; break;
                                            }
                                        }
                                    }
                                    if (brokenHist)
                                    {
                                        DumpDetectionCsv(sym, detection.Pattern, detection.IsBullish, detection.Confidence, detection.AbXa, detection.BcAb, detection.CdXa,
                                            htX, hpXprice, htD, hpDprice, kline.Close, null, "skipped_broken_structure", detection.Notes);
                                        continue;
                                    }
                                }
                            }
                        }

                        // Historical: skip if this exact pattern (symbol + D time + price) already led to an entry
                        var patternKeyHist = BuildPatternKey(sym, detection.Pattern, htD, hpDprice);
                        if (_tradedPatterns.ContainsKey(patternKeyHist))
                        {
                            DumpDetectionCsv(sym, detection.Pattern, detection.IsBullish, detection.Confidence, detection.AbXa, detection.BcAb, detection.CdXa,
                                htX, hpXprice, htD, hpDprice, kline.Close, null, "skipped_already_traded", detection.Notes);
                            continue;
                        }

                        if (detection.IsBullish)
                        {
                            await OrderManager.PlaceLongOrderAsync(sym, kline.Close, $"Harmonic:{detection.Pattern}", kline.OpenTime);
                            DumpDetectionCsv(sym, detection.Pattern, detection.IsBullish, detection.Confidence, detection.AbXa, detection.BcAb, detection.CdXa,
                                htX, hpXprice, htD, hpDprice, kline.Close, null, "placed_long", detection.Notes);
                            try
                            {
                                var placed = OrderManager.GetActiveTrades().Any(t => t.Symbol == sym && t.IsInTrade && t.Signal != null && t.Signal.StartsWith("Harmonic"));
                                if (placed) _tradedPatterns[patternKeyHist] = true;
                            }
                            catch { }
                        }
                        else if (detection.IsBearish)
                        {
                            await OrderManager.PlaceShortOrderAsync(sym, kline.Close, $"Harmonic:{detection.Pattern}", kline.OpenTime);
                            DumpDetectionCsv(sym, detection.Pattern, detection.IsBullish, detection.Confidence, detection.AbXa, detection.BcAb, detection.CdXa,
                                htX, hpXprice, htD, hpDprice, kline.Close, null, "placed_short", detection.Notes);
                            try
                            {
                                var placed = OrderManager.GetActiveTrades().Any(t => t.Symbol == sym && t.IsInTrade && t.Signal != null && t.Signal.StartsWith("Harmonic"));
                                if (placed) _tradedPatterns[patternKeyHist] = true;
                            }
                            catch { }
                        }
                    }
                }

                if (!string.IsNullOrEmpty(kline.Symbol) && kline.Close > 0)
                {
                    var currentPrices = new Dictionary<string, decimal> { { kline.Symbol, kline.Close } };
                    await OrderManager.CheckAndCloseTrades(currentPrices, kline.OpenTime);
                }
            }
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
    }
}
