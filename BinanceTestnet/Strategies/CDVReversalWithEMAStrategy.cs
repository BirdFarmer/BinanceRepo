using BinanceTestnet.Models;
using BinanceTestnet.Strategies.Helpers;
using BinanceTestnet.Trading;
using RestSharp;
using Skender.Stock.Indicators;
using System.Globalization;
using System.Collections.Concurrent;

namespace BinanceTestnet.Strategies
{
    public class CDVReversalWithEMAStrategy : StrategyBase, ISnapshotAwareStrategy
    {
        protected override bool SupportsClosedCandles => true;

        private const int EmaLength = 50;
        private const int TriggerExpiryBars = 40;
        private const int FetchKlinesLimit = 200; // enough history for trigger lifecycle
        // Pine-spec tuning defaults
        private const decimal MinBodyWickRatio = 1.5m; // min_body_wick_ratio
        private const decimal WickTolerance = 0.0001m; // wick_tolerance
        private static bool UseHaCandle = true; // hacandle in Pine (default true)

        // NOTE: set true for detailed per-symbol debugging; default false for minimal output
        public static bool EnableDebugLogging = false;

        private enum TriggerDirection { None = 0, Bullish = 1, Bearish = -1 }

        private class Trigger
        {
            public TriggerDirection Direction { get; set; }
            public int Index { get; set; } // index in the klines list where trigger occurred
            public long CloseTime { get; set; }
            public bool Consumed { get; set; }
        }

        // Per-symbol trigger storage to avoid cross-symbol state contamination
        private readonly ConcurrentDictionary<string, Trigger?> _activeTriggers = new();

        private string FormatTrigger(Trigger t)
        {
            if (t == null) return "(none)";
            var dt = DateTimeOffset.FromUnixTimeMilliseconds(t.CloseTime).UtcDateTime;
            return $"dir={t.Direction} index={t.Index} time={dt:u} consumed={t.Consumed}";
        }
        public CDVReversalWithEMAStrategy(RestClient client, string apiKey, OrderManager orderManager, Wallet wallet)
            : base(client, apiKey, orderManager, wallet)
        {
        }

        public override async Task RunAsync(string symbol, string interval)
        {
            try
            {
                LogDebug($"RunAsync start for {symbol} interval={interval}");
                var request = StrategyUtils.CreateGet("/fapi/v1/klines", new Dictionary<string, string>
                {
                    {"symbol", symbol}, {"interval", interval}, {"limit", FetchKlinesLimit.ToString()} 
                });

                var response = await Client.ExecuteGetAsync(request);
                if (!response.IsSuccessful || response.Content == null)
                {
                    HandleErrorResponse(symbol, response);
                    return;
                }

                var klines = StrategyUtils.ParseKlines(response.Content);
                if (klines == null || klines.Count < 3)
                {
                    LogError($"Not enough klines for {symbol}");
                    return;
                }

                // Pre-compute EMA series here so the rest of RunAsync can reference it
                // (ProcessKlinesAsync also computes its own series for the snapshot path).
                var quotesForEma = StrategyUtils.ToIndicatorQuotes(klines, useClosedCandle: true);
                var emaSeries = quotesForEma.GetEma(EmaLength).ToList();

                await ProcessKlinesAsync(symbol, klines);

                // Build CDV candle series using Pine-like weighted delta (_rate)
                var cdvCandles = new List<(decimal Open, decimal High, decimal Low, decimal Close)>();
                decimal cumulative = 0m;
                decimal prevCum = 0m;
                for (int i = 0; i < klines.Count; i++)
                {
                    var k = klines[i];

                    // _rate(cond) implementation
                    decimal tw = k.High - Math.Max(k.Open, k.Close);
                    decimal bw = Math.Min(k.Open, k.Close) - k.Low;
                    decimal body = Math.Abs(k.Close - k.Open);
                    decimal denom = tw + bw + body;
                    decimal rateTrue = 0.5m;
                    if (denom != 0)
                    {
                        // when cond == true (open <= close) use 2*body in numerator
                        var numTrue = 0.5m * (tw + bw + (2m * body));
                        rateTrue = numTrue / denom;
                    }
                    // guard
                    if (rateTrue == 0m) rateTrue = 0.5m;

                    decimal deltaup = k.Volume * rateTrue;
                    // delta down uses cond open > close -> the complementary weighting
                    decimal rateFalse = 1m - rateTrue; // approximate complementary; matches behavior when body dominates
                    decimal deltadown = k.Volume * rateFalse;

                    decimal delta = k.Close >= k.Open ? deltaup : -deltadown;
                    prevCum = cumulative;
                    cumulative += delta;

                    var open = prevCum;
                    var close = cumulative;
                    var high = Math.Max(prevCum, cumulative);
                    var low = Math.Min(prevCum, cumulative);

                    cdvCandles.Add((open, high, low, close));
                }

                // Optional Heikin-Ashi conversion for CDV candles (Pine option hacandle)
                var cdvFinal = new List<(decimal Open, decimal High, decimal Low, decimal Close)>();
                if (UseHaCandle)
                {
                    decimal haOpenPrev = 0m;
                    decimal haClosePrev = 0m;
                    for (int i = 0; i < cdvCandles.Count; i++)
                    {
                        var c = cdvCandles[i];
                        var haClose = (c.Open + c.High + c.Low + c.Close) / 4m;
                        decimal haOpen = i == 0 ? (c.Open + c.Close) / 2m : (haOpenPrev + haClosePrev) / 2m;
                        var haHigh = Math.Max(c.High, Math.Max(haOpen, haClose));
                        var haLow = Math.Min(c.Low, Math.Min(haOpen, haClose));
                        cdvFinal.Add((haOpen, haHigh, haLow, haClose));
                        haOpenPrev = haOpen;
                        haClosePrev = haClose;
                    }
                }
                else
                {
                    cdvFinal = cdvCandles.ToList();
                }

                // Rehydrate recent triggers by scanning back up to TriggerExpiryBars
                // so live runs can pick up triggers that formed before the strategy started.
                // We only rehydrate confirmed double triggers (two consecutive single-bar triggers)
                var (signalKline, previousKline) = StrategyUtils.SelectSignalPair(klines, useClosedCandle: true);
                if (signalKline == null || previousKline == null) return;

                int signalIndex = klines.IndexOf(signalKline);
                // helper to get EMA decimal for a given kline index (align EMA series to klines)
                decimal GetEmaAt(int klineIndex)
                {
                    if (emaSeries == null || emaSeries.Count == 0) return 0m;
                    // EMA series produced by Skender starts at kline index (EmaLength - 1)
                    int emaIndex = klineIndex - (EmaLength - 1);
                    if (emaIndex < 0 || emaIndex >= emaSeries.Count) return 0m;
                    var tmp = emaSeries[emaIndex].Ema;
                    return tmp.HasValue ? (decimal)tmp.Value : 0m;
                }

                // scan backwards from signalIndex to find the most recent valid trigger
                var lookbackStart = Math.Max(1, signalIndex - TriggerExpiryBars);
                Trigger? found = null;
                for (int i = signalIndex; i >= lookbackStart; i--)
                {
                    var k = klines[i];
                    var prevK = klines[Math.Max(0, i - 1)];
                    // check for confirmed double trigger at i and i-1
                    if (i < 1) continue;
                    var kPrev = klines[i - 1];
                    var cdv_i = cdvFinal[i];
                    var cdvPrev_i = cdvFinal[Math.Max(0, i - 1)];

                    bool priceGreen_i = k.Close > k.Open;
                    bool priceRed_i = k.Close < k.Open;
                    // use CDV candle color instead of comparing to previous close: green CDV means cdv.Close > cdv.Open
                    bool cdvGreen_i = cdv_i.Close > cdv_i.Open;
                    bool cdvRed_i = cdv_i.Close < cdv_i.Open;
                    decimal cdvBody_i = Math.Abs(cdv_i.Close - cdv_i.Open);
                    decimal cdvUpper_i = cdv_i.High - Math.Max(cdv_i.Open, cdv_i.Close);
                    decimal cdvLower_i = Math.Min(cdv_i.Open, cdv_i.Close) - cdv_i.Low;
                    var emaAtI = GetEmaAt(i);
                    bool priceHighBelowEma_i = k.High < emaAtI;
                    bool priceLowAboveEma_i = k.Low > emaAtI;

                    // single-bar triggers for i and i-1
                    // bearish trigger: red price candle + green CDV candle with small lower wick
                    bool bearish_i = priceRed_i && cdvGreen_i && cdvLower_i <= WickTolerance && cdvBody_i > cdvUpper_i * MinBodyWickRatio && priceLowAboveEma_i;
                    // bullish trigger: green price candle + red CDV candle with small upper wick
                    bool bullish_i = priceGreen_i && cdvRed_i && cdvUpper_i <= WickTolerance && cdvBody_i > cdvLower_i * MinBodyWickRatio && priceHighBelowEma_i;

                    // compute for previous bar
                    var cdv_prevbar = cdvFinal[i - 1];
                    var kprev = klines[i - 1];
                    bool priceGreen_prev = kprev.Close > kprev.Open;
                    bool priceRed_prev = kprev.Close < kprev.Open;
                    bool cdvGreen_prev = cdv_prevbar.Close > cdv_prevbar.Open;
                    bool cdvRed_prev = cdv_prevbar.Close < cdv_prevbar.Open;
                    decimal cdvBody_prev = Math.Abs(cdv_prevbar.Close - cdv_prevbar.Open);
                    decimal cdvUpper_prev = cdv_prevbar.High - Math.Max(cdv_prevbar.Open, cdv_prevbar.Close);
                    decimal cdvLower_prev = Math.Min(cdv_prevbar.Open, cdv_prevbar.Close) - cdv_prevbar.Low;
                    var emaAtPrev_i = GetEmaAt(i - 1);
                    bool priceHighBelowEma_prev = kprev.High < emaAtPrev_i;
                    bool priceLowAboveEma_prev = kprev.Low > emaAtPrev_i;

                    bool bearish_prev = priceRed_prev && cdvGreen_prev && cdvLower_prev <= WickTolerance && cdvBody_prev > cdvUpper_prev * MinBodyWickRatio && priceLowAboveEma_prev;
                    bool bullish_prev = priceGreen_prev && cdvRed_prev && cdvUpper_prev <= WickTolerance && cdvBody_prev > cdvLower_prev * MinBodyWickRatio && priceHighBelowEma_prev;

                    if (bullish_i && bullish_prev)
                    {
                        found = new Trigger { Direction = TriggerDirection.Bullish, Index = i, CloseTime = k.CloseTime, Consumed = false };
                        break;
                    }
                    if (bearish_i && bearish_prev)
                    {
                        found = new Trigger { Direction = TriggerDirection.Bearish, Index = i, CloseTime = k.CloseTime, Consumed = false };
                        break;
                    }
                }

                if (found != null)
                {
                    // apply cancellation if opposite exists
                    var active = GetActiveTrigger(symbol);
                    if (active != null && active.Direction != found.Direction)
                    {
                        LogDebug($"Rehydration: cancelling existing {active.Direction} trigger for {symbol} in favor of rehydrated {found.Direction} trigger — previous: {FormatTrigger(active)}");
                        ClearActiveTrigger(symbol);
                    }
                    // only set if none exists
                    if (GetActiveTrigger(symbol) == null)
                        SetActiveTrigger(symbol, found);
                    var activeNow = GetActiveTrigger(symbol);
                    if (activeNow != null && activeNow.Index == found.Index)
                        LogDebug($"Rehydrated trigger for {symbol}: {FormatTrigger(activeNow)}");
                }

                // signal pair already extracted; signalIndex variable available
                var cdvSignal = cdvFinal[signalIndex];
                var cdvPrev = cdvFinal[Math.Max(0, signalIndex - 1)];

                // Diagnostic: emit detailed state to help debug why triggers are not created
                LogDebug($"{symbol} debug: klines={klines.Count} emaSeries={emaSeries.Count} signalIndex={signalIndex}");
                var emaAtSignal = GetEmaAt(signalIndex);
                var emaAtPrev = GetEmaAt(Math.Max(0, signalIndex - 1));
                LogDebug($"{symbol} debug: emaAtSignal={emaAtSignal}, emaAtPrev={emaAtPrev}, signalClose={signalKline.Close}, signalHigh={signalKline.High}, signalLow={signalKline.Low}");
                LogDebug($"{symbol} debug: cdvSignal=open={cdvSignal.Open},high={cdvSignal.High},low={cdvSignal.Low},close={cdvSignal.Close}");
                LogDebug($"{symbol} debug: cdvPrev=open={cdvPrev.Open},high={cdvPrev.High},low={cdvPrev.Low},close={cdvPrev.Close}");

                // Evaluate single-bar conditions for signal and previous bar, then require double (both true)
                bool priceCandleGreen = signalKline.Close > signalKline.Open;
                bool priceCandleRed = signalKline.Close < signalKline.Open;

                bool cdvCandleGreen = cdvSignal.Close > cdvSignal.Open;
                bool cdvCandleRed = cdvSignal.Close < cdvSignal.Open;

                decimal cdvBody = Math.Abs(cdvSignal.Close - cdvSignal.Open);
                decimal cdvUpper = cdvSignal.High - Math.Max(cdvSignal.Open, cdvSignal.Close);
                decimal cdvLower = Math.Min(cdvSignal.Open, cdvSignal.Close) - cdvSignal.Low;

                // bullish: green price candle + red CDV candle with small upper wick
                bool singleBullish = priceCandleGreen && cdvCandleRed && cdvUpper <= WickTolerance && cdvBody > cdvLower * MinBodyWickRatio && (signalKline.High < emaAtSignal);
                // bearish: red price candle + green CDV candle with small lower wick
                bool singleBearish = priceCandleRed && cdvCandleGreen && cdvLower <= WickTolerance && cdvBody > cdvUpper * MinBodyWickRatio && (signalKline.Low > emaAtSignal);

                bool prevSingleBullish = false;
                bool prevSingleBearish = false;
                if (signalIndex - 1 >= 0)
                {
                    var prevCandle = klines[signalIndex - 1];
                    var cdvPrevBar = cdvFinal[signalIndex - 1];
                    var cdvPrevPrevBar = cdvFinal[Math.Max(0, signalIndex - 2)];
                    bool priceGreenPrev = prevCandle.Close > prevCandle.Open;
                    bool priceRedPrev = prevCandle.Close < prevCandle.Open;
                    bool cdvPrevBarGreen = cdvPrevBar.Close > cdvPrevBar.Open;
                    bool cdvPrevBarRed = cdvPrevBar.Close < cdvPrevBar.Open;
                    decimal cdvBodyPrev = Math.Abs(cdvPrevBar.Close - cdvPrevBar.Open);
                    decimal cdvUpperPrev = cdvPrevBar.High - Math.Max(cdvPrevBar.Open, cdvPrevBar.Close);
                    decimal cdvLowerPrev = Math.Min(cdvPrevBar.Open, cdvPrevBar.Close) - cdvPrevBar.Low;
                    var emaPrevBar = GetEmaAt(signalIndex - 1);
                    prevSingleBullish = priceGreenPrev && cdvPrevBarRed && cdvUpperPrev <= WickTolerance && cdvBodyPrev > cdvLowerPrev * MinBodyWickRatio && (prevCandle.High < emaPrevBar);
                    prevSingleBearish = priceRedPrev && cdvPrevBarGreen && cdvLowerPrev <= WickTolerance && cdvBodyPrev > cdvUpperPrev * MinBodyWickRatio && (prevCandle.Low > emaPrevBar);
                }

                // Double bullish trigger conditions
                if (singleBullish && prevSingleBullish)
                {
                    var active = GetActiveTrigger(symbol);
                    if (active?.Direction == TriggerDirection.Bearish)
                    {
                        LogDebug($"Cancelling existing bearish trigger for {symbol} due to new bullish trigger — previous: {FormatTrigger(active)}");
                        ClearActiveTrigger(symbol);
                    }

                    var newTrig = new Trigger { Direction = TriggerDirection.Bullish, Index = signalIndex, CloseTime = signalKline.CloseTime, Consumed = false };
                    SetActiveTrigger(symbol, newTrig);
                    var activeNow = GetActiveTrigger(symbol);
                    LogDebug($"Set bullish trigger for {symbol} — {(activeNow != null ? FormatTrigger(activeNow) : "(none)")}");
                    StrategyUtils.TraceSignalCandle("CDVReversalWithEMAStrategy", symbol, true, signalKline, previousKline, "DoubleBullishTrigger");
                }

                // Double bearish trigger
                if (singleBearish && prevSingleBearish)
                {
                    var active = GetActiveTrigger(symbol);
                    if (active?.Direction == TriggerDirection.Bullish)
                    {
                        LogDebug($"Cancelling existing bullish trigger for {symbol} due to new bearish trigger — previous: {FormatTrigger(active)}");
                        ClearActiveTrigger(symbol);
                    }

                    var newTrig = new Trigger { Direction = TriggerDirection.Bearish, Index = signalIndex, CloseTime = signalKline.CloseTime, Consumed = false };
                    SetActiveTrigger(symbol, newTrig);
                    var activeNowB = GetActiveTrigger(symbol);
                    LogDebug($"Set bearish trigger for {symbol} — {(activeNowB != null ? FormatTrigger(activeNowB) : "(none)")}");
                    StrategyUtils.TraceSignalCandle("CDVReversalWithEMAStrategy", symbol, true, signalKline, previousKline, "DoubleBearishTrigger");
                }

                // Expire triggers older than TriggerExpiryBars
                var activeCheck = GetActiveTrigger(symbol);
                if (activeCheck != null)
                {
                    var age = signalIndex - activeCheck.Index;
                    if (age > TriggerExpiryBars)
                    {
                        LogDebug($"Trigger expired for {symbol} (age={age}) — expired trigger: {FormatTrigger(activeCheck)}");
                        ClearActiveTrigger(symbol);
                    }
                }

                // Entry checks — whole candle crosses EMA50 with active trigger within last 40 bars
                var prevIndex = Math.Max(0, signalIndex - 1);
                var prevKline = klines[prevIndex];

                bool crossesAboveEma = signalKline.Low > emaAtSignal && prevKline.Low <= emaAtPrev;
                bool crossesBelowEma = signalKline.High < emaAtSignal && prevKline.High >= emaAtPrev;

                var activeAtEntry = GetActiveTrigger(symbol);
                if (crossesAboveEma && activeAtEntry != null && activeAtEntry.Direction == TriggerDirection.Bullish && !activeAtEntry.Consumed)
                {
                    var age = signalIndex - activeAtEntry.Index;
                    if (age <= TriggerExpiryBars)
                    {
                        LogDebug($"Placing LONG for {symbol} — EMA crossover with active bullish trigger; trigger={FormatTrigger(activeAtEntry)}");
                        await OrderManager.PlaceLongOrderAsync(symbol, signalKline.Close, "CDVReversal+EMA50", signalKline.CloseTime);
                        TryConsumeActiveTrigger(symbol);
                    }
                    else
                    {
                        LogDebug($"Active bullish trigger expired at entry check for {symbol} — trigger={FormatTrigger(activeAtEntry)}");
                    }
                }

                if (crossesBelowEma && activeAtEntry != null && activeAtEntry.Direction == TriggerDirection.Bearish && !activeAtEntry.Consumed)
                {
                    var age = signalIndex - activeAtEntry.Index;
                    if (age <= TriggerExpiryBars)
                    {
                        LogDebug($"Placing SHORT for {symbol} — EMA crossover with active bearish trigger; trigger={FormatTrigger(activeAtEntry)}");
                        await OrderManager.PlaceShortOrderAsync(symbol, signalKline.Close, "CDVReversal+EMA50", signalKline.CloseTime);
                        TryConsumeActiveTrigger(symbol);
                    }
                    else
                    {
                        LogDebug($"Active bearish trigger expired at entry check for {symbol} — trigger={FormatTrigger(activeAtEntry)}");
                    }
                }

                // Check and close trades using existing manager
                var currentPrices = new Dictionary<string, decimal> { { symbol, signalKline.Close } };
                await OrderManager.CheckAndCloseTrades(currentPrices, signalKline.CloseTime);

                // Diagnostic: always emit active trigger summary so multitest logs capture it
                LogInfo($"Active trigger for {symbol}: {GetActiveTriggerSummary(symbol)}");
            }
            catch (Exception ex)
            {
                LogError($"Error processing {symbol}: {ex.Message}");
            }
        }

            // Shared helper so snapshot path can reuse the same logic
            private async Task ProcessKlinesAsync(string symbol, List<Kline> klines)
            {
                // Build indicator quotes and EMA
                var quotes = StrategyUtils.ToIndicatorQuotes(klines, useClosedCandle: true);
                var emaSeries = quotes.GetEma(EmaLength).ToList();
                double? emaTmp = emaSeries.Count > 0 ? emaSeries.Last().Ema : null;
                var emaValue = emaTmp.HasValue ? (decimal)emaTmp.Value : 0m;

                // Build CDV candle series using Pine-like weighted delta (_rate)
                var cdvCandles = new List<(decimal Open, decimal High, decimal Low, decimal Close)>();
                decimal cumulative = 0m;
                decimal prevCum = 0m;
                for (int i = 0; i < klines.Count; i++)
                {
                    var k = klines[i];

                    // _rate(cond) implementation
                    decimal tw = k.High - Math.Max(k.Open, k.Close);
                    decimal bw = Math.Min(k.Open, k.Close) - k.Low;
                    decimal body = Math.Abs(k.Close - k.Open);
                    decimal denom = tw + bw + body;
                    decimal rateTrue = 0.5m;
                    if (denom != 0)
                    {
                        // when cond == true (open <= close) use 2*body in numerator
                        var numTrue = 0.5m * (tw + bw + (2m * body));
                        rateTrue = numTrue / denom;
                    }
                    // guard
                    if (rateTrue == 0m) rateTrue = 0.5m;

                    decimal deltaup = k.Volume * rateTrue;
                    // delta down uses cond open > close -> the complementary weighting
                    decimal rateFalse = 1m - rateTrue; // approximate complementary; matches behavior when body dominates
                    decimal deltadown = k.Volume * rateFalse;

                    decimal delta = k.Close >= k.Open ? deltaup : -deltadown;
                    prevCum = cumulative;
                    cumulative += delta;

                    var open = prevCum;
                    var close = cumulative;
                    var high = Math.Max(prevCum, cumulative);
                    var low = Math.Min(prevCum, cumulative);

                    cdvCandles.Add((open, high, low, close));
                }

                // Optional Heikin-Ashi conversion for CDV candles (Pine option hacandle)
                var cdvFinal = new List<(decimal Open, decimal High, decimal Low, decimal Close)>();
                if (UseHaCandle)
                {
                    decimal haOpenPrev = 0m;
                    decimal haClosePrev = 0m;
                    for (int i = 0; i < cdvCandles.Count; i++)
                    {
                        var c = cdvCandles[i];
                        var haClose = (c.Open + c.High + c.Low + c.Close) / 4m;
                        decimal haOpen = i == 0 ? (c.Open + c.Close) / 2m : (haOpenPrev + haClosePrev) / 2m;
                        var haHigh = Math.Max(c.High, Math.Max(haOpen, haClose));
                        var haLow = Math.Min(c.Low, Math.Min(haOpen, haClose));
                        cdvFinal.Add((haOpen, haHigh, haLow, haClose));
                        haOpenPrev = haOpen;
                        haClosePrev = haClose;
                    }
                }
                else
                {
                    cdvFinal = cdvCandles.ToList();
                }

                // Rehydrate recent triggers by scanning back up to TriggerExpiryBars
                var (signalKline, previousKline) = StrategyUtils.SelectSignalPair(klines, useClosedCandle: true);
                if (signalKline == null || previousKline == null) return;

                int signalIndex = klines.IndexOf(signalKline);
                // helper to get EMA decimal for a given kline index (align EMA series to klines)
                decimal GetEmaAt(int klineIndex)
                {
                    if (emaSeries == null || emaSeries.Count == 0) return 0m;
                    int emaIndex = klineIndex - (EmaLength - 1);
                    if (emaIndex < 0 || emaIndex >= emaSeries.Count) return 0m;
                    var tmp = emaSeries[emaIndex].Ema;
                    return tmp.HasValue ? (decimal)tmp.Value : 0m;
                }

                // scan backwards from signalIndex to find the most recent valid trigger
                var lookbackStart = Math.Max(1, signalIndex - TriggerExpiryBars);
                Trigger? found = null;
                for (int i = signalIndex; i >= lookbackStart; i--)
                {
                    var k = klines[i];
                    var prevK = klines[Math.Max(0, i - 1)];
                    if (i < 1) continue;
                    var kPrev = klines[i - 1];
                    var cdv_i = cdvFinal[i];
                    var cdvPrev_i = cdvFinal[Math.Max(0, i - 1)];

                    bool priceGreen_i = k.Close > k.Open;
                    bool priceRed_i = k.Close < k.Open;
                    bool cdvGreen_i = cdv_i.Close > cdv_i.Open;
                    bool cdvRed_i = cdv_i.Close < cdv_i.Open;
                    decimal cdvBody_i = Math.Abs(cdv_i.Close - cdv_i.Open);
                    decimal cdvUpper_i = cdv_i.High - Math.Max(cdv_i.Open, cdv_i.Close);
                    decimal cdvLower_i = Math.Min(cdv_i.Open, cdv_i.Close) - cdv_i.Low;
                    var emaAtI = GetEmaAt(i);
                    bool priceHighBelowEma_i = k.High < emaAtI;
                    bool priceLowAboveEma_i = k.Low > emaAtI;

                    bool bearish_i = priceRed_i && cdvGreen_i && cdvLower_i <= WickTolerance && cdvBody_i > cdvUpper_i * MinBodyWickRatio && priceLowAboveEma_i;
                    bool bullish_i = priceGreen_i && cdvRed_i && cdvUpper_i <= WickTolerance && cdvBody_i > cdvLower_i * MinBodyWickRatio && priceHighBelowEma_i;

                    var cdv_prevbar = cdvFinal[i - 1];
                    var kprev = klines[i - 1];
                    bool priceGreen_prev = kprev.Close > kprev.Open;
                    bool priceRed_prev = kprev.Close < kprev.Open;
                    bool cdvGreen_prev = cdv_prevbar.Close > cdv_prevbar.Open;
                    bool cdvRed_prev = cdv_prevbar.Close < cdv_prevbar.Open;
                    decimal cdvBody_prev = Math.Abs(cdv_prevbar.Close - cdv_prevbar.Open);
                    decimal cdvUpper_prev = cdv_prevbar.High - Math.Max(cdv_prevbar.Open, cdv_prevbar.Close);
                    decimal cdvLower_prev = Math.Min(cdv_prevbar.Open, cdv_prevbar.Close) - cdv_prevbar.Low;
                    var emaAtPrev_i = GetEmaAt(i - 1);
                    bool priceHighBelowEma_prev = kprev.High < emaAtPrev_i;
                    bool priceLowAboveEma_prev = kprev.Low > emaAtPrev_i;

                    bool bearish_prev = priceRed_prev && cdvGreen_prev && cdvLower_prev <= WickTolerance && cdvBody_prev > cdvUpper_prev * MinBodyWickRatio && priceLowAboveEma_prev;
                    bool bullish_prev = priceGreen_prev && cdvRed_prev && cdvUpper_prev <= WickTolerance && cdvBody_prev > cdvLower_prev * MinBodyWickRatio && priceHighBelowEma_prev;

                    if (bullish_i && bullish_prev)
                    {
                        found = new Trigger { Direction = TriggerDirection.Bullish, Index = i, CloseTime = k.CloseTime, Consumed = false };
                        break;
                    }
                    if (bearish_i && bearish_prev)
                    {
                        found = new Trigger { Direction = TriggerDirection.Bearish, Index = i, CloseTime = k.CloseTime, Consumed = false };
                        break;
                    }
                }

                if (found != null)
                {
                    var active = GetActiveTrigger(symbol);
                    if (active != null && active.Direction != found.Direction)
                    {
                        LogDebug($"Rehydration: cancelling existing {active.Direction} trigger for {symbol} in favor of rehydrated {found.Direction} trigger — previous: {FormatTrigger(active)}");
                        ClearActiveTrigger(symbol);
                    }
                    if (GetActiveTrigger(symbol) == null)
                        SetActiveTrigger(symbol, found);
                    var activeNow = GetActiveTrigger(symbol);
                    if (activeNow != null && activeNow.Index == found.Index)
                        LogDebug($"Rehydrated trigger for {symbol}: {FormatTrigger(activeNow)}");
                }

                var cdvSignal = cdvFinal[signalIndex];
                var cdvPrev = cdvFinal[Math.Max(0, signalIndex - 1)];

                bool priceCandleGreen = signalKline.Close > signalKline.Open;
                bool priceCandleRed = signalKline.Close < signalKline.Open;

                bool cdvCandleGreen = cdvSignal.Close > cdvSignal.Open;
                bool cdvCandleRed = cdvSignal.Close < cdvSignal.Open;

                decimal cdvBody = Math.Abs(cdvSignal.Close - cdvSignal.Open);
                decimal cdvUpper = cdvSignal.High - Math.Max(cdvSignal.Open, cdvSignal.Close);
                decimal cdvLower = Math.Min(cdvSignal.Open, cdvSignal.Close) - cdvSignal.Low;

                bool singleBullish = priceCandleGreen && cdvCandleRed && cdvUpper <= WickTolerance && cdvBody > cdvLower * MinBodyWickRatio && (signalKline.High < GetEmaAt(signalIndex));
                bool singleBearish = priceCandleRed && cdvCandleGreen && cdvLower <= WickTolerance && cdvBody > cdvUpper * MinBodyWickRatio && (signalKline.Low > GetEmaAt(signalIndex));

                bool prevSingleBullish = false;
                bool prevSingleBearish = false;
                if (signalIndex - 1 >= 0)
                {
                    var prevCandle = klines[signalIndex - 1];
                    var cdvPrevBar = cdvFinal[signalIndex - 1];
                    var cdvPrevPrevBar = cdvFinal[Math.Max(0, signalIndex - 2)];
                    bool priceGreenPrev = prevCandle.Close > prevCandle.Open;
                    bool priceRedPrev = prevCandle.Close < prevCandle.Open;
                    bool cdvPrevBarGreen = cdvPrevBar.Close > cdvPrevBar.Open;
                    bool cdvPrevBarRed = cdvPrevBar.Close < cdvPrevBar.Open;
                    decimal cdvBodyPrev = Math.Abs(cdvPrevBar.Close - cdvPrevBar.Open);
                    decimal cdvUpperPrev = cdvPrevBar.High - Math.Max(cdvPrevBar.Open, cdvPrevBar.Close);
                    decimal cdvLowerPrev = Math.Min(cdvPrevBar.Open, cdvPrevBar.Close) - cdvPrevBar.Low;
                    var emaPrevBar = GetEmaAt(signalIndex - 1);
                    prevSingleBullish = priceGreenPrev && cdvPrevBarRed && cdvUpperPrev <= WickTolerance && cdvBodyPrev > cdvLowerPrev * MinBodyWickRatio && (prevCandle.High < emaPrevBar);
                    prevSingleBearish = priceRedPrev && cdvPrevBarGreen && cdvLowerPrev <= WickTolerance && cdvBodyPrev > cdvUpperPrev * MinBodyWickRatio && (prevCandle.Low > emaPrevBar);
                }

                if (singleBullish && prevSingleBullish)
                {
                    var active = GetActiveTrigger(symbol);
                    if (active?.Direction == TriggerDirection.Bearish)
                    {
                        LogDebug($"Cancelling existing bearish trigger for {symbol} due to new bullish trigger — previous: {FormatTrigger(active)}");
                        ClearActiveTrigger(symbol);
                    }

                    var newTrig = new Trigger { Direction = TriggerDirection.Bullish, Index = signalIndex, CloseTime = signalKline.CloseTime, Consumed = false };
                    SetActiveTrigger(symbol, newTrig);
                    var activeNow = GetActiveTrigger(symbol);
                    LogDebug($"Set bullish trigger for {symbol} — {(activeNow != null ? FormatTrigger(activeNow) : "(none)")}");
                    StrategyUtils.TraceSignalCandle("CDVReversalWithEMAStrategy", symbol, true, signalKline, previousKline, "DoubleBullishTrigger");
                }

                if (singleBearish && prevSingleBearish)
                {
                    var active = GetActiveTrigger(symbol);
                    if (active?.Direction == TriggerDirection.Bullish)
                    {
                        LogDebug($"Cancelling existing bullish trigger for {symbol} due to new bearish trigger — previous: {FormatTrigger(active)}");
                        ClearActiveTrigger(symbol);
                    }

                    var newTrig = new Trigger { Direction = TriggerDirection.Bearish, Index = signalIndex, CloseTime = signalKline.CloseTime, Consumed = false };
                    SetActiveTrigger(symbol, newTrig);
                    var activeNowB = GetActiveTrigger(symbol);
                    LogDebug($"Set bearish trigger for {symbol} — {(activeNowB != null ? FormatTrigger(activeNowB) : "(none)")}");
                    StrategyUtils.TraceSignalCandle("CDVReversalWithEMAStrategy", symbol, true, signalKline, previousKline, "DoubleBearishTrigger");
                }

                var activeCheck = GetActiveTrigger(symbol);
                if (activeCheck != null)
                {
                    var age = signalIndex - activeCheck.Index;
                    if (age > TriggerExpiryBars)
                    {
                        LogDebug($"Trigger expired for {symbol} (age={age}) — expired trigger: {FormatTrigger(activeCheck)}");
                        ClearActiveTrigger(symbol);
                    }
                }

                var prevIndex = Math.Max(0, signalIndex - 1);
                var prevKline = klines[prevIndex];

                bool crossesAboveEma = signalKline.Low > GetEmaAt(signalIndex) && prevKline.Low <= GetEmaAt(prevIndex);
                bool crossesBelowEma = signalKline.High < GetEmaAt(signalIndex) && prevKline.High >= GetEmaAt(prevIndex);

                var activeAtEntry = GetActiveTrigger(symbol);
                if (crossesAboveEma && activeAtEntry != null && activeAtEntry.Direction == TriggerDirection.Bullish && !activeAtEntry.Consumed)
                {
                    var age = signalIndex - activeAtEntry.Index;
                    if (age <= TriggerExpiryBars)
                    {
                        LogDebug($"Placing LONG for {symbol} — EMA crossover with active bullish trigger; trigger={FormatTrigger(activeAtEntry)}");
                        await OrderManager.PlaceLongOrderAsync(symbol, signalKline.Close, "CDVReversal+EMA50", signalKline.CloseTime);
                        TryConsumeActiveTrigger(symbol);
                    }
                    else
                    {
                        LogDebug($"Active bullish trigger expired at entry check for {symbol} — trigger={FormatTrigger(activeAtEntry)}");
                    }
                }

                if (crossesBelowEma && activeAtEntry != null && activeAtEntry.Direction == TriggerDirection.Bearish && !activeAtEntry.Consumed)
                {
                    var age = signalIndex - activeAtEntry.Index;
                    if (age <= TriggerExpiryBars)
                    {
                        LogDebug($"Placing SHORT for {symbol} — EMA crossover with active bearish trigger; trigger={FormatTrigger(activeAtEntry)}");
                        await OrderManager.PlaceShortOrderAsync(symbol, signalKline.Close, "CDVReversal+EMA50", signalKline.CloseTime);
                        TryConsumeActiveTrigger(symbol);
                    }
                    else
                    {
                        LogDebug($"Active bearish trigger expired at entry check for {symbol} — trigger={FormatTrigger(activeAtEntry)}");
                    }
                }

                var currentPrices = new Dictionary<string, decimal> { { symbol, signalKline.Close } };
                await OrderManager.CheckAndCloseTrades(currentPrices, signalKline.CloseTime);

                LogInfo($"Active trigger for {symbol}: {GetActiveTriggerSummary(symbol)}");
            }

            public async Task RunAsyncWithSnapshot(string symbol, string interval, Dictionary<string, List<Kline>> snapshot)
            {
                if (snapshot != null && snapshot.TryGetValue(symbol, out var klines) && klines != null && klines.Count > 0)
                {
                    await ProcessKlinesAsync(symbol, klines);
                }
                else
                {
                    await RunAsync(symbol, interval);
                }
            }

        public override async Task RunOnHistoricalDataAsync(IEnumerable<Kline> historicalCandles)
        {
            LogDebug($"RunOnHistoricalDataAsync start: candles={historicalCandles.Count()}");

            var klines = historicalCandles.ToList();
            if (klines.Count < 3) return;

            // Build CDV candle series using Pine-like weighted delta (_rate) for historical runs
            var cdvCandles = new List<(decimal Open, decimal High, decimal Low, decimal Close)>();
            decimal cumulativeHist = 0m;
            decimal prevCumHist = 0m;
            for (int i = 0; i < klines.Count; i++)
            {
                var k = klines[i];
                decimal tw = k.High - Math.Max(k.Open, k.Close);
                decimal bw = Math.Min(k.Open, k.Close) - k.Low;
                decimal body = Math.Abs(k.Close - k.Open);
                decimal denom = tw + bw + body;
                decimal rateTrue = 0.5m;
                if (denom != 0)
                {
                    var numTrue = 0.5m * (tw + bw + (2m * body));
                    rateTrue = numTrue / denom;
                }
                if (rateTrue == 0m) rateTrue = 0.5m;
                decimal deltaup = k.Volume * rateTrue;
                decimal rateFalse = 1m - rateTrue;
                decimal deltadown = k.Volume * rateFalse;
                decimal delta = k.Close >= k.Open ? deltaup : -deltadown;
                prevCumHist = cumulativeHist;
                cumulativeHist += delta;
                var open = prevCumHist;
                var close = cumulativeHist;
                var high = Math.Max(prevCumHist, cumulativeHist);
                var low = Math.Min(prevCumHist, cumulativeHist);
                cdvCandles.Add((open, high, low, close));
            }

            // Optional HA conversion
            var cdvFinal = new List<(decimal Open, decimal High, decimal Low, decimal Close)>();
            if (UseHaCandle)
            {
                decimal haOpenPrev = 0m;
                decimal haClosePrev = 0m;
                for (int i = 0; i < cdvCandles.Count; i++)
                {
                    var c = cdvCandles[i];
                    var haClose = (c.Open + c.High + c.Low + c.Close) / 4m;
                    decimal haOpen = i == 0 ? (c.Open + c.Close) / 2m : (haOpenPrev + haClosePrev) / 2m;
                    var haHigh = Math.Max(c.High, Math.Max(haOpen, haClose));
                    var haLow = Math.Min(c.Low, Math.Min(haOpen, haClose));
                    cdvFinal.Add((haOpen, haHigh, haLow, haClose));
                    haOpenPrev = haOpen;
                    haClosePrev = haClose;
                }
            }
            else
            {
                cdvFinal = cdvCandles.ToList();
            }

            var quotes = StrategyUtils.ToIndicatorQuotes(klines, useClosedCandle: true);
            var emaSeries = quotes.GetEma(EmaLength).ToList();

            // helper to obtain a usable EMA value for any kline index (align EMA series to kline indices)
            decimal GetEmaAtIndex(List<Skender.Stock.Indicators.EmaResult> series, int klineIndex)
            {
                if (series == null || series.Count == 0) return 0m;
                // EMA series starts at kline index (EmaLength - 1)
                int emaIndex = klineIndex - (EmaLength - 1);
                if (emaIndex < 0 || emaIndex >= series.Count) return 0m;
                var tmp = series[emaIndex].Ema;
                return tmp.HasValue ? (decimal)tmp.Value : 0m;
            }

            // Iterate through historical candles and apply Pine-spec double-trigger + EMA entry
            for (int idx = 1; idx < klines.Count; idx++)
            {
                var signal = klines[idx];
                var prev = klines[idx - 1];

                // Use a clamped EMA lookup so historical and live flows behave consistently
                decimal emaSignal = GetEmaAtIndex(emaSeries, idx);
                decimal emaPrev = GetEmaAtIndex(emaSeries, idx - 1);

                var cdv = cdvFinal[idx];
                var cdvPrev = cdvFinal[Math.Max(0, idx - 1)];

                bool priceGreen = signal.Close > signal.Open;
                bool priceRed = signal.Close < signal.Open;
                bool cdvGreen = cdv.Close > cdv.Open;
                bool cdvRed = cdv.Close < cdv.Open;
                decimal cdvBody = Math.Abs(cdv.Close - cdv.Open);
                decimal cdvUpper = cdv.High - Math.Max(cdv.Open, cdv.Close);
                decimal cdvLower = Math.Min(cdv.Open, cdv.Close) - cdv.Low;

                // bullish: green price + red CDV candle (small upper wick)
                bool singleBullish = priceGreen && cdvRed && cdvUpper <= WickTolerance && cdvBody > cdvLower * MinBodyWickRatio && (signal.High < emaSignal);
                // bearish: red price + green CDV candle (small lower wick)
                bool singleBearish = priceRed && cdvGreen && cdvLower <= WickTolerance && cdvBody > cdvUpper * MinBodyWickRatio && (signal.Low > emaSignal);

                // compute previous single-bar
                bool prevSingleBullish = false;
                bool prevSingleBearish = false;
                if (idx - 1 >= 0)
                {
                    var p = klines[idx - 1];
                    var cdv_p = cdvFinal[idx - 1];
                    var cdv_pp = cdvFinal[Math.Max(0, idx - 2)];
                    bool pGreen = p.Close > p.Open;
                    bool pRed = p.Close < p.Open;
                    bool cdv_p_green = cdv_p.Close > cdv_p.Open;
                    bool cdv_p_red = cdv_p.Close < cdv_p.Open;
                    decimal cdvBodyPrev = Math.Abs(cdv_p.Close - cdv_p.Open);
                    decimal cdvUpperPrev = cdv_p.High - Math.Max(cdv_p.Open, cdv_p.Close);
                    decimal cdvLowerPrev = Math.Min(cdv_p.Open, cdv_p.Close) - cdv_p.Low;
                    decimal emaPrevVal = GetEmaAtIndex(emaSeries, idx - 1);
                    prevSingleBullish = pGreen && cdv_p_red && cdvUpperPrev <= WickTolerance && cdvBodyPrev > cdvLowerPrev * MinBodyWickRatio && (p.High < emaPrevVal);
                    prevSingleBearish = pRed && cdv_p_green && cdvLowerPrev <= WickTolerance && cdvBodyPrev > cdvUpperPrev * MinBodyWickRatio && (p.Low > emaPrevVal);
                }

                // Debug: emit per-bar evaluation so we can see why triggers may not occur
                LogDebug($"HIST idx={idx} symbol={signal.Symbol} singleBullish={singleBullish} singleBearish={singleBearish} prevBullish={prevSingleBullish} prevBearish={prevSingleBearish} cdvBody={cdvBody} cdvUpper={cdvUpper} cdvLower={cdvLower} emaSignal={emaSignal}");

                // If a single-bar condition is true but double-trigger is not observed, log which sub-conditions failed
                if ((singleBullish || singleBearish) && !(singleBullish && prevSingleBullish) && !(singleBearish && prevSingleBearish))
                {
                    var parts = new List<string>();
                    if (singleBullish)
                    {
                        parts.Add("singleBullish passed");
                        // enumerate individual checks for bullish
                        bool pGreen = signal.Close > signal.Open;
                        bool wickOk = cdvUpper <= WickTolerance;
                        bool bodyWickOk = cdvBody > cdvLower * MinBodyWickRatio;
                        bool priceBelowEma = signal.High < emaSignal;
                        if (!pGreen) parts.Add("priceGreen failed");
                        if (!cdvRed) parts.Add("cdvRed (CDV candle not red) failed");
                        if (!wickOk) parts.Add($"cdvUpper ({cdvUpper}) > wickTolerance");
                        if (!bodyWickOk) parts.Add($"body/low ratio failed (body={cdvBody}, low={cdvLower})");
                        if (!priceBelowEma) parts.Add($"priceHigh<EMA failed (high={signal.High} ema={emaSignal})");
                    }
                    if (singleBearish)
                    {
                        parts.Add("singleBearish passed");
                        bool pRed = signal.Close < signal.Open;
                        bool wickOk = cdvLower <= WickTolerance;
                        bool bodyWickOk = cdvBody > cdvUpper * MinBodyWickRatio;
                        bool priceAboveEma = signal.Low > emaSignal;
                        if (!pRed) parts.Add("priceRed failed");
                        if (!cdvGreen) parts.Add("cdvGreen (CDV candle not green) failed");
                        if (!wickOk) parts.Add($"cdvLower ({cdvLower}) > wickTolerance");
                        if (!bodyWickOk) parts.Add($"body/upper ratio failed (body={cdvBody}, upper={cdvUpper})");
                        if (!priceAboveEma) parts.Add($"priceLow>EMA failed (low={signal.Low} ema={emaSignal})");
                    }
                    if (parts.Count > 0)
                    {
                        LogDebug($"HIST idx={idx} diagnostics: {string.Join("; ", parts)}");
                    }
                }

                // Check double triggers (per-symbol)
                var sym = signal.Symbol ?? string.Empty;
                if (singleBullish && prevSingleBullish)
                {
                    var active = GetActiveTrigger(sym);
                    if (active?.Direction == TriggerDirection.Bearish)
                    {
                        LogDebug($"HIST idx={idx} cancelling existing bearish trigger due to bullish double trigger");
                        ClearActiveTrigger(sym);
                    }
                    var newTrig = new Trigger { Direction = TriggerDirection.Bullish, Index = idx, CloseTime = signal.CloseTime, Consumed = false };
                    SetActiveTrigger(sym, newTrig);
                    var activeNow = GetActiveTrigger(sym);
                    LogDebug($"HIST idx={idx} Set bullish trigger: {(activeNow != null ? FormatTrigger(activeNow) : "(none)")}");
                }

                if (singleBearish && prevSingleBearish)
                {
                    var active = GetActiveTrigger(sym);
                    if (active?.Direction == TriggerDirection.Bullish)
                    {
                        LogDebug($"HIST idx={idx} cancelling existing bullish trigger due to bearish double trigger");
                        ClearActiveTrigger(sym);
                    }
                    var newTrig = new Trigger { Direction = TriggerDirection.Bearish, Index = idx, CloseTime = signal.CloseTime, Consumed = false };
                    SetActiveTrigger(sym, newTrig);
                    var activeNow2 = GetActiveTrigger(sym);
                    LogDebug($"HIST idx={idx} Set bearish trigger: {(activeNow2 != null ? FormatTrigger(activeNow2) : "(none)")}");
                }

                // Expire triggers older than TriggerExpiryBars (per-symbol)
                var checkActive = GetActiveTrigger(sym);
                if (checkActive != null)
                {
                    var age = idx - checkActive.Index;
                    if (age > TriggerExpiryBars)
                    {
                        LogDebug($"HIST idx={idx} Trigger expired (age={age}): {FormatTrigger(checkActive)}");
                        ClearActiveTrigger(sym);
                    }
                }

                // Entry: whole-candle EMA cross using per-bar EMA
                bool crossesAbove = signal.Low > emaSignal && prev.Low <= emaPrev;
                bool crossesBelow = signal.High < emaSignal && prev.High >= emaPrev;

                var activeForSym = GetActiveTrigger(sym);
                if (crossesAbove && activeForSym != null && activeForSym.Direction == TriggerDirection.Bullish && !activeForSym.Consumed)
                {
                    var age = idx - activeForSym.Index;
                    if (age <= TriggerExpiryBars)
                    {
                        LogDebug($"HIST idx={idx} Placing LONG {signal.Symbol} at {signal.Close} due to EMA crossover and bullish trigger {FormatTrigger(activeForSym)}");
                        await OrderManager.PlaceLongOrderAsync(signal.Symbol ?? string.Empty, signal.Close, "CDVReversal+EMA50", signal.CloseTime);
                        TryConsumeActiveTrigger(sym);
                    }
                }

                if (crossesBelow && activeForSym != null && activeForSym.Direction == TriggerDirection.Bearish && !activeForSym.Consumed)
                {
                    var age = idx - activeForSym.Index;
                    if (age <= TriggerExpiryBars)
                    {
                        LogDebug($"HIST idx={idx} Placing SHORT {signal.Symbol} at {signal.Close} due to EMA crossover and bearish trigger {FormatTrigger(activeForSym)}");
                        await OrderManager.PlaceShortOrderAsync(signal.Symbol ?? string.Empty, signal.Close, "CDVReversal+EMA50", signal.CloseTime);
                        TryConsumeActiveTrigger(sym);
                    }
                }

                if (!string.IsNullOrEmpty(signal.Symbol) && signal.Close > 0)
                {
                    var prices = new Dictionary<string, decimal> { { signal.Symbol, signal.Close } };
                    await OrderManager.CheckAndCloseTrades(prices, signal.CloseTime);
                }
            }

            // Diagnostic: emit active trigger summary after processing historical candles
            LogInfo($"Active trigger summary after historical run: {GetActiveTriggerSummary()}");
        }

        

        private void LogDebug(string message)
        {
            if (!EnableDebugLogging) return;
            Console.WriteLine(message);
        }

        private void LogInfo(string message)
        {
            Console.WriteLine(message);
        }

        private void LogError(string message)
        {
            Console.WriteLine($"Error: {message}");
        }

        // Per-symbol trigger helpers
        private Trigger? GetActiveTrigger(string symbol)
        {
            if (string.IsNullOrEmpty(symbol)) return null;
            if (_activeTriggers.TryGetValue(symbol, out var t)) return t;
            return null;
        }

        private void SetActiveTrigger(string symbol, Trigger? trigger)
        {
            if (string.IsNullOrEmpty(symbol)) return;
            _activeTriggers.AddOrUpdate(symbol, trigger, (k, old) => trigger);
        }

        private void ClearActiveTrigger(string symbol)
        {
            if (string.IsNullOrEmpty(symbol)) return;
            _activeTriggers.TryRemove(symbol, out _);
        }

        private void TryConsumeActiveTrigger(string symbol)
        {
            if (string.IsNullOrEmpty(symbol)) return;
            if (_activeTriggers.TryGetValue(symbol, out var existing) && existing != null)
            {
                var updated = new Trigger { Direction = existing.Direction, Index = existing.Index, CloseTime = existing.CloseTime, Consumed = true };
                _activeTriggers.TryUpdate(symbol, updated, existing);
            }
        }

        // Expose a small diagnostic summary for external callers or for logging
        public string GetActiveTriggerSummary(string? symbol = null)
        {
            if (!string.IsNullOrEmpty(symbol))
            {
                var t = GetActiveTrigger(symbol);
                if (t == null) return "ActiveTrigger: none";
                return "ActiveTrigger: " + FormatTrigger(t);
            }

            // Aggregate summary for all symbols
            var entries = new List<string>();
            foreach (var kv in _activeTriggers)
            {
                if (kv.Value != null) entries.Add($"{kv.Key}=>{FormatTrigger(kv.Value)}");
            }
            return entries.Count > 0 ? "ActiveTriggers: " + string.Join(", ", entries) : "ActiveTrigger: none";
        }

        private void HandleErrorResponse(string symbol, RestResponse response)
        {
            LogError($"Error for {symbol}: {response.ErrorMessage}");
            LogError($"Status Code: {response.StatusCode}");
            LogError($"Content: {response.Content}");
        }
    }
}
