using BinanceTestnet.Models;
using BinanceTestnet.Trading;
using Newtonsoft.Json;
using RestSharp;
using Skender.Stock.Indicators;
using System.Globalization;

namespace BinanceTestnet.Strategies
{
public class RSIMomentumStrategy : StrategyBase
{
    protected override bool SupportsClosedCandles => true;
    private readonly int _rsiPeriod = 14;
    private readonly int _lookbackPeriod = 150;
    private readonly object _lock = new();

    private static Dictionary<string, string> rsiStateMap = new();

    private readonly Dictionary<string, decimal> previousRsiMap = new();
    // Confirmation and parameterization
    private readonly int _fastRsiPeriod = 7;
    private readonly int _fastRsiLongThreshold = 65;
    private readonly int _fastRsiShortThreshold = 35;
    private readonly int _cooldownCandles = 3;
    private readonly decimal _volumeMultiplier = 1.0m; // require volume >= avgVolume * multiplier
    private readonly int _volumeLookback = 20;
    private readonly Dictionary<string, int> _lastEntryIndexMap = new();

    public RSIMomentumStrategy(RestClient client, string apiKey, OrderManager orderManager, Wallet wallet)
        : base(client, apiKey, orderManager, wallet)
    {
    }

    public override async Task RunAsync(string symbol, string interval)
    {
        try
        {
            var request = Helpers.StrategyUtils.CreateGet("/fapi/v1/klines", new Dictionary<string,string>
            {
                {"symbol", symbol},
                {"interval", interval},
                {"limit", (_lookbackPeriod + 1).ToString()}
            });
            var response = await Client.ExecuteGetAsync(request);
            var klines = response.IsSuccessful && response.Content != null
                ? Helpers.StrategyUtils.ParseKlines(response.Content)
                : null;
            if (klines == null || klines.Count <= _rsiPeriod)
            {
                Console.WriteLine($"[WARNING] Insufficient data for {symbol}.");
                return;
            }

            // Respect closed-candle policy by excluding forming candle for indicator evaluation
            var workingKlines = UseClosedCandles ? Helpers.StrategyUtils.ExcludeForming(klines) : klines;
            if (workingKlines.Count <= _rsiPeriod) return;

            // Build quotes and compute primary and fast RSI for confirmations
            var quotes = workingKlines.Select(k => new BinanceTestnet.Models.Quote
            {
                Date = DateTimeOffset.FromUnixTimeMilliseconds(k.CloseTime).UtcDateTime,
                Open = k.Open,
                High = k.High,
                Low = k.Low,
                Close = k.Close,
                Volume = k.Volume
            }).ToList();

            var rsiResults = Indicator.GetRsi(quotes, _rsiPeriod).ToList();
            var fastRsiResults = Indicator.GetRsi(quotes, _fastRsiPeriod).ToList();

            // Ensure we have indicator results aligned with klines
            if (rsiResults.Count == 0 || fastRsiResults.Count == 0) return;

            // Console.WriteLine($"[DEBUG] RSI State Map Count: {rsiStateMap.Count}");
            //Console.WriteLine($"[DEBUG] RSI State for {symbol}: {(rsiStateMap.ContainsKey(symbol) ? rsiStateMap[symbol] : "NOT SET")}");


            // **Always evaluate first before setting initial state**
            if (rsiStateMap.ContainsKey(symbol))
            {
                await EvaluateRSIConditions(rsiResults, fastRsiResults, symbol, workingKlines);
            }

            // **Now initialize the RSI state if it does not exist**
            if (!rsiStateMap.ContainsKey(symbol))
            {
                InitializeRSIState(symbol, rsiResults, workingKlines);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Processing {symbol}: {ex.Message}");
        }
    }

         public override async Task RunOnHistoricalDataAsync(IEnumerable<Kline> historicalData)
        {
            var quotes = historicalData.Select(k => new BinanceTestnet.Models.Quote
            {
                Date = DateTimeOffset.FromUnixTimeMilliseconds(k.OpenTime).UtcDateTime,
                Close = k.Close
            }).ToList();

            var rsiResults = Indicator.GetRsi(quotes, _rsiPeriod).ToList();
            var fastRsiResults = Indicator.GetRsi(quotes, _fastRsiPeriod).ToList();
            Dictionary<string, string> rsiStateMap = new();

            for (int i = 1; i < rsiResults.Count; i++)
            {
                var lastRsi = rsiResults[i];
                var prevRsi = rsiResults[i - 1];
                var kline = historicalData.ElementAt(i);
                string? symbol = kline.Symbol;
                if (string.IsNullOrEmpty(symbol)) continue;

                if (!rsiStateMap.ContainsKey(symbol))
                    rsiStateMap[symbol] = "neutral";

                if (prevRsi.Rsi < 29 && lastRsi.Rsi >= 29)
                    rsiStateMap[symbol] = "coming_from_oversold";
                else if (prevRsi.Rsi > 71 && lastRsi.Rsi <= 71)
                    rsiStateMap[symbol] = "coming_from_overbought";

                decimal fastRsi = (decimal)fastRsiResults[i].Rsi.GetValueOrDefault();
                // volume confirmation
                bool volumeOk = true;
                try
                {
                    var startIdx = Math.Max(0, i - _volumeLookback);
                    var volAvg = historicalData.Skip(startIdx).Take(_volumeLookback).Select(k => k.Volume).DefaultIfEmpty(0).Average();
                    volumeOk = volAvg == 0 ? true : kline.Volume >= volAvg * _volumeMultiplier;
                }
                catch { volumeOk = true; }

                // enforce cooldown for historical signals per-symbol
                bool cooldownOk = true;
                if (_lastEntryIndexMap.ContainsKey(symbol))
                {
                    var lastIndex = _lastEntryIndexMap[symbol];
                    cooldownOk = (i - lastIndex) >= _cooldownCandles;
                }

                // Long Entry Condition (with confirmations)
                if (rsiStateMap[symbol] == "coming_from_oversold" && lastRsi.Rsi >= 71 && fastRsi >= _fastRsiLongThreshold && volumeOk && cooldownOk)
                {
                    await OrderManager.PlaceLongOrderAsync(symbol, kline.Close, "RSI Momentum", kline.CloseTime);
                    LogTradeSignal("LONG", symbol, kline.Close);
                    rsiStateMap[symbol] = "neutral";
                    _lastEntryIndexMap[symbol] = i;
                }

                // Short Entry Condition (with confirmations)
                else if (rsiStateMap[symbol] == "coming_from_overbought" && lastRsi.Rsi <= 29 && fastRsi <= _fastRsiShortThreshold && volumeOk && cooldownOk)
                {
                    await OrderManager.PlaceShortOrderAsync(symbol, kline.Close, "RSI Momentum", kline.CloseTime);
                    LogTradeSignal("SHORT", symbol, kline.Close);
                    rsiStateMap[symbol] = "neutral";
                    _lastEntryIndexMap[symbol] = i;
                }

                // Check for open trade closing conditions
                var currentPrices = new Dictionary<string, decimal> { { symbol, kline.Close } };
                await OrderManager.CheckAndCloseTrades(currentPrices, kline.CloseTime);
            }
        }

    private void InitializeRSIState(string symbol, List<Skender.Stock.Indicators.RsiResult> rsiResults, List<Kline> klines)
    {
        for (int i = rsiResults.Count - 1; i >= 0; i--)
        {
            var r = rsiResults[i];
            var kline = klines.Count > i ? klines[i] : null;  // May be null if mismatch
            if (r.Rsi == null) continue;
            double rsiD = r.Rsi.GetValueOrDefault();

            if (rsiD >= 71)
            {
                rsiStateMap[symbol] = "OVERBOUGHT";
                return;
            }
            if (rsiD <= 29)
            {
                rsiStateMap[symbol] = "OVERSOLD";
                return;
            }
        }
        rsiStateMap[symbol] = "NEUTRAL";
        //Console.WriteLine($"[STATE] {symbol} is set to NEUTRAL.");
    }

    private async Task EvaluateRSIConditions(List<Skender.Stock.Indicators.RsiResult> rsiResults, List<Skender.Stock.Indicators.RsiResult> fastRsiResults, string symbol, List<Kline> klines)
    {
        lock (_lock)
        {
            if (rsiResults.Count < 2 || fastRsiResults.Count < 2) return; // Ensure we have at least two RSI values

            double previousRsi = rsiResults[^2].Rsi.GetValueOrDefault();  // RSI of previous candle
            double currentRsi = rsiResults[^1].Rsi.GetValueOrDefault();   // RSI of current candle
            string currentState = rsiStateMap.ContainsKey(symbol) ? rsiStateMap[symbol] : "NEUTRAL";

            var prevKline = klines[^2];  // Previous Kline (aligned to rsiValues)
            var currKline = klines[^1];  // Current evaluated Kline (closed if policy requires)

            // // Ensure the trade is placed only in real-time, not historical
            // if (currKline.CloseTime < DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) return;
            if(currentRsi >= 71 || currentRsi <= 29)
                Console.WriteLine($"******** {symbol} at {currKline.Close} with RSI {currentRsi} on {DateTimeOffset.FromUnixTimeMilliseconds(currKline.CloseTime).UtcDateTime}");

            // Check for confirmation filters: fast RSI and volume
            double fastRsiCurrent = fastRsiResults.Count >= 2 && fastRsiResults[^1].Rsi.HasValue ? fastRsiResults[^1].Rsi.GetValueOrDefault() : 0.0;
            bool volumeOk = true;
            try
            {
                var start = Math.Max(0, klines.Count - 1 - _volumeLookback);
                var avgVol = klines.Skip(start).Take(_volumeLookback).Select(k => k.Volume).DefaultIfEmpty(0).Average();
                volumeOk = avgVol == 0 ? true : klines[^1].Volume >= avgVol * _volumeMultiplier;
            }
            catch { volumeOk = true; }

            // enforce cooldown: don't re-enter until _cooldownCandles passed
            bool cooldownOk = true;
            if (_lastEntryIndexMap.ContainsKey(symbol))
            {
                var lastIndex = _lastEntryIndexMap[symbol];
                cooldownOk = (klines.Count - 1 - lastIndex) >= _cooldownCandles;
            }

            if (currentState == "OVERSOLD" && previousRsi < 71 && currentRsi >= 71)
            {
                // require fast RSI and volume confirmation and cooldown
                if (fastRsiCurrent >= _fastRsiLongThreshold && volumeOk && cooldownOk)
                {
                    Console.WriteLine($"[TRADE] Long Entry for {symbol} at {currKline.Close} with RSI {currentRsi} on {DateTimeOffset.FromUnixTimeMilliseconds(currKline.CloseTime).UtcDateTime}");
                    Helpers.StrategyUtils.TraceSignalCandle("RSIMomentum", symbol, UseClosedCandles, currKline, prevKline, "RSI >= 71 from OVERSOLD (confirmed)");
                    Task.Run(async () => await PlaceTradeAsync(symbol, "LONG", currKline.Close, currKline.CloseTime));

                    Console.WriteLine($"[STATE CHANGE] {symbol} changed from OVERSOLD to OVERBOUGHT at RSI {currentRsi}.");
                    rsiStateMap[symbol] = "OVERBOUGHT";
                    _lastEntryIndexMap[symbol] = klines.Count - 1;
                }
            }
            else if (currentState == "OVERBOUGHT" && previousRsi > 29 && currentRsi <= 29)
            {
                // require fast RSI and volume confirmation and cooldown
                if (fastRsiCurrent <= _fastRsiShortThreshold && volumeOk && cooldownOk)
                {
                    Console.WriteLine($"[TRADE] Short Entry for {symbol} at {currKline.Close} with RSI {currentRsi} on {DateTimeOffset.FromUnixTimeMilliseconds(currKline.CloseTime).UtcDateTime}");
                    Helpers.StrategyUtils.TraceSignalCandle("RSIMomentum", symbol, UseClosedCandles, currKline, prevKline, "RSI <= 29 from OVERBOUGHT (confirmed)");
                    Task.Run(async () => await PlaceTradeAsync(symbol, "SHORT", currKline.Close, currKline.CloseTime));

                    Console.WriteLine($"[STATE CHANGE] {symbol} changed from OVERBOUGHT to OVERSOLD at RSI {currentRsi}.");
                    rsiStateMap[symbol] = "OVERSOLD";
                    _lastEntryIndexMap[symbol] = klines.Count - 1;
                }
            }
        }
    }

    private async Task PlaceTradeAsync(string symbol, string side, decimal price, long timestamp)
    {
        try
        {
            if (side == "LONG")
            {
                await OrderManager.PlaceLongOrderAsync(symbol, price, "RSI Momentum", timestamp);
                LogTradeSignal("LONG", symbol, price);
            }
            else if (side == "SHORT")
            {
                await OrderManager.PlaceShortOrderAsync(symbol, price, "RSI Momentum", timestamp);
                LogTradeSignal("LONG", symbol, price);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Order placement for {symbol}: {ex.Message}");
        }
    }

    private async Task<List<decimal>> FetchRSIFromKlinesAsync(List<Kline> klines)
    {
        var quotes = klines.Select(k => new BinanceTestnet.Models.Quote
        {
            Date = DateTimeOffset.FromUnixTimeMilliseconds(k.CloseTime).UtcDateTime,
            Open = k.Open,
            High = k.High,
            Low = k.Low,
            Close = k.Close,
            Volume = k.Volume
        }).ToList();

    var rsiResults = Indicator.GetRsi(quotes, _rsiPeriod).ToList();
    return rsiResults.Where(r => r.Rsi.HasValue).Select(r => (decimal)r.Rsi.GetValueOrDefault()).ToList();
    }

    // Request creation and parsing centralized in StrategyUtils

    private void LogTradeSignal(string direction, string symbol, decimal price)
    {
        Console.WriteLine($"[RSI Momentum] {direction} signal for {symbol} at {price} ({DateTime.UtcNow})");
    }
}
}
