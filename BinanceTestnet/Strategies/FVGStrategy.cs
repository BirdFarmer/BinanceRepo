using BinanceTestnet.Models;
using BinanceTestnet.Trading;
using Newtonsoft.Json;
using RestSharp;
using Skender.Stock.Indicators;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace BinanceTestnet.Strategies
{
public class FVGStrategy : StrategyBase
{
    protected override bool SupportsClosedCandles => true;
    private readonly int _fvgLookbackPeriod = 36; // Number of periods to look back for FVGs
    private readonly int _emaPeriod = 50; // EMA length for trend alignment
    private readonly int _adxPeriod = 14; // ADX period
    private readonly decimal _adxThreshold = 20m; // Minimum ADX required to consider trend
    private readonly int _fvgExpiryBars = 100; // Expire FVGs after this many bars
    

    public FVGStrategy(RestClient client, string apiKey, OrderManager orderManager, Wallet wallet)
        : base(client, apiKey, orderManager, wallet)
    {
    }

    public override async Task RunAsync(string symbol, string interval)
    {
        try
        {
            var klines = await FetchKlinesAsync(symbol, interval);
            if (klines == null || klines.Count <= _fvgLookbackPeriod)
                return;

            // Always detect FVGs on closed candles only
            var closedKlines = Helpers.StrategyUtils.ExcludeForming(klines);
            var fvgZones = IdentifyFVGs(closedKlines, symbol);

            RemoveFulfilledFVGs(fvgZones, closedKlines, symbol);

            if (fvgZones.Count >= 1 && klines.Count >= 3) // Need at least 3 candles now (2 closed + 1 forming)
            {
                // Use current forming candle for entry logic
                // B = klines[^2] (previous closed), C = klines[^1] (current forming)
                var C = klines[^1]; // current forming candle
                var B = klines[^2]; // previous closed candle

                var cOpenTime = DateTimeOffset.FromUnixTimeMilliseconds(C.OpenTime).UtcDateTime;
                var bCloseTime = DateTimeOffset.FromUnixTimeMilliseconds(B.CloseTime).UtcDateTime;

                // Check ALL FVG zones, not just the last one
                foreach (var fvg in fvgZones)
                {
                    // Ensure B and C are after FVG creation
                    if (bCloseTime > fvg.CreationTime && cOpenTime > fvg.CreationTime)
                    {
                        // Build indicators from closed candles only
                        var indicatorQuotes = ToIndicatorQuotes(closedKlines);
                        var emaResults = Indicator.GetEma(indicatorQuotes, _emaPeriod).ToList();
                        var adxResults = indicatorQuotes.GetAdx(_adxPeriod).ToList();

                        // Determine latest EMA and ADX values (be permissive if indicators unavailable)
                        decimal? latestEma = null;
                        decimal? latestAdx = null;
                        var lastEmaRes = emaResults.LastOrDefault();
                        if (lastEmaRes != null && lastEmaRes.Ema.HasValue)
                            latestEma = Convert.ToDecimal(lastEmaRes.Ema.Value);
                        var lastAdxRes = adxResults.LastOrDefault();
                        if (lastAdxRes != null && lastAdxRes.Adx.HasValue)
                            latestAdx = Convert.ToDecimal(lastAdxRes.Adx.Value);

                        bool trendFilterLong = latestEma.HasValue ? (C.Close > latestEma.Value) : true;
                        bool trendFilterShort = latestEma.HasValue ? (C.Close < latestEma.Value) : true;
                        bool adxFilter = latestAdx.HasValue ? (latestAdx.Value >= _adxThreshold) : true;

                        if (!adxFilter) continue;

                        if (fvg.Type == FVGType.Bullish)
                        {
                            // B.Low < upper (wick INTO zone) and C.Low > upper (closed ABOVE zone)
                            if (B.Low < fvg.UpperBound && C.Low > fvg.UpperBound && trendFilterLong)
                            {
                                Console.WriteLine($"[FVG ENTRY] {symbol} LONG — Zone=[{fvg.LowerBound:F6}-{fvg.UpperBound:F6}]");
                                Console.WriteLine($"  B.Low={B.Low:F6} (wicked into zone) | C.Low={C.Low:F6} (closed above) | Entry={C.Close:F6}");
                                Console.WriteLine($"  ZoneCreated: {fvg.CreationTime:MM-dd HH:mm}");
                                await OrderManager.PlaceLongOrderAsync(symbol, C.Close, "FVG", C.CloseTime);
                            }
                        }
                        else // Bearish
                        {
                            // B.High > lower (wick INTO zone) and C.High < lower (closed BELOW zone)
                            if (B.High > fvg.LowerBound && C.High < fvg.LowerBound && trendFilterShort)
                            {
                                Console.WriteLine($"[FVG ENTRY] {symbol} SHORT — Zone=[{fvg.LowerBound:F6}-{fvg.UpperBound:F6}]");
                                Console.WriteLine($"  B.High={B.High:F6} (wicked into zone) | C.High={C.High:F6} (closed below) | Entry={C.Close:F6}");
                                Console.WriteLine($"  ZoneCreated: {fvg.CreationTime:MM-dd HH:mm}");
                                await OrderManager.PlaceShortOrderAsync(symbol, C.Close, "FVG", C.CloseTime);
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error processing {symbol}: {ex.Message}");
        }
    }

    
    private List<Kline> ConvertQuotesToKlines(List<BinanceTestnet.Models.Quote> quotes)
    {
        return quotes.Select(q => new Kline
        {
            OpenTime = ((DateTimeOffset)q.Date).ToUnixTimeMilliseconds(),
            Open = q.Open,
            High = q.High,
            Low = q.Low,
            Close = q.Close,
            CloseTime = ((DateTimeOffset)q.Date.AddSeconds(1)).ToUnixTimeMilliseconds(), // Adjust CloseTime as needed
            Volume = 0, // If volume is unavailable, default to 0
            NumberOfTrades = 0 // Default value for historical data
        }).ToList();
    }

    public override async Task RunOnHistoricalDataAsync(IEnumerable<Kline> historicalData)
    {
        var historicalList = historicalData.ToList();
        
        if (historicalList.Count > _fvgLookbackPeriod)
        {
            for (int i = _fvgLookbackPeriod; i < historicalList.Count - 1; i++) // Leave one candle for "current"
            {
                // Use slices of historical data to simulate real-time
                var historicalSlice = historicalList.Take(i + 1).ToList(); // All data up to current point
                var fvgZones = IdentifyFVGs(historicalSlice, historicalList[i].Symbol!);

                RemoveFulfilledFVGs(fvgZones, historicalSlice, historicalList[i].Symbol!);

                // Declare C here so it's accessible outside the if block
                Kline? C = null;
                Kline? B = null;

                if (fvgZones.Count >= 1 && historicalSlice.Count >= 3) // Still need 3 candles total for FVG detection
                {
                    C = historicalSlice[^1]; // current candle
                    B = historicalSlice[^2]; // previous candle
                    
                    var cCloseTime = DateTimeOffset.FromUnixTimeMilliseconds(C.CloseTime).UtcDateTime;
                    var bCloseTime = DateTimeOffset.FromUnixTimeMilliseconds(B.CloseTime).UtcDateTime;

                    foreach (var fvg in fvgZones)
                    {
                        // Allow entries immediately after FVG creation
                        if (bCloseTime > fvg.CreationTime && cCloseTime > fvg.CreationTime)
                        {
                            // Convert to quotes for indicators
                            var quotes = historicalSlice.Select(k => new BinanceTestnet.Models.Quote
                            {
                                Date = DateTimeOffset.FromUnixTimeMilliseconds(k.OpenTime).UtcDateTime,
                                High = k.High,
                                Low = k.Low,
                                Open = k.Open,
                                Close = k.Close
                            }).ToList();

                            var emaResults = Indicator.GetEma(quotes, _emaPeriod).ToList();
                            var adxResults = quotes.GetAdx(_adxPeriod).ToList();

                            decimal? latestEma = null;
                            decimal? latestAdx = null;
                            var lastEmaRes = emaResults.LastOrDefault();
                            if (lastEmaRes != null && lastEmaRes.Ema.HasValue)
                                latestEma = Convert.ToDecimal(lastEmaRes.Ema.Value);
                            var lastAdxRes = adxResults.LastOrDefault();
                            if (lastAdxRes != null && lastAdxRes.Adx.HasValue)
                                latestAdx = Convert.ToDecimal(lastAdxRes.Adx.Value);

                            bool trendFilterLong = latestEma.HasValue ? (C.Close > latestEma.Value) : true;
                            bool trendFilterShort = latestEma.HasValue ? (C.Close < latestEma.Value) : true;
                            bool adxFilter = latestAdx.HasValue ? (latestAdx.Value >= _adxThreshold) : true;

                            if (!adxFilter) continue;

                            if (fvg.Type == FVGType.Bullish)
                            {
                                // B.Low < upper (wick INTO zone) and C.Low > upper (closed ABOVE zone)
                                if (B.Low < fvg.UpperBound && C.Low > fvg.UpperBound && trendFilterLong)
                                {
                                    Console.WriteLine($"[FVG ENTRY HIST] {historicalList[i].Symbol} LONG — Zone=[{fvg.LowerBound:F6}-{fvg.UpperBound:F6}]");
                                    Console.WriteLine($"  B.Low={B.Low:F6} (wicked into zone) | C.Low={C.Low:F6} (closed above) | Entry={C.Close:F6}");
                                    Console.WriteLine($"  ZoneCreated: {fvg.CreationTime:MM-dd HH:mm}");
                                    await OrderManager.PlaceLongOrderAsync(
                                        historicalList[i].Symbol!,
                                        C.Close,
                                        "FVG",
                                        C.CloseTime);
                                }
                            }
                            else // Bearish
                            {
                                // B.High > lower (wick INTO zone) and C.High < lower (closed BELOW zone)
                                if (B.High > fvg.LowerBound && C.High < fvg.LowerBound && trendFilterShort)
                                {
                                    Console.WriteLine($"[FVG ENTRY HIST] {historicalList[i].Symbol} SHORT — Zone=[{fvg.LowerBound:F6}-{fvg.UpperBound:F6}]");
                                    Console.WriteLine($"  B.High={B.High:F6} (wicked into zone) | C.High={C.High:F6} (closed below) | Entry={C.Close:F6}");
                                    Console.WriteLine($"  ZoneCreated: {fvg.CreationTime:MM-dd HH:mm}");
                                    await OrderManager.PlaceShortOrderAsync(
                                        historicalList[i].Symbol!,
                                        C.Close,
                                        "FVG",
                                        C.CloseTime);
                                }
                            }
                        }
                    }
                }

                // Check for open trade closing conditions - use the current candle price
                var currentCandle = historicalList[i];
                var currentPrices = new Dictionary<string, decimal> { 
                    { currentCandle.Symbol!, currentCandle.Close } 
                };
                await OrderManager.CheckAndCloseTrades(currentPrices, currentCandle.OpenTime);
            }
        }
    }

    private List<FVGZone> IdentifyFVGs(List<Kline> closedKlines, string symbol = "UNKNOWN")
    {
        var fvgZones = new List<FVGZone>();

        // We need at least 3 closed candles to detect an FVG
        for (int i = 2; i < closedKlines.Count; i++)
        {
            // All these are guaranteed to be CLOSED candles
            var candleC = closedKlines[i];      // Most recently closed (just closed)
            var candleB = closedKlines[i-1];    // Previous closed  
            var candleA = closedKlines[i-2];    // Two candles back (oldest)

            // Check if candles are consecutive green/red
            bool threeGreenCandles = candleC.Close > candleC.Open && 
                                    candleB.Close > candleB.Open && 
                                    candleA.Close > candleA.Open;
            
            bool threeRedCandles = candleC.Close < candleC.Open && 
                                candleB.Close < candleB.Open && 
                                candleA.Close < candleA.Open;

            // Bullish FVG: C.Low > A.High AND B.Close > A.High + 3 green candles
            bool bullFvg = candleC.Low > candleA.High && 
                        candleB.Close > candleA.High &&
                        threeGreenCandles && 
                        HasVolumeConfirmation(closedKlines, i);

            // Bearish FVG: C.High < A.Low AND B.Close < A.Low + 3 red candles  
            bool bearFvg = candleC.High < candleA.Low && 
                        candleB.Close < candleA.Low &&
                        threeRedCandles && 
                        HasVolumeConfirmation(closedKlines, i);

            if (bullFvg || bearFvg)
            {
                // Convert timestamps to readable format
                var timeA = DateTimeOffset.FromUnixTimeMilliseconds(candleA.CloseTime).ToString("MM-dd HH:mm:ss");
                var timeB = DateTimeOffset.FromUnixTimeMilliseconds(candleB.CloseTime).ToString("MM-dd HH:mm:ss");
                var timeC = DateTimeOffset.FromUnixTimeMilliseconds(candleC.CloseTime).ToString("MM-dd HH:mm:ss");
                
                // Console.WriteLine($"[FVG DEBUG] {symbol} | A:{timeA} -> B:{timeB} -> C:{timeC}");
                // Console.WriteLine($"[FVG DEBUG] {symbol} | A.High={candleA.High:F6} A.Low={candleA.Low:F6} A.OC={candleA.Close:F6}/{candleA.Open:F6} {(candleA.Close > candleA.Open ? "GREEN" : "RED")}");
                // Console.WriteLine($"[FVG DEBUG] {symbol} | B.Close={candleB.Close:F6} B.OC={candleB.Close:F6}/{candleB.Open:F6} {(candleB.Close > candleB.Open ? "GREEN" : "RED")}");
                // Console.WriteLine($"[FVG DEBUG] {symbol} | C.High={candleC.High:F6} C.Low={candleC.Low:F6} C.OC={candleC.Close:F6}/{candleC.Open:F6} {(candleC.Close > candleC.Open ? "GREEN" : "RED")}");
                // Console.WriteLine($"[FVG DEBUG] {symbol} | 3-Green: {threeGreenCandles} | 3-Red: {threeRedCandles}");
                // Console.WriteLine($"[FVG DEBUG] {symbol} | Bullish: {candleC.Low > candleA.High} && {candleB.Close > candleA.High} && {threeGreenCandles} = {bullFvg}");
                // Console.WriteLine($"[FVG DEBUG] {symbol} | Bearish: {candleC.High < candleA.Low} && {candleB.Close < candleA.Low} && {threeRedCandles} = {bearFvg}");
            }

            if (bullFvg)
            {
                var zone = new FVGZone
                {
                    LowerBound = candleA.Low,    // A.Low
                    UpperBound = candleC.Low,    // C.Low  
                    Type = FVGType.Bullish,
                    CreationTime = DateTimeOffset.FromUnixTimeMilliseconds(candleC.CloseTime).UtcDateTime
                };
                fvgZones.Add(zone);
                
                // Console.WriteLine($"[FVG DETECTED] {symbol} BULLISH | Time: {zone.CreationTime:MM-dd HH:mm}");
                // Console.WriteLine($"  Range: {zone.LowerBound:F6} - {zone.UpperBound:F6} | Gap: {zone.GapSize:F6}");
                // Console.WriteLine($"  CandleA: {DateTimeOffset.FromUnixTimeMilliseconds(candleA.CloseTime):MM-dd HH:mm} | High={candleA.High:F6} Low={candleA.Low:F6} (GREEN)");
                // Console.WriteLine($"  CandleB: {DateTimeOffset.FromUnixTimeMilliseconds(candleB.CloseTime):MM-dd HH:mm} | Close={candleB.Close:F6} (GREEN)");
                // Console.WriteLine($"  CandleC: {DateTimeOffset.FromUnixTimeMilliseconds(candleC.CloseTime):MM-dd HH:mm} | Low={candleC.Low:F6} (GREEN)");
                // Console.WriteLine("---");
            }
            else if (bearFvg)
            {
                var zone = new FVGZone
                {
                    LowerBound = candleC.High,   // C.High
                    UpperBound = candleA.High,   // A.High
                    Type = FVGType.Bearish,
                    CreationTime = DateTimeOffset.FromUnixTimeMilliseconds(candleC.CloseTime).UtcDateTime
                };
                fvgZones.Add(zone);
                
                // Console.WriteLine($"[FVG DETECTED] {symbol} BEARISH | Time: {zone.CreationTime:MM-dd HH:mm}");
                // Console.WriteLine($"  Range: {zone.LowerBound:F6} - {zone.UpperBound:F6} | Gap: {zone.GapSize:F6}");
                // Console.WriteLine($"  CandleA: {DateTimeOffset.FromUnixTimeMilliseconds(candleA.CloseTime):MM-dd HH:mm} | High={candleA.High:F6} Low={candleA.Low:F6} (RED)");
                // Console.WriteLine($"  CandleB: {DateTimeOffset.FromUnixTimeMilliseconds(candleB.CloseTime):MM-dd HH:mm} | Close={candleB.Close:F6} (RED)");
                // Console.WriteLine($"  CandleC: {DateTimeOffset.FromUnixTimeMilliseconds(candleC.CloseTime):MM-dd HH:mm} | High={candleC.High:F6} (RED)");
                // Console.WriteLine("---");
            }
        }

        return fvgZones;
    }

    
    void RemoveFulfilledFVGs(List<FVGZone> fvgZones, List<Kline> allKlines, string symbol)
    {
        if (fvgZones == null || fvgZones.Count == 0 || allKlines == null || allKlines.Count == 0)
            return;
        for (int i = fvgZones.Count - 1; i >= 0; i--)
        {
            var fvg = fvgZones[i];
            bool isFulfilled = false;

            // Get all candles after the FVG's creation time
            var candlesSinceCreation = allKlines
                .Where(k => DateTimeOffset.FromUnixTimeMilliseconds(k.CloseTime).UtcDateTime > fvg.CreationTime)
                .ToList();

            // Expire the zone if we've seen _fvgExpiryBars or more candles since creation
            if (candlesSinceCreation.Count >= _fvgExpiryBars)
            {
                isFulfilled = true; // expired
            }
            else
            {
                // Removal rule: use candle.Close for hard-fill detection
                foreach (var kline in candlesSinceCreation)
                {
                    if (fvg.Type == FVGType.Bullish)
                    {
                        // Remove bullish FVG if a later candle CLOSES below its lower boundary
                        if (kline.Close < fvg.LowerBound)
                        {
                            isFulfilled = true;
                            break;
                        }
                    }
                    else if (fvg.Type == FVGType.Bearish)
                    {
                        // Remove bearish FVG if a later candle CLOSES above its upper boundary
                        if (kline.Close > fvg.UpperBound)
                        {
                            isFulfilled = true;
                            break;
                        }
                    }
                }
            }

            if (isFulfilled)
            {
                fvgZones.RemoveAt(i);
            }
        }
    }
    
    private bool HasVolumeConfirmation(List<Kline> closedKlines, int currentIndex)
    {
        if (closedKlines.Count < 21) return true; // Not enough data, be permissive
        
        var volumeB = closedKlines[currentIndex - 1].Volume; // B candle volume
        var recentVolumes = closedKlines.Skip(closedKlines.Count - 21).Take(20).Select(k => k.Volume).ToList();
        var volumeMA20 = recentVolumes.Average();
        
        return volumeB >= volumeMA20 * 2m; // B volume >= 2x 20-period average
    }

    
    // Parsing and request creation centralized in StrategyUtils
    private async Task<List<Kline>?> FetchKlinesAsync(string symbol, string interval)
    {
        var request = Helpers.StrategyUtils.CreateGet("/fapi/v1/klines", new Dictionary<string,string>
        {
            {"symbol", symbol},
            {"interval", interval},
            {"limit", (_fvgLookbackPeriod + 1).ToString()}
        });

        var response = await Client.ExecuteGetAsync(request);
        if (response.IsSuccessful && response.Content != null)
        {
            var klines = Helpers.StrategyUtils.ParseKlines(response.Content);
            return klines;
        }
        else
        {
            Console.WriteLine($"Failed to fetch klines for {symbol}: {response.ErrorMessage}");
            return null;
        }
    }
}

public class FVGZone
{
    public decimal LowerBound { get; set; }
    public decimal UpperBound { get; set; }
    public FVGType Type { get; set; } // Indicates if the FVG is bullish or bearish
    public DateTime CreationTime { get; set; } // New property to track when the FVG was created
    // Optional diagnostics
    public decimal GapSize => UpperBound - LowerBound;
}


public enum FVGType
{
    Bullish,
    Bearish
}



}
