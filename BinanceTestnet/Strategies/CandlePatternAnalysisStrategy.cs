using BinanceTestnet.Models;
using RestSharp;
using Skender.Stock.Indicators;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using BinanceTestnet.Trading;

namespace BinanceTestnet.Strategies
{
    public class CandlePatternAnalysisStrategy : StrategyBase
    {
        // Strategy parameters (tweakable via UI)
        public static bool UseVolumeFilter = true;
        public static bool UseEmaFilter = false;
        public static decimal IndecisiveThreshold = 0.3m; // 30%
        public static int VolumeMALength = 20;
        public static int EmaLength = 50;
        // Enable verbose debug output for RunAsync (false by default)
        public static bool DebugMode = true;
        // Number of extra candles to request beyond the minimum required for indicators
        // Increase this if users may choose large EMA lengths (e.g. EMA100+)
        public static int CandleFetchBuffer = 150;

        protected override bool SupportsClosedCandles => true;

        public CandlePatternAnalysisStrategy(RestClient client, string apiKey, OrderManager orderManager, Wallet wallet)
            : base(client, apiKey, orderManager, wallet)
        {
        }

        public override async Task RunAsync(string symbol, string interval)
        {
            try
            {
                // Calculate minimal candles required for indicators and pattern detection.
                // We need at least: 3 indecisive bars + 1 signal bar, plus enough bars to compute volume SMA and EMA.
                int required = Math.Max(VolumeMALength, EmaLength);
                int buffer = CandleFetchBuffer; // extra bars for indicator warm-up and stability
                int limit = Math.Min(Math.Max(required + buffer, 4), 1000); // clamp to exchange limits

                if (DebugMode)
                {
                    Console.WriteLine($"[CandlePattern] requesting {limit} klines (required={required}, buffer={buffer})");
                }

                var request = Helpers.StrategyUtils.CreateGet("/fapi/v1/klines", new Dictionary<string, string>
                {
                    { "symbol", symbol },
                    { "interval", interval },
                    { "limit", limit.ToString() }
                });

                decimal lastPrice = 0;

                var response = await Client.ExecuteGetAsync(request);
                if (response.IsSuccessful && response.Content != null)
                {
                    var klines = Helpers.StrategyUtils.ParseKlines(response.Content);
                    if (klines != null && klines.Count > 4)
                    {
                        var (signalKline, previousKline) = SelectSignalPair(klines);
                        if (DebugMode)
                        {
                            Console.WriteLine($"[CandlePattern] {symbol} - loaded {klines.Count} klines; UseClosedCandles={UseClosedCandles}");
                        }
                        if (signalKline == null || previousKline == null) return;

                        var idx = klines.IndexOf(signalKline);
                        if (DebugMode)
                        {
                            Console.WriteLine($"[CandlePattern] signalKline index={idx}, time={DateTimeOffset.FromUnixTimeMilliseconds(signalKline.OpenTime)}");
                        }
                        if (idx < 3) return; // need at least 3 prior candles

                        if (TryDetectSignal(klines, idx, out var signal, out var rangeHigh, out var rangeLow))
                        {
                            if (DebugMode)
                            {
                                Console.WriteLine($"[CandlePattern] TryDetectSignal => {signal} (rangeHigh={rangeHigh}, rangeLow={rangeLow}, close={signalKline.Close})");
                            }
                            // Volume filter (use SMA of previous VolumeMALength bars, excluding current)
                            bool volOk = true;
                            var currVol = signalKline.Volume;
                            if (UseVolumeFilter)
                            {
                                int start = Math.Max(0, idx - VolumeMALength);
                                var avgVol = klines.Skip(start).Take(VolumeMALength).Select(k => k.Volume).DefaultIfEmpty(0m).Average();
                                volOk = avgVol == 0 ? true : currVol > avgVol;
                                if (DebugMode)
                                {
                                    Console.WriteLine($"[CandlePattern] Volume check: currVol={currVol}, avgVol({VolumeMALength})={avgVol}, volOk={volOk}");
                                }
                            }

                            // EMA filter
                            bool emaOk = true;
                            if (UseEmaFilter)
                            {
                                var quotes = ToIndicatorQuotes(klines);
                                var ema = Indicator.GetEma(quotes, EmaLength).ToList();
                                // ensure ema aligns
                                if (ema.Count > idx && ema[idx]?.Ema != null)
                                {
                                    var currEma = (decimal)ema[idx].Ema.GetValueOrDefault();
                                    if (signal == "BULL") emaOk = signalKline.Close > currEma;
                                    else if (signal == "BEAR") emaOk = signalKline.Close < currEma;
                                    if (DebugMode)
                                    {
                                        Console.WriteLine($"[CandlePattern] EMA check: EMA{EmaLength}={currEma}, price={signalKline.Close}, emaOk={emaOk}");
                                    }
                                }
                            }

                            if (volOk && emaOk)
                            {
                                if (signal == "BULL")
                                {
                                    await OrderManager.PlaceLongOrderAsync(symbol, signalKline.Close, "CandlePattern", signalKline.OpenTime);
                                    LogTradeSignal("LONG", symbol, signalKline.Close, signalKline.Volume, rangeHigh, rangeLow);
                                    lastPrice = signalKline.Close;
                                }
                                else if (signal == "BEAR")
                                {
                                    await OrderManager.PlaceShortOrderAsync(symbol, signalKline.Close, "CandlePattern", signalKline.OpenTime);
                                    LogTradeSignal("SHORT", symbol, signalKline.Close, signalKline.Volume, rangeHigh, rangeLow);
                                    lastPrice = signalKline.Close;
                                }
                            }
                        }

                        if (lastPrice > 0)
                        {
                            var currentPrices = new Dictionary<string, decimal> { { symbol, lastPrice } };
                            await OrderManager.CheckAndCloseTrades(currentPrices);
                        }
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

        public override async Task RunOnHistoricalDataAsync(IEnumerable<Kline> historicalData)
        {
            var klines = historicalData.ToList();

            // Precompute EMA if needed
            var quotes = ToIndicatorQuotes(klines);
            var ema = UseEmaFilter ? Indicator.GetEma(quotes, EmaLength).ToList() : null;

            for (int i = 3; i < klines.Count; i++)
            {
                var current = klines[i];
                if (TryDetectSignal(klines, i, out var signal, out var rangeHigh, out var rangeLow))
                {
                    bool volOk = true;
                    if (UseVolumeFilter)
                    {
                        int start = Math.Max(0, i - VolumeMALength);
                        var avgVol = klines.Skip(start).Take(VolumeMALength).Select(k => k.Volume).DefaultIfEmpty(0m).Average();
                        volOk = avgVol == 0 ? true : current.Volume > avgVol;
                    }

                    bool emaOk = true;
                    if (UseEmaFilter && ema != null)
                    {
                        if (ema.Count > i && ema[i]?.Ema != null)
                        {
                            var currEma = (decimal)ema[i].Ema.GetValueOrDefault();
                            if (signal == "BULL") emaOk = current.Close > currEma;
                            else if (signal == "BEAR") emaOk = current.Close < currEma;
                        }
                    }

                    if (volOk && emaOk)
                    {
                        if (signal == "BULL")
                        {
                            await OrderManager.PlaceLongOrderAsync(current.Symbol!, current.Close, "CandlePattern", current.OpenTime);
                            LogTradeSignal("LONG", current.Symbol!, current.Close, current.Volume, rangeHigh, rangeLow);
                        }
                        else if (signal == "BEAR")
                        {
                            await OrderManager.PlaceShortOrderAsync(current.Symbol!, current.Close, "CandlePattern", current.OpenTime);
                            LogTradeSignal("SHORT", current.Symbol!, current.Close, current.Volume, rangeHigh, rangeLow);
                        }
                    }

                    var currentPrices = new Dictionary<string, decimal> { { current.Symbol!, current.Close } };
                    await OrderManager.CheckAndCloseTrades(currentPrices, current.OpenTime);
                }
            }
        }

        // Public helper to allow unit tests to assert detection on synthetic data
        public static bool TryDetectSignal(IReadOnlyList<Kline> klines, int idx, out string signal, out decimal rangeHigh, out decimal rangeLow)
        {
            signal = string.Empty;
            rangeHigh = 0m; rangeLow = 0m;
            if (klines == null || idx < 3 || idx >= klines.Count) return false;

            if (DebugMode)
            {
                var window = string.Join(" | ", Enumerable.Range(Math.Max(0, idx - 5), Math.Min(6, klines.Count - Math.Max(0, idx - 5))).Select(i =>
                {
                    var k = klines[i];
                    return $"[{i}] O:{k.Open} H:{k.High} L:{k.Low} C:{k.Close} V:{k.Volume}";
                }));
                Console.WriteLine($"[CandlePattern] TryDetectSignal idx={idx}, sample window: {window}");
            }

            // check previous 3 bars are indecisive
            bool indec1 = IsIndecisive(klines[idx - 1]);
            bool indec2 = IsIndecisive(klines[idx - 2]);
            bool indec3 = IsIndecisive(klines[idx - 3]);
            bool currIndec = IsIndecisive(klines[idx]);

            if (DebugMode)
            {
                Console.WriteLine($"[CandlePattern] indecisive checks (idx-3..idx): {indec3},{indec2},{indec1},{currIndec} (currIndec should be false)");
                var b1 = Math.Abs(klines[idx - 1].Close - klines[idx - 1].Open);
                var r1 = klines[idx - 1].High - klines[idx - 1].Low;
                var b2 = Math.Abs(klines[idx - 2].Close - klines[idx - 2].Open);
                var r2 = klines[idx - 2].High - klines[idx - 2].Low;
                var b3 = Math.Abs(klines[idx - 3].Close - klines[idx - 3].Open);
                var r3 = klines[idx - 3].High - klines[idx - 3].Low;
                Console.WriteLine($"[CandlePattern] bodies/ranges: idx-3 body={b3} range={r3} ; idx-2 body={b2} range={r2} ; idx-1 body={b1} range={r1}");
            }

            if (indec1 && indec2 && indec3 && !currIndec)
            {
                rangeHigh = Math.Max(klines[idx - 1].High, Math.Max(klines[idx - 2].High, klines[idx - 3].High));
                rangeLow = Math.Min(klines[idx - 1].Low, Math.Min(klines[idx - 2].Low, klines[idx - 3].Low));

                var close = klines[idx].Close;
                if (DebugMode)
                {
                    Console.WriteLine($"[CandlePattern] computed rangeHigh={rangeHigh}, rangeLow={rangeLow}, currentClose={close}");
                }
                if (close > rangeHigh)
                {
                    signal = "BULL";
                    if (DebugMode) Console.WriteLine($"[CandlePattern] signal=BULL at idx={idx}");
                    return true;
                }
                if (close < rangeLow)
                {
                    signal = "BEAR";
                    if (DebugMode) Console.WriteLine($"[CandlePattern] signal=BEAR at idx={idx}");
                    return true;
                }
                if (DebugMode) Console.WriteLine($"[CandlePattern] close did not break range (close={close})");
            }
            return false;
        }

        private static bool IsIndecisive(Kline k)
        {
            var range = k.High - k.Low;
            if (range <= 0) return false;
            var body = Math.Abs(k.Close - k.Open);
            return (body / range) < IndecisiveThreshold;
        }

        private void LogTradeSignal(string direction, string symbol, decimal price, decimal currentVolume, decimal rangeHigh, decimal rangeLow)
        {
            Console.WriteLine("****** Candle Pattern Breakout ******************");
            Console.WriteLine($"{direction}: {symbol} @ {price} (Broken {(direction=="LONG"?"above":"below")} { (direction=="LONG"? rangeHigh: rangeLow) } range)");
            Console.WriteLine($"Volume: {currentVolume} {(UseVolumeFilter?"> SMA":"(Volume filter disabled)")}");
            if (UseEmaFilter)
            {
                Console.WriteLine($"EMA{EmaLength}: (EMA filter enabled)");
            }
            Console.WriteLine("************************************************");
        }

        private void HandleErrorResponse(string symbol, RestResponse response)
        {
            Console.WriteLine($"Error for {symbol}: {response.ErrorMessage}");
            Console.WriteLine($"Status Code: {response.StatusCode}");
            Console.WriteLine($"Content: {response.Content}");
        }
    }
}
