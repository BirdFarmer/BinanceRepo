using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using BinanceTestnet.Models;
using Newtonsoft.Json;
using RestSharp;
using Skender.Stock.Indicators;
using BinanceTestnet.Trading;

namespace BinanceTestnet.Strategies
{
public class SMAExpansionStrategy : StrategyBase
{
    private const int ExpansionWindowSize = 1;  // Adjusted for more robust detection
    private static ConcurrentDictionary<string, Queue<int>> recentExpansions = new ConcurrentDictionary<string, Queue<int>>();
    private ConcurrentDictionary<string, Queue<int>> _expansionResults;

    public SMAExpansionStrategy(RestClient client, string apiKey, OrderManager orderManager, Wallet wallet)
        : base(client, apiKey, orderManager, wallet)
    {
        _expansionResults = new ConcurrentDictionary<string, Queue<int>>();
    }

    public override async Task RunAsync(string symbol, string interval)
    {
        if (string.IsNullOrEmpty(symbol))
        {
            Console.WriteLine("Error: Symbol is null or empty.");
            return;
        }

        // Fetch klines once
        var request = Helpers.StrategyUtils.CreateGet("/fapi/v1/klines", new Dictionary<string,string>
        {
            {"symbol", symbol},
            {"interval", interval},
            {"limit", "401"}
        });
    var response = await Client.ExecuteGetAsync(request);
        var klines = response.IsSuccessful && response.Content != null
            ? Helpers.StrategyUtils.ParseKlines(response.Content)
            : null;
        if (klines == null || klines.Count == 0)
        {
            Console.WriteLine($"No klines data fetched for {symbol}");
            return;
        }

        var closes = klines.Select(k => k.Close).ToArray();
        var history = ConvertToQuoteList(klines, closes);

        // Parallelize SMA and RSI calculations
        var smaTasks = new[]
        {
            Task.Run(() => Indicator.GetSma(history, 25).Where(q => q.Sma.HasValue).Select(q => q.Sma!.Value).ToList()),
            Task.Run(() => Indicator.GetSma(history, 50).Where(q => q.Sma.HasValue).Select(q => q.Sma!.Value).ToList()),
            Task.Run(() => Indicator.GetSma(history, 100).Where(q => q.Sma.HasValue).Select(q => q.Sma!.Value).ToList()),
            Task.Run(() => Indicator.GetSma(history, 200).Where(q => q.Sma.HasValue).Select(q => q.Sma!.Value).ToList())
        };

        await Task.WhenAll(smaTasks);

        var sma25 = smaTasks[0].Result;
        var sma50 = smaTasks[1].Result;
        var sma100 = smaTasks[2].Result;
        var sma200 = smaTasks[3].Result;

        if (sma200.Count >= 200)
        {
            int index = sma200.Count - 1;

            int expansionResult = BinanceTestnet.Indicators.ExpandingAverages.ConfirmThe200Turn(//CheckSMAExpansionEasy(
                sma25.Select(d => (double)d).ToList(),
                sma50.Select(d => (double)d).ToList(),
                sma100.Select(d => (double)d).ToList(),
                sma200.Select(d => (double)d).ToList(),
                index
            );

            decimal currentPrice = await GetCurrentPriceFromBinance(symbol);
            TrackExpansion(symbol, currentPrice, expansionResult);

            // Check trading conditions after tracking expansion
            await CheckTradingConditions(symbol, currentPrice, klines.Last().OpenTime, sma100[sma100.Count - 1]);
        }
    }


    public override async Task RunOnHistoricalDataAsync(IEnumerable<Kline> historicalData)
    {
        // Lists for storing calculated SMA values
        var sma25 = new List<decimal>();
        var sma50 = new List<decimal>();
        var sma100 = new List<decimal>();
        var sma200 = new List<decimal>();
        var closes = new List<decimal>();

        // Pre-process the entire historical data to avoid repetitive computation
        var klinesArray = historicalData.ToArray();
        closes.AddRange(klinesArray.Select(k => k.Close));

        // Calculate all SMAs in one go, reducing the need for repeated calculations
        var history = ConvertToQuoteList(klinesArray, closes.ToArray());
        var sma25Values = Indicator.GetSma(history, 25).Where(q => q.Sma.HasValue).Select(q => q.Sma!.Value).ToList();
        var sma50Values = Indicator.GetSma(history, 50).Where(q => q.Sma.HasValue).Select(q => q.Sma!.Value).ToList();
        var sma100Values = Indicator.GetSma(history, 100).Where(q => q.Sma.HasValue).Select(q => q.Sma!.Value).ToList();
        var sma200Values = Indicator.GetSma(history, 200).Where(q => q.Sma.HasValue).Select(q => q.Sma!.Value).ToList();

        // Combine the SMA calculation results into the respective lists
        sma25.AddRange(sma25Values.Select(d => (decimal)d));
        sma50.AddRange(sma50Values.Select(d => (decimal)d));
        sma100.AddRange(sma100Values.Select(d => (decimal)d));
        sma200.AddRange(sma200Values.Select(d => (decimal)d));


        // Track expansions only if SMA200 is available
        if (sma200.Count >= 200)
        {
            int smaOffset = 200 - 1; // Offset due to SMA200 calculation, as the first valid SMA200 is at the 200th data point
            
            for (int i = 0; i < sma200.Count; i++) // Use sma200.Count since it's the longest SMA
            {
                int klineIndex = i + smaOffset; // Align SMA indices with the correct kline index

                // Check expansion condition using the values up to the current point
                int expansionResult = BinanceTestnet.Indicators.ExpandingAverages.CheckSMAExpansion(//CheckSMAExpansionEasy(
                    sma25.Take(i + 1).Select(d => (double)d).ToList(),
                    sma50.Take(i + 1).Select(d => (double)d).ToList(),
                    sma100.Take(i + 1).Select(d => (double)d).ToList(),
                    sma200.Take(i + 1).Select(d => (double)d).ToList(),
                    i

                    
                );

                // Retrieve the corresponding kline
                var kline = klinesArray[klineIndex];

                if(kline.Symbol == null) continue;
                
                // Track expansion based on the current kline and expansion result
                TrackExpansion(kline.Symbol, kline.Close, expansionResult);

                // Ensure SMA100 and kline are aligned for checking trading conditions
                await CheckTradingConditions(kline.Symbol, kline.Close, kline.CloseTime, (double)sma100[i]);
            }
        }
    }

    // Request creation and parsing centralized in StrategyUtils

    private List<BinanceTestnet.Models.Quote> ConvertToQuoteList(IEnumerable<Kline> klines, decimal[] closes)
    {
        var quotes = new List<BinanceTestnet.Models.Quote>();
        foreach (var kline in klines)
        {
            quotes.Add(new BinanceTestnet.Models.Quote
            {
                Open = kline.Open,
                High = kline.High,
                Low = kline.Low,
                Close = kline.Close                
            });
        }
        return quotes;
    }

    private void TrackExpansion(string symbol, decimal currentPrice, int expansionResult)
    {
        if (string.IsNullOrEmpty(symbol))
        {
            Console.WriteLine("Error: Symbol is null or empty when tracking expansion.");
            return;
        }

        var expansionsQueue = recentExpansions.GetOrAdd(symbol, new Queue<int>());
        //Console.WriteLine($"Queue count after GetOrAdd for {symbol}: {expansionsQueue.Count}");

        lock (expansionsQueue)
        {
            //Console.WriteLine($"Before Enqueue - Queue count for {symbol}: {expansionsQueue.Count}");

            if (expansionsQueue.Count == ExpansionWindowSize)
            {
                expansionsQueue.Dequeue();
                //Console.WriteLine($"After Dequeue - Queue count for {symbol}: {expansionsQueue.Count}");
            }

            expansionsQueue.Enqueue(expansionResult);
            //Console.WriteLine($"After Enqueue - Updated expansion queue for {symbol}: {string.Join(", ", expansionsQueue)}");
        }

        //CheckTradingConditions(symbol, currentPrice, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()).GetAwaiter().GetResult();
    }

    private async Task CheckTradingConditions(string symbol, decimal currentPrice, long entryTimeStamp, double sma100Value)
    {
        if (string.IsNullOrEmpty(symbol))
        {
            Console.WriteLine("Error: Symbol is null or empty when checking trading conditions.");
            return;
        }

        lock (recentExpansions)
        {
            if (recentExpansions.ContainsKey(symbol) && recentExpansions[symbol].Count == ExpansionWindowSize)
            {
                var recentExpansionResults = recentExpansions[symbol].ToArray();
                bool allLongExpansions = recentExpansionResults.All(r => r == 1);
                bool allShortExpansions = recentExpansionResults.All(r => r == -1);

                if (allLongExpansions)
                {
                    // Confirm the trend with a long-term SMA (e.g., 200-period) and RSI1
                    //if (IsRSIOverbought(symbol))//IsUptrend(symbol) && 
                    //{
                        //Console.WriteLine($"Placing Long Order for {symbol} at {currentPrice}");
                        //OrderManager.PlaceShortOrderAsync(symbol, currentPrice, "SMAExpansion", entryTimeStamp, (decimal)sma100Value).GetAwaiter().GetResult();
                        OrderManager.PlaceLongOrderAsync(symbol, currentPrice, "SMAExpansion", entryTimeStamp, null).GetAwaiter().GetResult();
                        LogTradeSignal("LONG", symbol, currentPrice);
                        
                    //}
                }
                else if (allShortExpansions)
                {
                    // Confirm the trend with a long-term SMA (e.g., 200-period) and RSI
                    //if (IsRSIOversold(symbol))//IsDowntrend(symbol) && 
                    //{                        
                        //Console.WriteLine($"Placing Short Order for {symbol} at {currentPrice}");
                        //OrderManager.PlaceLongOrderAsync(symbol, currentPrice, "SMAExpansion", entryTimeStamp, (decimal)sma100Value).GetAwaiter().GetResult();
                        OrderManager.PlaceShortOrderAsync(symbol, currentPrice, "SMAExpansion", entryTimeStamp, null).GetAwaiter().GetResult();
                        LogTradeSignal("SHORT", symbol, currentPrice);
                    //}
                }
            }
            else
            {
                if (recentExpansions.ContainsKey(symbol))
                {
                    //Console.WriteLine($"Symbol: {symbol}, Not enough expansions to decide trade. Current queue: {string.Join(", ", recentExpansions[symbol])}/{ExpansionWindowSize}");
                }
                else
                {
                    Console.WriteLine($"Symbol: {symbol}, No expansions tracked yet.");
                }
            }

            var currentPrices = new Dictionary<string, decimal> { { symbol, currentPrice } };
            OrderManager.CheckAndCloseTrades(currentPrices, entryTimeStamp).GetAwaiter().GetResult();
        }
    }

    // Request creation centralized in StrategyUtils

    private async Task<decimal> GetCurrentPriceFromBinance(string symbol)
    {
        if (string.IsNullOrEmpty(symbol))
        {
            Console.WriteLine("Error: Symbol is null or empty when getting current price.");
            return 0;
        }

        var request = Helpers.StrategyUtils.CreateGet("/fapi/v1/ticker/price", new Dictionary<string,string>
        {
            {"symbol", symbol}
        });

        var response = await Client.ExecuteGetAsync(request);

        if (response.IsSuccessful && response.Content != null)
        {
            var tickerDict = JsonConvert.DeserializeObject<Dictionary<string, object?>>(response.Content);
            if (tickerDict != null && tickerDict.TryGetValue("price", out var priceObj) && priceObj != null)
            {
                if (decimal.TryParse(priceObj.ToString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
                    return parsed;
            }
            return 0m;
        }
        else
        {
            HandleErrorResponse(symbol, response);
            return 0; // or handle error appropriately
        }
    }
    private bool IsRSIOversold(string symbol)
    {
        if (string.IsNullOrEmpty(symbol))
        {
            Console.WriteLine("Error: Symbol is null or empty when checking RSI oversold.");
            return false;
        }

        var rsi = GetRSI(symbol, 14);
        return rsi < 49;
    }

    private bool IsRSIOverbought(string symbol)
    {
        if (string.IsNullOrEmpty(symbol))
        {
            Console.WriteLine("Error: Symbol is null or empty when checking RSI overbought.");
            return false;
        }

        var rsi = GetRSI(symbol, 14);
        return rsi > 51;
    }

    private decimal GetSMA(List<decimal> closes, int period)
    {
        if (closes.Count < period)
            return 0;

        return closes.Skip(closes.Count - period).Take(period).Average();
    }


    private decimal GetRSI(string symbol, int period)
    {
        if (string.IsNullOrEmpty(symbol))
        {
            Console.WriteLine("Error: Symbol is null or empty when calculating RSI.");
            return 0;
        }

        var request = Helpers.StrategyUtils.CreateGet("/fapi/v1/klines", new Dictionary<string,string>
        {
            {"symbol", symbol},
            {"interval", "1m"},
            {"limit", "401"}
        });
        var response = Client.ExecuteGetAsync(request).Result;
        var klines = response.IsSuccessful && response.Content != null ? Helpers.StrategyUtils.ParseKlines(response.Content) : new List<Kline>();
        var history = ConvertToQuoteList(klines, klines.Select(k => k.Close).ToArray());

        var rsiValues = Indicator.GetRsi(history, period)
            .Where(q => q.Rsi.HasValue)
            .Select(q => q.Rsi ?? 0)
            .ToList();

        return (decimal)rsiValues.LastOrDefault();
    }

    private void UpdateSMAs(List<decimal> closes, List<decimal> sma14, List<decimal> sma50, List<decimal> sma100, List<decimal> sma200)
    {
        sma14.Add(GetSMA(closes, 14));
        sma50.Add(GetSMA(closes, 50));
        sma100.Add(GetSMA(closes, 100));
        sma200.Add(GetSMA(closes, 200));
    }

    private void LogTradeSignal(string direction, string symbol, decimal price)
    {
        Console.WriteLine($"****** Enhanced SMA Expansion Strategy ******************");
        Console.WriteLine($"Go {direction} on {symbol} @ {price} at {DateTime.Now:HH:mm:ss}");
        if(direction == "LONG")
        {
            Console.WriteLine("Condition: `SMA50 > SMA100 > SMA200`");
            Console.WriteLine("All averages (SMA50, SMA100, and SMA200) show positive change over the last index.");
            Console.WriteLine("The shorter-term SMA25 should not indicate an upward change.");
        }            
        else
        {
            Console.WriteLine("Condition: `SMA50 < SMA100 < SMA200`");
            Console.WriteLine("All averages (SMA50, SMA100, and SMA200) show negative change over the last index.");
            Console.WriteLine("The SMA25 should not indicate a downward change.");
        }
            
        Console.WriteLine($"*********************************************************");
    }

    private void HandleErrorResponse(string symbol, RestResponse response)
    {
        Console.WriteLine($"Error for {symbol}: {response.ErrorMessage}");
        Console.WriteLine($"Status Code: {response.StatusCode}");
        Console.WriteLine($"Content: {response.Content}");
    }
}
}

