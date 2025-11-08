using BinanceTestnet.Models;
using BinanceTestnet.Trading;
using Newtonsoft.Json;
using RestSharp;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace BinanceTestnet.Strategies
{
public class FVGStrategy : StrategyBase
{
    private readonly int _fvgLookbackPeriod = 36; // Number of periods to look back for FVGs
    private readonly int _orderBookDepthLevels = 1000; // Number of levels to fetch from order book

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
                var closedKlines = klines.Take(klines.Count - 1).ToList();
                var fvgZones = IdentifyFVGs(closedKlines);

                RemoveFulfilledFVGs(fvgZones, closedKlines, symbol);

                if (fvgZones.Count >= 2)
                {
                    var lastFVG = fvgZones.Last();
                    var secondLastFVG = fvgZones[^2];

                    if (lastFVG.Type == secondLastFVG.Type)
                    {
                        var orderBook = await FetchOrderBookAsync(symbol);

                        if (ValidateFVGWithOrderBook(lastFVG, orderBook))
                        {
                            var currentKline = klines.Last();

                            if (lastFVG.Type == FVGType.Bullish)
                            {
                                if (currentKline.Low > lastFVG.UpperBound)
                                {                                    
                                    Console.WriteLine($"Low {currentKline.Low} is entering closest FVG between {lastFVG.LowerBound} and {lastFVG.UpperBound}.");
                                    await OrderManager.PlaceLongOrderAsync(symbol, currentKline.Close, "FVG", currentKline.CloseTime);
                                }
                            }
                            else if (lastFVG.Type == FVGType.Bearish)
                            {
                                if (currentKline.High < lastFVG.LowerBound)
                                {
                                    Console.WriteLine($"High {currentKline.High} is entering closest FVG between {lastFVG.UpperBound} and {lastFVG.LowerBound}.");
                                    await OrderManager.PlaceShortOrderAsync(symbol, currentKline.Close, "FVG", currentKline.CloseTime);
                                }
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
    
    private List<Kline> ConvertQuotesToKlines(List<Quote> quotes)
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
        var quotes = historicalData.Select(k => new Quote
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

                RemoveFulfilledFVGs(fvgZones, historicalKlines, historicalData.ElementAt(i).Symbol);

                if (fvgZones.Count >= 2)
                {
                    var lastFVG = fvgZones.Last();
                    var secondLastFVG = fvgZones[^2];

                    if (lastFVG.Type == secondLastFVG.Type)
                    {
                        if (lastFVG.Type == FVGType.Bullish)
                        {
                            // Long entry condition
                            if (previousQuote.Low >= lastFVG.LowerBound &&
                                previousQuote.Low <= lastFVG.UpperBound &&
                                currentQuote.Low > lastFVG.UpperBound)
                            {
                                await OrderManager.PlaceLongOrderAsync(
                                    historicalData.ElementAt(i + 1).Symbol,
                                    currentQuote.Close,
                                    "FVG",
                                    new DateTimeOffset(currentQuote.Date).ToUnixTimeMilliseconds());
                                continue;
                            }
                        }
                        else if (lastFVG.Type == FVGType.Bearish)
                        {
                            // Short entry condition
                            if (previousQuote.High <= lastFVG.UpperBound &&
                                previousQuote.High >= lastFVG.LowerBound &&
                                currentQuote.High < lastFVG.LowerBound)
                            {
                                await OrderManager.PlaceShortOrderAsync(
                                    historicalData.ElementAt(i + 1).Symbol,
                                    currentQuote.Close,
                                    "FVG",
                                    new DateTimeOffset(currentQuote.Date).ToUnixTimeMilliseconds());
                                continue;    
                            }
                        }
                    }
                }            

                // Check for open trade closing conditions
                var currentPrices = new Dictionary<string, decimal> { { historicalData.ElementAt(i).Symbol, currentQuote.Close } };
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
                    // Invalidate bullish FVG if price closes below its lower boundary
                    if (kline.Low < fvg.LowerBound)
                    {
                        isFulfilled = true;
                        break;
                    }
                }
                else if (fvg.Type == FVGType.Bearish)
                {
                    // Invalidate bearish FVG if price closes above its upper boundary
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

    
    private async Task<Dictionary<string, Dictionary<decimal, decimal>>?> FetchOrderBookAsync(string symbol)
    {
        var request = Helpers.StrategyUtils.CreateGet("/fapi/v1/depth", new Dictionary<string,string>
        {
            {"symbol", symbol},
            {"limit", _orderBookDepthLevels.ToString()}
        });

        var response = await Client.ExecuteGetAsync(request);
        if (response.IsSuccessful && response.Content != null)
        {
            var orderBook = JsonConvert.DeserializeObject<OrderBook>(response.Content);
            if (orderBook == null)
            {
                Console.WriteLine($"Failed to deserialize order book for {symbol}");
                return null;
            }

            var bucketedBids = Helpers.StrategyUtils.BucketOrders(orderBook.Bids);
            var bucketedAsks = Helpers.StrategyUtils.BucketOrders(orderBook.Asks);

            return new Dictionary<string, Dictionary<decimal, decimal>>
            {
                { "Bids", bucketedBids },
                { "Asks", bucketedAsks }
            };
        }
        else
        {
            Console.WriteLine($"Failed to fetch order book for {symbol}: {response.ErrorMessage}");
            return null;
        }
    }

 
    private Dictionary<string, decimal> AnalyzeOrderBook(OrderBook orderBook)
    {
        decimal buyVolume = 0;
        decimal sellVolume = 0;

        foreach (var bid in orderBook.Bids)
        {
            buyVolume += bid[0] * bid[1]; // Price * Quantity
        }

        foreach (var ask in orderBook.Asks)
        {
            sellVolume += ask[0] * ask[1]; // Price * Quantity
        }

        return new Dictionary<string, decimal>
        {
            { "BuyVolume", buyVolume },
            { "SellVolume", sellVolume },
            { "Imbalance", buyVolume - sellVolume }
        };
    }

    private bool ValidateFVGWithOrderBook(FVGZone fvgZone, Dictionary<string, Dictionary<decimal, decimal>>? orderBookData)
    {
        if (orderBookData == null)
            return false;

        if (!orderBookData.TryGetValue("Bids", out var bids) || !orderBookData.TryGetValue("Asks", out var asks))
            return false;

        decimal totalBidVolume = bids.Values.Sum();
        decimal totalAskVolume = asks.Values.Sum();
        decimal totalVolume = totalBidVolume + totalAskVolume;

        if (totalVolume == 0) return false; // Prevent division by zero

        var imbalance = totalBidVolume - totalAskVolume;
        var imbalancePercentage = Math.Abs(imbalance) / totalVolume * 100;

        // Define a significance threshold (e.g., 5% of the total volume)
        const decimal significanceThreshold = 5.0m;

        //Console.WriteLine($"Imbalance: {imbalance}, Total Volume: {totalVolume}, Imbalance %: {imbalancePercentage}");

        var isBullish = fvgZone.Type == FVGType.Bullish;

        // Validate only if imbalance exceeds the significance threshold
        if (imbalancePercentage >= significanceThreshold)
        {
            if (isBullish && imbalance > 0)
            {
                Console.WriteLine("Significant bullish imbalance supports bullish FVG.");
                return true;
            }
            else if (!isBullish && imbalance < 0)
            {
                Console.WriteLine("Significant bearish imbalance supports bearish FVG.");
                return true;
            }
        }

        //Console.WriteLine("Imbalance not significant enough to validate FVG signal.");
        return false;
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
}


public enum FVGType
{
    Bullish,
    Bearish
}


public class OrderBook
{
    [JsonProperty("bids")]
    public List<List<decimal>> Bids { get; set; } = new();

    [JsonProperty("asks")]
    public List<List<decimal>> Asks { get; set; } = new();
}

public class OrderBookEntry
{
    public decimal Price { get; set; }
    public decimal Quantity { get; set; }
}
}
