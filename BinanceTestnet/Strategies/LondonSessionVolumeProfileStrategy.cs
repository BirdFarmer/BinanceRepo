using BinanceTestnet.Models;
using BinanceTestnet.Strategies.VolumeProfile;
using BinanceTestnet.Enums;
using RestSharp;
using Newtonsoft.Json.Linq;
using System.Globalization;
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using BinanceTestnet.Trading;

namespace BinanceTestnet.Strategies
{
    public class LondonSessionVolumeProfileStrategy : StrategyBase, ISnapshotAwareStrategy
    {
        protected override bool SupportsClosedCandles => true;
        public override int RequiredHistory => 400;

        // Defaults (UTC) - may be overridden by user settings
        private TimeSpan _sessionStart = TimeSpan.FromHours(8); // 08:00
        private TimeSpan _sessionEnd = TimeSpan.FromHours(14).Add(TimeSpan.FromMinutes(30)); // 14:30
        private decimal _valueAreaPct = 0.70m;
        private string _breakCheckField = "Close"; // Close | High | Low
        private bool _useOrderBookVap = true;
        private bool _allowBothSides = false;
        private int _limitExpiryMinutes = 60;
        private int _buckets = 120;
        private decimal _pocSanityPercent = 2.0m; // percent of session range

        // Per-instance state
        private DateTime _lastSessionDate = DateTime.MinValue;
        // Track how many entries we've placed this session per symbol/side (allows configuring >1)
        // Make these static so the counters survive strategy instance recreation (watchers are static already).
        private static readonly ConcurrentDictionary<string, int> _longPlacedPerSymbol = new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, int> _shortPlacedPerSymbol = new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private int _maxEntriesPerSidePerSession = 1;
        // Cache computed session profiles per-symbol to avoid FRVP jitter across cycles
        private class SessionProfileCacheEntry
        {
            public DateTime SessionDate { get; set; }
            public decimal LH { get; set; }
            public decimal LL { get; set; }
            public decimal POC { get; set; }
            public decimal VAH { get; set; }
            public decimal VAL { get; set; }
        }

        // Public helper to clear in-memory session state (watchers and per-symbol counters).
        // Intended to be called by external orchestration (start/stop) to ensure no stale
        // watchers survive between runs or after runtime config changes.
        public static void ClearInMemorySessionState()
        {
            try
            {
                // Clear watchers
                foreach (var key in _watchers.Keys.ToList())
                {
                    _watchers.TryRemove(key, out _);
                }

                // Clear placement counters
                _longPlacedPerSymbol.Clear();
                _shortPlacedPerSymbol.Clear();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[London VP] Failed to clear in-memory session state: {ex.Message}");
            }
        }
        private readonly Dictionary<string, SessionProfileCacheEntry> _sessionProfileCache = new Dictionary<string, SessionProfileCacheEntry>(StringComparer.OrdinalIgnoreCase);
        // Watchers for touch-to-enter behavior: when a breakout is detected we mark the symbol
        // as LookingForLong/Short with a target price and expiry; a later candle that touches
        // the target will execute a market entry.
        private class EntryWatcher
        {
            public decimal Target { get; set; }
            public bool IsLong { get; set; }
            public DateTime Created { get; set; }
            public DateTime Expiry { get; set; }
            public long SignalTimestamp { get; set; }
        }
        // Shared, thread-safe watchers map so watchers survive strategy instance recreation
        private static readonly ConcurrentDictionary<string, EntryWatcher> _watchers = new ConcurrentDictionary<string, EntryWatcher>(StringComparer.OrdinalIgnoreCase);
        private decimal _scanDurationHours = 4.0m;
        // Debug toggle (can be set by user settings)
        private bool _enableDebug = false;
        // The risk ratio used to compute TP when using POC as stop.
        private decimal _pocRiskRatio = 2.0m;
        // When false, do not allow entries triggered after sessionEnd + _scanDurationHours.
        // When true (default), existing watchers may still execute after the scan window.
        private bool _allowEntriesAfterScanWindow = true;

        private void D(string msg)
        {
            if (_enableDebug) Console.WriteLine(msg);
        }

        public LondonSessionVolumeProfileStrategy(RestClient client, string apiKey, OrderManager orderManager, Wallet wallet) : base(client, apiKey, orderManager, wallet)
        {
            // Load user settings (if available) to override defaults
            try
            {
                var dto = BinanceTestnet.Config.UserSettingsReader.Load();
                if (dto != null)
                {
                    _breakCheckField = dto.LondonBreakCheck ?? _breakCheckField;
                    if (TimeSpan.TryParse(dto.LondonSessionStart, out var ss)) _sessionStart = ss;
                    if (TimeSpan.TryParse(dto.LondonSessionEnd, out var se)) _sessionEnd = se;
                    if (dto.LondonValueAreaPercent > 0) _valueAreaPct = dto.LondonValueAreaPercent / 100m;
                    _useOrderBookVap = dto.LondonUseOrderBookVap;
                    _allowBothSides = dto.LondonAllowBothSides;
                    _limitExpiryMinutes = dto.LondonLimitExpiryMinutes;
                    if (dto.LondonBuckets > 0) _buckets = dto.LondonBuckets;
                    if (dto.LondonPocSanityPercent > 0) _pocSanityPercent = dto.LondonPocSanityPercent;
                    if (dto.LondonScanDurationHours > 0) _scanDurationHours = dto.LondonScanDurationHours;
                    if (dto.LondonMaxEntriesPerSidePerSession > 0) _maxEntriesPerSidePerSession = dto.LondonMaxEntriesPerSidePerSession;
                    if (dto.LondonPocRiskRatio > 0) _pocRiskRatio = dto.LondonPocRiskRatio;
                    // Seed the runtime flag so strategies running in this process will pick up the persisted value.
                    BinanceTestnet.Strategies.Helpers.StrategyRuntimeConfig.LondonUsePocAsStop = dto.LondonUsePocAsStop;
                    if (dto.LondonAllowEntriesAfterScanWindow == false) _allowEntriesAfterScanWindow = false;
                    // Enable/disable verbose strategy logging
                    _enableDebug = dto.LondonEnableDebug;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[London VP] Failed to load user settings: {ex.Message}");
            }
        }

        public override async Task RunAsync(string symbol, string interval)
        {
            try
            {
                var request = Helpers.StrategyUtils.CreateGet("/fapi/v1/klines", new Dictionary<string,string>
                {
                    {"symbol", symbol},
                    {"interval", interval},
                    {"limit", "800"}
                });

                var response = await Client.ExecuteGetAsync(request);
                if (response.IsSuccessful && response.Content != null)
                {
                    var klines = Helpers.StrategyUtils.ParseKlines(response.Content);
                    if (klines != null && klines.Count > 0)
                    {
                        await ProcessKlinesAsync(symbol, klines);
                    }
                }
                else
                {
                    HandleErrorResponse(symbol, response);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"London VP error for {symbol}: {ex.Message}");
            }
        }

        public async Task RunAsyncWithSnapshot(string symbol, string interval, Dictionary<string, List<Kline>> snapshot)
        {
            try
            {
                List<Kline> klines = null;
                if (snapshot != null && snapshot.TryGetValue(symbol, out var s) && s != null && s.Count > 0)
                {
                    klines = s;
                }

                if (klines == null)
                {
                    var request = Helpers.StrategyUtils.CreateGet("/fapi/v1/klines", new Dictionary<string,string>
                    {
                        {"symbol", symbol},
                        {"interval", interval},
                        {"limit", "800"}
                    });

                    var response = await Client.ExecuteGetAsync(request);
                    if (response.IsSuccessful && response.Content != null)
                    {
                        klines = Helpers.StrategyUtils.ParseKlines(response.Content);
                    }
                    else
                    {
                        HandleErrorResponse(symbol, response);
                        return;
                    }
                }

                if (klines != null && klines.Count > 0)
                {
                    await ProcessKlinesAsync(symbol, klines);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"London VP snapshot error for {symbol}: {ex.Message}");
            }
        }

        public override Task RunOnHistoricalDataAsync(IEnumerable<Kline> historicalData)
        {
            // Backtest runner (per-symbol). This mirrors live behaviour but forces FRVP
            // as the source of POC/VAH/VAL and uses watcher expiry from settings (minutes).
            return RunHistoricalAsyncInternal(historicalData);
        }

        private async Task RunHistoricalAsyncInternal(IEnumerable<Kline> historicalData)
        {
            var klines = historicalData?.OrderBy(k => k.OpenTime).ToList();
            if (klines == null || klines.Count == 0) return;

            string symbol = klines.First().Symbol ?? "";

            // Local per-backtest state (do not touch static runtime watchers)
            var localWatchers = new Dictionary<string, EntryWatcher>(StringComparer.OrdinalIgnoreCase);
            int longPlaced = 0;
            int shortPlaced = 0;

            // Helper to get DateTime from kline
            DateTime KlineCloseUtc(Kline k) => DateTimeOffset.FromUnixTimeMilliseconds(k.CloseTime).UtcDateTime;

            // Build session windows over the date range
            var firstDate = KlineCloseUtc(klines.First()).Date.AddDays(-1);
            var lastDate = KlineCloseUtc(klines.Last()).Date.AddDays(1);

            for (var d = firstDate; d <= lastDate; d = d.AddDays(1))
            {
                // compute the canonical session window for this date
                var reference = d.Add(_sessionEnd);
                var (sessionDate, sessionStart, sessionEnd) = BinanceTestnet.Strategies.Helpers.SessionTimeHelper.GetSessionWindow(reference, _sessionStart, _sessionEnd);

                // collect session candles
                var sessionKlines = klines.Where(k => {
                    var t = KlineCloseUtc(k);
                    return t >= sessionStart && t <= sessionEnd;
                }).ToList();

                if (sessionKlines == null || sessionKlines.Count == 0) continue;

                // compute FRVP from session candles (force FRVP)
                var profile = FixedRangeVolumeProfileCalculator.BuildFromKlines(sessionKlines, buckets: _buckets, valueAreaPct: _valueAreaPct);
                var poc = profile.POC;
                var vah = profile.VAH;
                var val = profile.VAL;
                var LH = sessionKlines.Max(k => k.High);
                var LL = sessionKlines.Min(k => k.Low);

                // Prepare all post-session candles (chronological) from the whole klines list
                var postSessionKlines = klines.Where(k => KlineCloseUtc(k) > sessionEnd).OrderBy(k => k.OpenTime).ToList();
                if (postSessionKlines == null || postSessionKlines.Count == 0) continue;

                // iterate through post-session candles, creating watchers on breakout and firing on later touches
                foreach (var post in postSessionKlines)
                {
                    var postClose = KlineCloseUtc(post);
                    // compute cutoff for allowing new watchers/triggers: sessionEnd + scanDurationHours
                    var scanCutoff = sessionEnd.AddHours((double)_scanDurationHours);

                    // expire existing watcher if present
                    if (localWatchers.TryGetValue(symbol, out var existingWatcher))
                    {
                        if (existingWatcher.Expiry != DateTime.MaxValue && postClose > existingWatcher.Expiry)
                        {
                            localWatchers.Remove(symbol);
                        }
                        else
                        {
                            // Only trigger on subsequent candles (postClose > signal time)
                            var signalTime = DateTimeOffset.FromUnixTimeMilliseconds(existingWatcher.SignalTimestamp).UtcDateTime;
                            // If we are past the configured scan cutoff and entries after scan window are disabled,
                            // stop this watcher and skip triggering.
                            if (postClose > scanCutoff && !_allowEntriesAfterScanWindow)
                            {
                                D($"[London VP] Post-session time {postClose:O} is after scan cutoff {scanCutoff:O} and entries after scan window are disabled. Removing watcher for {symbol}.");
                                localWatchers.Remove(symbol);
                                continue;
                            }

                            if (postClose > signalTime)
                            {
                                bool rangeIncludes = post.High >= existingWatcher.Target && post.Low <= existingWatcher.Target;
                                if (rangeIncludes)
                                {
                                    // Execute market entry at this candle's Close and set SL=POC, TP=2:1 RR
                                    var entryPrice = post.Close;
                                    var risk = Math.Abs(entryPrice - poc);
                                    if (risk > 0m)
                                    {
                                        if (existingWatcher.IsLong)
                                        {
                                                if (BinanceTestnet.Strategies.Helpers.StrategyRuntimeConfig.LondonUsePocAsStop)
                                                {
                                                    var tp = entryPrice + _pocRiskRatio * risk;
                                                    await OrderManager.PlaceLongOrderAsync(symbol, entryPrice, "London VP - Backtest", post.CloseTime, tp, poc);
                                                }
                                                else
                                                {
                                                    await OrderManager.PlaceLongOrderAsync(symbol, entryPrice, "London VP - Backtest", post.CloseTime);
                                                }
                                            longPlaced++;
                                        }
                                        else
                                        {
                                            if (BinanceTestnet.Strategies.Helpers.StrategyRuntimeConfig.LondonUsePocAsStop)
                                            {
                                                var tp = entryPrice - _pocRiskRatio * risk;
                                                await OrderManager.PlaceShortOrderAsync(symbol, entryPrice, "London VP - Backtest", post.CloseTime, tp, poc);
                                            }
                                            else
                                            {
                                                await OrderManager.PlaceShortOrderAsync(symbol, entryPrice, "London VP - Backtest", post.CloseTime);
                                            }
                                            shortPlaced++;
                                        }
                                    }

                                    localWatchers.Remove(symbol);
                                }
                            }
                        }
                    }

                    // If we're already watching this symbol, continue (don't create duplicate watcher)
                    if (localWatchers.ContainsKey(symbol))
                    {
                        // still call check/close to advance any open trades
                        var prices = new Dictionary<string, decimal> { { symbol, post.Close } };
                        await OrderManager.CheckAndCloseTrades(prices, post.CloseTime);
                        continue;
                    }

                    // If this post-session candle is past the configured scan window, skip creating new watchers
                    var postScanCutoff = sessionEnd.AddHours((double)_scanDurationHours);
                    if (postClose > postScanCutoff)
                    {
                        D($"[London VP] Post-session candle {postClose:O} is after sessionEnd+scanDuration ({postScanCutoff:O}); skipping watcher creation for {symbol}.");
                        // still advance trade checks for this candle and move on
                        var pricesSkip = new Dictionary<string, decimal> { { symbol, post.Close } };
                        await OrderManager.CheckAndCloseTrades(pricesSkip, post.CloseTime);
                        continue;
                    }

                    // Create new watcher on breakout condition (current post candle only)
                    bool isLongBreak = false;
                    bool isShortBreak = false;
                    switch ((_breakCheckField ?? "Close").ToLowerInvariant())
                    {
                        case "high":
                            isLongBreak = post.High > LH;
                            isShortBreak = post.Low < LL;
                            break;
                        case "low":
                            isLongBreak = post.Low > LH;
                            isShortBreak = post.High < LL;
                            break;
                        default:
                            isLongBreak = post.Close > LH;
                            isShortBreak = post.Close < LL;
                            break;
                    }

                    // Respect per-symbol caps (we're per-symbol so these are simple counters)
                    if (isLongBreak)
                    {
                        if (!_allowBothSides && shortPlaced > 0)
                        {
                            // don't create long if a short already placed and both-sides not allowed
                        }
                        else if (longPlaced >= _maxEntriesPerSidePerSession)
                        {
                            // reached per-symbol cap
                        }
                        else
                        {
                            var longEntry = (LH + vah) / 2m;
                            var watcher = new EntryWatcher
                            {
                                Target = longEntry,
                                IsLong = true,
                                Created = postClose,
                                Expiry = (_limitExpiryMinutes <= 0) ? DateTime.MaxValue : postClose.AddMinutes(_limitExpiryMinutes),
                                SignalTimestamp = post.CloseTime
                            };
                            localWatchers[symbol] = watcher;
                            D($"[London VP] [Backtest] LONG watcher created for {symbol}: LH={LH}, VAH={vah}, POC={poc}, target={longEntry}, expiry={watcher.Expiry:O}");
                        }
                    }

                    if (isShortBreak)
                    {
                        if (!_allowBothSides && longPlaced > 0)
                        {
                            // don't create short if a long already placed and both-sides not allowed
                        }
                        else if (shortPlaced >= _maxEntriesPerSidePerSession)
                        {
                            // reached per-symbol cap
                        }
                        else
                        {
                            var shortEntry = (LL + val) / 2m;
                            var watcher = new EntryWatcher
                            {
                                Target = shortEntry,
                                IsLong = false,
                                Created = postClose,
                                Expiry = (_limitExpiryMinutes <= 0) ? DateTime.MaxValue : postClose.AddMinutes(_limitExpiryMinutes),
                                SignalTimestamp = post.CloseTime
                            };
                            localWatchers[symbol] = watcher;
                            D($"[London VP] [Backtest] SHORT watcher created for {symbol}: LL={LL}, VAL={val}, POC={poc}, target={shortEntry}, expiry={watcher.Expiry:O}");
                        }
                    }

                    // Always advance trade checks for this candle
                    var currentPrices = new Dictionary<string, decimal> { { symbol, post.Close } };
                    await OrderManager.CheckAndCloseTrades(currentPrices, post.CloseTime);
                }
            }
        }

        private async Task ProcessKlinesAsync(string symbol, List<Kline> klines)
        {
            // Determine session window using helper (handles previous-day when reference is before session start)
            var latest = klines.Last();
            var latestTime = DateTimeOffset.FromUnixTimeMilliseconds(latest.CloseTime).UtcDateTime;
            var (sessionDate, sessionStart, sessionEnd) = BinanceTestnet.Strategies.Helpers.SessionTimeHelper.GetSessionWindow(latestTime, _sessionStart, _sessionEnd);

            // Reset per-session state when moving to a new session day
            if (_lastSessionDate.Date != sessionDate.Date)
            {
                _lastSessionDate = sessionDate;
                _longPlacedPerSymbol.Clear();
                _shortPlacedPerSymbol.Clear();
            }

            // Collect session klines between sessionStart (inclusive) and sessionEnd (inclusive)
            var sessionKlines = klines.Where(k => {
                var t = DateTimeOffset.FromUnixTimeMilliseconds(k.OpenTime).UtcDateTime;
                return t >= sessionStart && t <= sessionEnd;
            }).ToList();

            D($"[London VP] Session window for {symbol}: {sessionStart:O} - {sessionEnd:O}. Collected {sessionKlines.Count} candles.");

            if (sessionKlines == null || sessionKlines.Count == 0)
            {
                D($"[London VP] No session candles found for {symbol} on {sessionDate:yyyy-MM-dd}.");
                return;
            }

            // Use cached session profile when available for this symbol+sessionDate to avoid
            // small FRVP changes across cycles caused by variable kline windows.
            SessionProfileCacheEntry cacheEntry = null;

            // Declare variables up-front to avoid C# scope shadowing issues
            decimal LH, LL, poc, vah, val;

            if (_sessionProfileCache.TryGetValue(symbol, out var existingCache) && existingCache.SessionDate.Date == sessionDate.Date)
            {
                cacheEntry = existingCache;
                D($"[London VP] Using cached session profile for {symbol} date={sessionDate:yyyy-MM-dd}");
                LH = cacheEntry.LH;
                LL = cacheEntry.LL;
                poc = cacheEntry.POC;
                vah = cacheEntry.VAH;
                val = cacheEntry.VAL;
            }
            else
            {
                LH = sessionKlines.Max(k => k.High);
                LL = sessionKlines.Min(k => k.Low);

                // Compute fixed-range volume profile (FRVP) from session klines.
                // Using FRVP as primary source (OHLCV-distribution). Order-book based approaches were deprecated.
                var profile = FixedRangeVolumeProfileCalculator.BuildFromKlines(sessionKlines, buckets: _buckets, valueAreaPct: _valueAreaPct);
                poc = profile.POC;
                vah = profile.VAH;
                val = profile.VAL;

                D($"[London VP] Using FRVP for {symbol}: POC={poc} VAH={vah} VAL={val} (buckets={_buckets})");
                D($"[London VP] {symbol} LH={LH} LL={LL} POC={poc} VAH={vah} VAL={val} (VA%={_valueAreaPct:P0})");

                cacheEntry = new SessionProfileCacheEntry
                {
                    SessionDate = sessionDate,
                    LH = LH,
                    LL = LL,
                    POC = poc,
                    VAH = vah,
                    VAL = val
                };
                _sessionProfileCache[symbol] = cacheEntry;
            }

            // POC sanity: if POC is significantly outside the session high/low, clamp it to session range.
            try
            {
                var sessionRange = LH - LL;
                if (sessionRange > 0)
                {
                    var margin = sessionRange * (_pocSanityPercent / 100m);
                    if (poc < LL - margin || poc > LH + margin)
                    {
                        D($"[London VP] POC {poc} outside session range [{LL},{LH}] by >{_pocSanityPercent}% margin. Clamping to session range.");
                        poc = Math.Min(Math.Max(poc, LL), LH);
                    }
                }
            }
            catch { /* non-fatal */ }

            // Log kline time range for debugging
                try
                {
                    var firstK = klines.First();
                    var firstOpen = DateTimeOffset.FromUnixTimeMilliseconds(firstK.OpenTime).UtcDateTime;
                    var lastK = klines.Last();
                    var lastOpen = DateTimeOffset.FromUnixTimeMilliseconds(lastK.OpenTime).UtcDateTime;
                    var lastClose = DateTimeOffset.FromUnixTimeMilliseconds(lastK.CloseTime).UtcDateTime;
                    D($"[London VP] Klines for {symbol}: total={klines.Count}, firstOpen={firstOpen:yyyy-MM-ddTHH:mm'Z'}, lastOpen={lastOpen:yyyy-MM-ddTHH:mm'Z'}, lastClose={lastClose:yyyy-MM-ddTHH:mm'Z'}");
                }
            catch { /* ignore logging errors */ }

            // Scan all post-session candles (chronological) and trigger on the first close-based breakout.
            var postSessionKlines = klines.Where(k => DateTimeOffset.FromUnixTimeMilliseconds(k.OpenTime).UtcDateTime > sessionEnd)
                                        .OrderBy(k => k.OpenTime)
                                        .ToList();

            if (postSessionKlines == null || postSessionKlines.Count == 0)
            {
                // give more detail why none found
                try
                {
                    var lastK = klines.Last();
                    var lastClose = DateTimeOffset.FromUnixTimeMilliseconds(lastK.CloseTime).UtcDateTime;
                    D($"[London VP] No post-session candle found for {symbol}. Latest kline close={lastClose:O}, sessionEnd={sessionEnd:O}");
                }
                catch { }
                D($"[London VP] No post-session candles yet for {symbol}, waiting.");
                return;
            }

            // If not allowing both sides, and one side already placed (global per-session), skip entire symbol
                // Note: Per-symbol limits and allow-both-sides behavior are enforced when creating watchers below.
                // We do not skip scanning other symbols here.

            // Only evaluate the most recent post-session candle (current candle).
            var post = postSessionKlines.OrderBy(k => k.OpenTime).Last();
            var postOpenTs = DateTimeOffset.FromUnixTimeMilliseconds(post.OpenTime).UtcDateTime;
            var postCloseTs = DateTimeOffset.FromUnixTimeMilliseconds(post.CloseTime).UtcDateTime;
            D($"[London VP] Evaluating current post-session candle for {symbol}: openTs={postOpenTs:yyyy-MM-ddTHH:mm'Z'}, closeTs={postCloseTs:yyyy-MM-ddTHH:mm'Z'}, O={post.Open}, H={post.High}, L={post.Low}, C={post.Close}, V={post.Volume}");

            // Compute cutoff for allowing new watchers/triggers: sessionEnd + scanDurationHours
            var scanCutoff = sessionEnd.AddHours((double)_scanDurationHours);

            // Check for existing watcher (touch-to-enter) for this symbol and expire or trigger it
            if (_watchers.TryGetValue(symbol, out var existingWatcher))
            {
                // If watcher expired, remove it
                    if (postCloseTs > existingWatcher.Expiry)
                {
                    D($"[London VP] Watcher for {symbol} expired (target={existingWatcher.Target}) at {existingWatcher.Expiry:O}. Removing.");
                    _watchers.TryRemove(symbol, out _);
                }
                else
                {
                    // Always emit compact watcher state each cycle for diagnostics
                    DateTime signalTime;
                    try
                    {
                        signalTime = DateTimeOffset.FromUnixTimeMilliseconds(existingWatcher.SignalTimestamp).UtcDateTime;
                    }
                    catch
                    {
                        signalTime = DateTime.MinValue;
                    }

                    // If we are past the configured scan cutoff and entries after scan window are disabled,
                    // remove the watcher and skip further processing.
                    if (postCloseTs > scanCutoff && !_allowEntriesAfterScanWindow)
                    {
                        D($"[London VP] Post-session time {postCloseTs:O} is after scan cutoff {scanCutoff:O} and entries after scan window are disabled. Removing watcher for {symbol}.");
                        _watchers.TryRemove(symbol, out _);
                        return;
                    }

                    // Only trigger if this current candle is strictly later than the signal (require subsequent candle)
                    var signalTimeCheck = DateTimeOffset.FromUnixTimeMilliseconds(existingWatcher.SignalTimestamp).UtcDateTime;
                    if (postCloseTs > signalTimeCheck)
                    {
                        // Evaluate trigger and provide granular non-trigger reasons for debugging
                        if (existingWatcher.IsLong)
                        {
                            // Trigger if the current candle's range includes the target (captures wicks and bodies)
                            bool rangeIncludes = post.High >= existingWatcher.Target && post.Low <= existingWatcher.Target;
                            if (rangeIncludes)
                            {
                                D($"[London VP] Current candle (after signal) RANGE-INCLUDE LONG trigger for {symbol} (high>={existingWatcher.Target} && low<={existingWatcher.Target}). Entering market.");
                                try
                                {
                                    var entryPrice = post.Close;
                                    var risk = Math.Abs(entryPrice - poc);
                                    if (risk <= 0m)
                                    {
                                        D($"[London VP] Skipping LONG touch-entry for {symbol}: computed risk ({risk}) <= 0 (entry={entryPrice}, poc={poc}).");
                                    }
                                    else
                                    {
                                        if (BinanceTestnet.Strategies.Helpers.StrategyRuntimeConfig.LondonUsePocAsStop)
                                        {
                                            var tp = entryPrice + _pocRiskRatio * risk;
                                            await OrderManager.PlaceLongOrderAsync(symbol, entryPrice, "London VP - TouchEntry", post.CloseTime, tp, poc);
                                        }
                                        else
                                        {
                                            await OrderManager.PlaceLongOrderAsync(symbol, entryPrice, "London VP - TouchEntry", post.CloseTime);
                                        }
                                        Console.WriteLine($"[London VP] Market LONG entered for {symbol} at approx {entryPrice} SL(POC)={poc}");
                                        _longPlacedPerSymbol.AddOrUpdate(symbol, 1, (_, old) => old + 1);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"[London VP] Touch-entry error (LONG) for {symbol}: {ex.Message}");
                                }
                                _watchers.TryRemove(symbol, out _);
                            }
                            else
                            {
                                D($"[London VP] LONG watcher for {symbol} did not trigger: post.H={post.High}, post.L={post.Low}, target={existingWatcher.Target}, created={(signalTime==DateTime.MinValue?"n/a":signalTime.ToString("yyyy-MM-ddTHH:mm'Z'"))}, expiry={existingWatcher.Expiry:yyyy-MM-ddTHH:mm'Z'}");
                            }
                        }
                        else
                        {
                            bool rangeIncludes = post.High >= existingWatcher.Target && post.Low <= existingWatcher.Target;
                            if (rangeIncludes)
                            {
                                D($"[London VP] Current candle (after signal) RANGE-INCLUDE SHORT trigger for {symbol} (high>={existingWatcher.Target} && low<={existingWatcher.Target}). Entering market.");
                                try
                                {
                                    var entryPrice = post.Close;
                                    var risk = Math.Abs(entryPrice - poc);
                                    if (risk <= 0m)
                                    {
                                        D($"[London VP] Skipping SHORT touch-entry for {symbol}: computed risk ({risk}) <= 0 (entry={entryPrice}, poc={poc}).");
                                    }
                                    else
                                    {
                                            if (BinanceTestnet.Strategies.Helpers.StrategyRuntimeConfig.LondonUsePocAsStop)
                                            {
                                                var tp = entryPrice - _pocRiskRatio * risk;
                                                await OrderManager.PlaceShortOrderAsync(symbol, entryPrice, "London VP - TouchEntry", post.CloseTime, tp, poc);
                                            }
                                            else
                                            {
                                                await OrderManager.PlaceShortOrderAsync(symbol, entryPrice, "London VP - TouchEntry", post.CloseTime);
                                            }
                                            Console.WriteLine($"[London VP] Market SHORT entered for {symbol} at approx {entryPrice} SL(POC)={poc}");
                                        _shortPlacedPerSymbol.AddOrUpdate(symbol, 1, (_, old) => old + 1);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"[London VP] Touch-entry error (SHORT) for {symbol}: {ex.Message}");
                                }
                                _watchers.TryRemove(symbol, out _);
                            }
                            else
                            {
                                D($"[London VP] SHORT watcher for {symbol} did not trigger: post.H={post.High}, post.L={post.Low}, target={existingWatcher.Target}, created={(signalTime==DateTime.MinValue?"n/a":signalTime.ToString("yyyy-MM-ddTHH:mm'Z'"))}, expiry={existingWatcher.Expiry:yyyy-MM-ddTHH:mm'Z'}");
                            }
                        }
                    }
                    else
                    {
                        D($"[London VP] Existing watcher for {symbol} present but current candle is not later than signal; skipping trigger.");
                    }
                }
            }

            // If we are already watching this symbol (watcher present and not expired), do not attempt new breakouts.
            // This prevents creating duplicate triggers for the same breakout across successive cycles.
            if (_watchers.ContainsKey(symbol))
            {
                D($"[London VP] Already watching {symbol}, skipping breakout detection for this cycle.");
                return;
            }

            // No global stop; per-symbol limits and allow-both-sides are enforced below when creating watchers.

            // Determine breakout on the current candle only according to configured break-check field
            bool isLongBreak = false;
            bool isShortBreak = false;
            switch ((_breakCheckField ?? "Close").ToLowerInvariant())
            {
                case "high":
                    isLongBreak = post.High > LH;
                    isShortBreak = post.Low < LL;
                    break;
                case "low":
                    isLongBreak = post.Low > LH;
                    isShortBreak = post.High < LL;
                    break;
                default:
                    isLongBreak = post.Close > LH;
                    isShortBreak = post.Close < LL;
                    break;
            }

            // If this post-session candle is past the configured scan window, skip creating new watchers
            if (postCloseTs > scanCutoff)
            {
                D($"[London VP] Post-session candle {postCloseTs:O} is after sessionEnd+scanDuration ({scanCutoff:O}); skipping watcher creation for {symbol}.");
                return;
            }

            // Long breakout: evaluate only for the current candle
            if (isLongBreak)
            {
                decimal longEntry = (LH + vah) / 2m;
                long timestamp = post.CloseTime;
                try
                {
                    var risk = Math.Abs(longEntry - poc);
                    if (risk <= 0m)
                    {
                        D($"[London VP] Skipping LONG for {symbol}: computed risk ({risk}) <= 0 (entry={longEntry}, poc={poc}).");
                    }
                    else
                    {
                        var currentLongs = _longPlacedPerSymbol.TryGetValue(symbol, out var lc) ? lc : 0;
                        var currentShorts = _shortPlacedPerSymbol.TryGetValue(symbol, out var sc) ? sc : 0;
                        if (!_allowBothSides && currentShorts > 0)
                        {
                            D($"[London VP] LONG breakout ignored for {symbol}: allowBothSides=false and short already placed for this symbol.");
                        }
                        else if (currentLongs >= _maxEntriesPerSidePerSession)
                        {
                            D($"[London VP] LONG breakout ignored for {symbol}: reached per-symbol long cap ({currentLongs}/{_maxEntriesPerSidePerSession}).");
                        }
                        else
                        {
                            // On breakout, do NOT enter on the same candle. Always create a watcher for subsequent touches.
                            if (!_watchers.ContainsKey(symbol))
                            {
                                // Dump the last few post-session candles for debugging (helps find 11:51/11:52)
                                try
                                {
                                    const int dumpCount = 6;
                                    var recent = postSessionKlines.OrderBy(k => k.OpenTime).Skip(Math.Max(0, postSessionKlines.Count - dumpCount)).ToList();
                                    D($"[London VP] Dumping last {recent.Count} post-session candles for {symbol} (most recent last):");
                                    foreach (var ck in recent)
                                    {
                                        var ot = DateTimeOffset.FromUnixTimeMilliseconds(ck.OpenTime).UtcDateTime;
                                        D($"[London VP]  Candle {ot:O} O={ck.Open}, H={ck.High}, L={ck.Low}, C={ck.Close}, V={ck.Volume}");
                                    }
                                }
                                catch { }

                                var watcher = new EntryWatcher
                                {
                                    Target = longEntry,
                                    IsLong = true,
                                    Created = DateTime.UtcNow,
                                    Expiry = DateTime.UtcNow.AddHours((double)_scanDurationHours),
                                    SignalTimestamp = timestamp
                                };
                                _watchers[symbol] = watcher;
                                D($"[London VP] LONG breakout detected for {symbol}. LH={LH}, VAH={vah}, POC={poc}, target={longEntry}. Watching for touch until {watcher.Expiry:O}.");
                            }
                            else
                            {
                                D($"[London VP] LONG breakout for {symbol} ignored: already watching target {_watchers[symbol].Target}.");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[London VP] Order error (LONG) for {symbol}: {ex.Message}");
                }
            }

            // Short breakout: evaluate only for the current candle
            if (isShortBreak)
            {
                decimal shortEntry = (LL + val) / 2m;
                long timestamp = post.CloseTime;
                try
                {
                    var risk = Math.Abs(shortEntry - poc);
                        if (risk <= 0m)
                        {
                            D($"[London VP] Skipping SHORT for {symbol}: computed risk ({risk}) <= 0 (entry={shortEntry}, poc={poc}).");
                        }
                    else
                    {
                        var currentShorts = _shortPlacedPerSymbol.TryGetValue(symbol, out var sc2) ? sc2 : 0;
                        var currentLongs2 = _longPlacedPerSymbol.TryGetValue(symbol, out var lc2) ? lc2 : 0;
                        // On breakout, do NOT enter on the same candle. Always create a watcher for subsequent touches.
                        if (!_watchers.ContainsKey(symbol))
                        {
                            if (!_allowBothSides && currentLongs2 > 0)
                            {
                                D($"[London VP] SHORT breakout ignored for {symbol}: allowBothSides=false and long already placed for this symbol.");
                            }
                            else if (currentShorts >= _maxEntriesPerSidePerSession)
                            {
                                D($"[London VP] SHORT breakout ignored for {symbol}: reached per-symbol short cap ({currentShorts}/{_maxEntriesPerSidePerSession}).");
                            }
                            else
                            {
                            // Dump the last few post-session candles for debugging (helps find 11:51/11:52)
                            try
                            {
                                const int dumpCount = 6;
                                var recent = postSessionKlines.OrderBy(k => k.OpenTime).Skip(Math.Max(0, postSessionKlines.Count - dumpCount)).ToList();
                                D($"[London VP] Dumping last {recent.Count} post-session candles for {symbol} (most recent last):");
                                foreach (var ck in recent)
                                {
                                    var ot = DateTimeOffset.FromUnixTimeMilliseconds(ck.OpenTime).UtcDateTime;
                                    D($"[London VP]  Candle {ot:O} O={ck.Open}, H={ck.High}, L={ck.Low}, C={ck.Close}, V={ck.Volume}");
                                }
                            }
                            catch { }

                            var watcher = new EntryWatcher
                            {
                                Target = shortEntry,
                                IsLong = false,
                                Created = DateTime.UtcNow,
                                Expiry = DateTime.UtcNow.AddHours((double)_scanDurationHours),
                                SignalTimestamp = timestamp
                            };
                            _watchers[symbol] = watcher;
                            D($"[London VP] SHORT breakout detected for {symbol}. LL={LL}, VAL={val}, POC={poc}, target={shortEntry}. Watching for touch until {watcher.Expiry:O}.");
                            }
                        }
                        else
                        {
                            D($"[London VP] SHORT breakout for {symbol} ignored: already watching target {_watchers[symbol].Target}.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[London VP] Order error (SHORT) for {symbol}: {ex.Message}");
                }
            }
        }

        private void HandleErrorResponse(string symbol, RestResponse response)
        {
            Console.WriteLine($"Error for {symbol}: {response.ErrorMessage}");
            Console.WriteLine($"Status Code: {response.StatusCode}");
            Console.WriteLine($"Content: {response.Content}");
        }
    }
}
