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
    

    public FVGStrategy(RestClient client, string apiKey, OrderManager orderManager, Wallet wallet)
        : base(client, apiKey, orderManager, wallet)
    {
    }

    public override async Task RunAsync(string symbol, string interval)
    {
        try
        {
            var klines = await FetchKlinesAsync(symbol, interval);

            if (klines != null && klines.Count > _fvgLookbackPeriod)
            {
                // Always detect FVGs on closed candles only
                var closedKlines = Helpers.StrategyUtils.ExcludeForming(klines);
                var fvgZones = IdentifyFVGs(closedKlines);

                RemoveFulfilledFVGs(fvgZones, closedKlines, symbol);

                if (fvgZones.Count >= 2)
                {
                    var lastFVG = fvgZones.Last();
                    var secondLastFVG = fvgZones[^2];

                    if (lastFVG.Type == secondLastFVG.Type)
                    {
                        // Build indicator quotes respecting closed-candle policy
                        var indicatorQuotes = ToIndicatorQuotes(closedKlines);
                        var emaTrend = Indicator.GetEma(indicatorQuotes, _emaPeriod).ToList();
                        var adxResults = indicatorQuotes.GetAdx(_adxPeriod).ToList();

                        var (signalKline, previousKline) = SelectSignalPair(klines);
                        if (signalKline == null || previousKline == null) return;
                        var currentKline = signalKline;

                        // Map latest EMA/ADX values
                        var lastEma = emaTrend.Count == indicatorQuotes.Count ? emaTrend.Last().Ema : null;
                        var lastAdx = adxResults.Count == indicatorQuotes.Count ? adxResults.Last().Adx : null;

                        bool trendFilterLong = lastEma == null || currentKline.Close > (decimal)lastEma;
                        bool trendFilterShort = lastEma == null || currentKline.Close < (decimal)lastEma;
                        bool adxFilter = lastAdx != null && lastAdx >= (double)_adxThreshold;

                        // Require ADX threshold to ensure meaningful trend and EMA alignment for direction
                        if (!adxFilter) return;

                        if (lastFVG.Type == FVGType.Bullish)
                        {
                            if (currentKline.Low > lastFVG.UpperBound && trendFilterLong)
                            {
                                Console.WriteLine($"Low {currentKline.Low} is entering closest FVG between {lastFVG.LowerBound} and {lastFVG.UpperBound}.");
                                await OrderManager.PlaceLongOrderAsync(symbol, currentKline.Close, "FVG", currentKline.CloseTime);
                            }
                        }
                        else if (lastFVG.Type == FVGType.Bearish)
                        {
                            if (currentKline.High < lastFVG.LowerBound && trendFilterShort)
                            {
                                Console.WriteLine($"High {currentKline.High} is entering closest FVG between {lastFVG.UpperBound} and {lastFVG.LowerBound}.");
                                await OrderManager.PlaceShortOrderAsync(symbol, currentKline.Close, "FVG", currentKline.CloseTime);
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
        var quotes = historicalData.Select(k => new BinanceTestnet.Models.Quote
        {
            Date = DateTimeOffset.FromUnixTimeMilliseconds(k.OpenTime).UtcDateTime,
            High = k.High,
            Low = k.Low,
            Open = k.Open,
            Close = k.Close
        }).ToList();

        if (quotes.Count > _fvgLookbackPeriod)
        {
            for (int i = _fvgLookbackPeriod; i < quotes.Count - 1; i++) // Leave one candle for "current"
            {
                var currentQuote = quotes[i + 1]; // Current candle
                var previousQuote = quotes[i]; // Previous candle
                var historicalKlines = ConvertQuotesToKlines(quotes.Take(i + 1).ToList());
                var fvgZones = IdentifyFVGs(historicalKlines);

                RemoveFulfilledFVGs(fvgZones, historicalKlines, historicalData.ElementAt(i).Symbol!);

                if (fvgZones.Count >= 2)
                {
                    var lastFVG = fvgZones.Last();
                    var secondLastFVG = fvgZones[^2];

                    if (lastFVG.Type == secondLastFVG.Type)
                    {
                        // Build indicator arrays for historical series
                        var emaTrend = Indicator.GetEma(quotes, _emaPeriod).ToList();
                        var adxResults = quotes.GetAdx(_adxPeriod).ToList();

                        var emaVal = emaTrend.Count > i + 1 ? emaTrend[i + 1].Ema : null;
                        var adxVal = adxResults.Count > i + 1 ? adxResults[i + 1].Adx : null;

                        bool trendFilterLong = emaVal == null || currentQuote.Close > (decimal)emaVal;
                        bool trendFilterShort = emaVal == null || currentQuote.Close < (decimal)emaVal;
                        bool adxFilter = adxVal != null && adxVal >= (double)_adxThreshold;

                        if (!adxFilter) { /* skip if trend strength insufficient */ }
                        else if (lastFVG.Type == FVGType.Bullish)
                        {
                            // Long entry condition with trend filters
                            if (previousQuote.Low >= lastFVG.LowerBound &&
                                previousQuote.Low <= lastFVG.UpperBound &&
                                currentQuote.Low > lastFVG.UpperBound &&
                                trendFilterLong)
                            {
                                await OrderManager.PlaceLongOrderAsync(
                                    historicalData.ElementAt(i + 1).Symbol!,
                                    currentQuote.Close,
                                    "FVG",
                                    new DateTimeOffset(currentQuote.Date).ToUnixTimeMilliseconds());
                                continue;
                            }
                        }
                        else if (lastFVG.Type == FVGType.Bearish)
                        {
                            // Short entry condition with trend filters
                            if (previousQuote.High <= lastFVG.UpperBound &&
                                previousQuote.High >= lastFVG.LowerBound &&
                                currentQuote.High < lastFVG.LowerBound &&
                                trendFilterShort)
                            {
                                await OrderManager.PlaceShortOrderAsync(
                                    historicalData.ElementAt(i + 1).Symbol!,
                                    currentQuote.Close,
                                    "FVG",
                                    new DateTimeOffset(currentQuote.Date).ToUnixTimeMilliseconds());
                                continue;    
                            }
                        }
                    }
                }

                // Check for open trade closing conditions
                var currentPrices = new Dictionary<string, decimal> { { historicalData.ElementAt(i).Symbol!, currentQuote.Close } };
                await OrderManager.CheckAndCloseTrades(currentPrices, historicalData.ElementAt(i).OpenTime);
            }
        }
    }

    private List<FVGZone> IdentifyFVGs(List<Kline> klines)
    {
        var fvgZones = new List<FVGZone>();

        for (int i = 2; i < klines.Count; i++)
        {
            var firstKline = klines[i - 2];
            var thirdKline = klines[i];

            if (firstKline.High < thirdKline.Low)
            {
                fvgZones.Add(new FVGZone
                {
                    LowerBound = firstKline.High,
                    UpperBound = thirdKline.Low,
                    Type = FVGType.Bullish,
                    CreationTime = DateTimeOffset.FromUnixTimeMilliseconds(thirdKline.CloseTime).UtcDateTime
                });
            }
            else if (firstKline.Low > thirdKline.High)
            {
                fvgZones.Add(new FVGZone
                {
                    UpperBound = firstKline.Low,
                    LowerBound = thirdKline.High,
                    Type = FVGType.Bearish,
                    CreationTime = DateTimeOffset.FromUnixTimeMilliseconds(thirdKline.CloseTime).UtcDateTime
                });
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

            // Check all candles since the FVG's creation
            foreach (var kline in allKlines.Where(k => DateTimeOffset.FromUnixTimeMilliseconds(k.CloseTime).UtcDateTime > fvg.CreationTime))
            {
                if (fvg.Type == FVGType.Bullish)
                {
                    // Invalidate bullish FVG if a later candle closes below its lower boundary (hard fill)
                    if (kline.Low < fvg.LowerBound)
                    {
                        // hard fill: remove the bullish FVG
                        isFulfilled = true;
                        break;
                    }
                }
                else if (fvg.Type == FVGType.Bearish)
                {
                    // Invalidate bearish FVG if a later candle closes above its upper boundary (hard fill)
                    if (kline.High > fvg.UpperBound)
                    {
                        isFulfilled = true;
                        break;
                    }
                }
            }

            if (isFulfilled)
            {
                //Console.WriteLine($"[REMOVED] {symbol} FVG Zone:  Upper Bound: {fvg.UpperBound}  Lower Bound: {fvg.LowerBound}");
                fvgZones.RemoveAt(i);
            }
        }
    }

    // Bucket and rounding provided by StrategyUtils


    
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
