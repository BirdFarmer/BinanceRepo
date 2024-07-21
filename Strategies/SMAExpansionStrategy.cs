using BinanceLive.Models;
using Newtonsoft.Json;
using RestSharp;
using Skender.Stock.Indicators;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
public class SMAExpansionStrategy : StrategyBase
{
    private const int ExpansionWindowSize = 2;  // Number of consecutive expansions to track
    private Dictionary<string, Queue<int>> recentExpansions = new Dictionary<string, Queue<int>>();

    public SMAExpansionStrategy(RestClient client, string apiKey, OrderManager orderManager, Wallet wallet)
        : base(client, apiKey, orderManager, wallet)
    {
    }

    public override async Task RunAsync(string symbol, string interval)
    {
        var request = CreateRequest("/api/v3/klines");
        request.AddParameter("symbol", symbol, ParameterType.QueryString);
        request.AddParameter("interval", interval, ParameterType.QueryString);
        request.AddParameter("limit", "401", ParameterType.QueryString);

        var response = await Client.ExecuteGetAsync(request);

        if (response.IsSuccessful)
        {
            var klines = JsonConvert.DeserializeObject<List<List<object>>>(response.Content)
                ?.Select(k =>
                {
                    var kline = new Kline();
                    if (k.Count >= 9)
                    {
                        kline.Open = k[1] != null && decimal.TryParse(k[1].ToString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var open) ? open : 0;
                        kline.High = k[2] != null && decimal.TryParse(k[2].ToString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var high) ? high : 0;
                        kline.Low = k[3] != null && decimal.TryParse(k[3].ToString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var low) ? low : 0;
                        kline.Close = k[4] != null && decimal.TryParse(k[4].ToString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var close) ? close : 0;
                        kline.OpenTime = Convert.ToInt64(k[0]);
                        kline.CloseTime = Convert.ToInt64(k[6]);
                        kline.NumberOfTrades = Convert.ToInt32(k[8]);
                    }
                    return kline;
                })
                .ToList();

            if (klines != null && klines.Count > 0)
            {
                var closes = klines.Select(k => (double)k.Close).ToArray();

                List<Quote> history = closes.Select((close, index) => new Quote
                {
                    Date = DateTimeOffset.FromUnixTimeMilliseconds(klines[index].OpenTime).UtcDateTime,
                    Close = (decimal)close
                }).ToList();

                var sma14 = Indicator.GetSma(history, 14).Where(q => q.Sma.HasValue).Select(q => (double)q.Sma.Value).ToList();
                var sma50 = Indicator.GetSma(history, 50).Where(q => q.Sma.HasValue).Select(q => (double)q.Sma.Value).ToList();
                var sma100 = Indicator.GetSma(history, 100).Where(q => q.Sma.HasValue).Select(q => (double)q.Sma.Value).ToList();
                var sma200 = Indicator.GetSma(history, 200).Where(q => q.Sma.HasValue).Select(q => (double)q.Sma.Value).ToList();

                if (sma200.Count >= 200)
                {
                    int index = sma200.Count - 1;
                    int expansionResult = BinanceTestnet.Indicators.ExpandingAverages.CheckSMAExpansion(sma14, sma50, sma100, sma200, index);

                    // Fetch current price from Binance
                    decimal currentPrice = await GetCurrentPriceFromBinance(symbol);

                    // Call TrackExpansion with currentPrice
                    TrackExpansion(symbol, currentPrice, expansionResult);
                    //PrintRecentExpansions(symbol);
                    
                }
            }
        }
        else
        {
            Console.WriteLine($"Error for {symbol}: {response.ErrorMessage}");
            Console.WriteLine($"Status Code: {response.StatusCode}");
            Console.WriteLine($"Content: {response.Content}");
        }
    }

    private void TrackExpansion(string symbol, decimal currentPrice, int expansionResult)
    {
        UpdateRecentExpansions(symbol, expansionResult);

        // Optionally, you can perform trading logic here based on recent expansions
        CheckTradingConditions(symbol, currentPrice);
    }

    private void UpdateRecentExpansions(string symbol, int expansionResult)
    {
        lock (recentExpansions)
        {
            if (!recentExpansions.ContainsKey(symbol))
            {
                recentExpansions[symbol] = new Queue<int>();
            }

            recentExpansions[symbol].Enqueue(expansionResult);
            
            if (recentExpansions[symbol].Count > ExpansionWindowSize)
            {
                recentExpansions[symbol].Dequeue(); // Remove oldest expansion result
            }
        }
    }

    private void PrintRecentExpansions(string symbol)
    {
        lock (recentExpansions)
        {
            if (recentExpansions.ContainsKey(symbol))
            {
                var recentExpansionResults = recentExpansions[symbol];
                Console.WriteLine($"Recent Expansions for {symbol}: {string.Join(", ", recentExpansionResults)}");
            }
            else
            {
                Console.WriteLine($"No recent expansions tracked for {symbol}");
            }
        }
    }
    private void CheckTradingConditions(string symbol, decimal currentPrice)
    {
        lock (recentExpansions)
        {
            if (recentExpansions.ContainsKey(symbol) && recentExpansions[symbol].Count == ExpansionWindowSize)
            {
                var recentExpansionResults = recentExpansions[symbol];
                bool allLongExpansions = recentExpansionResults.All(r => r == 1);
                bool allShortExpansions = recentExpansionResults.All(r => r == -1);

                if (allLongExpansions)
                {
                    OrderManager.PlaceLongOrder(symbol, currentPrice, "SMAExpansion");
                }
                else if (allShortExpansions)
                {
                    
                    OrderManager.PlaceShortOrder(symbol, currentPrice, "SMAExpansion");
                }
            }

            // After checking for trade placements, always check for closures
            var currentPrices = new Dictionary<string, decimal>
            {
                { symbol, currentPrice }
            };

            OrderManager.CheckAndCloseTrades(currentPrices);
        }
    }


    private RestRequest CreateRequest(string resource)
    {
        var request = new RestRequest(resource, Method.Get);
        request.AddHeader("Content-Type", "application/json");
        request.AddHeader("Accept", "application/json");

        return request;
    }

    private async Task<decimal> GetCurrentPriceFromBinance(string symbol)
    {
        var request = CreateRequest("/api/v3/ticker/price");
        request.AddParameter("symbol", symbol, ParameterType.QueryString);

        var response = await Client.ExecuteGetAsync(request);

        if (response.IsSuccessful)
        {
            var ticker = JsonConvert.DeserializeObject<dynamic>(response.Content);
            decimal price = ticker.price;
            return price;
        }
        else
        {
            Console.WriteLine($"Error fetching current price for {symbol}: {response.ErrorMessage}");
            Console.WriteLine($"Status Code: {response.StatusCode}");
            Console.WriteLine($"Content: {response.Content}");
            return 0; // or handle error appropriately
        }
    }

    public override async Task RunOnHistoricalDataAsync(IEnumerable<Kline> historicalData)
    {
        var sma14 = new List<double>();
        var sma50 = new List<double>();
        var sma100 = new List<double>();
        var sma200 = new List<double>();
        var closes = new List<double>();

        foreach (var kline in historicalData)
        {
            closes.Add((double)kline.Close);
            UpdateSMAs(closes, sma14, sma50, sma100, sma200);

            if (sma200.Count >= 200)
            {
                int index = sma200.Count - 1;
                int expansionResult = BinanceTestnet.Indicators.ExpandingAverages.CheckSMAExpansion(sma14, sma50, sma100, sma200, index);
                TrackExpansion(kline.Symbol, kline.Close, expansionResult);
            }
        }
    }

    private void UpdateSMAs(List<double> closes, List<double> sma14, List<double> sma50, List<double> sma100, List<double> sma200)
    {
        if (closes.Count >= 14) sma14.Add(closes.Skip(closes.Count - 14).Take(14).Average());
        if (closes.Count >= 50) sma50.Add(closes.Skip(closes.Count - 50).Take(50).Average());
        if (closes.Count >= 100) sma100.Add(closes.Skip(closes.Count - 100).Take(100).Average());
        if (closes.Count >= 200) sma200.Add(closes.Skip(closes.Count - 200).Take(200).Average());
    }

    
    private void LogTradeSignal(string direction, string symbol, decimal price)
    {
        Console.WriteLine($"******MACD Divergence Strategy******************");
        Console.WriteLine($"Go {direction} on {symbol} @ {price} at {DateTime.Now:HH:mm:ss}");
        Console.WriteLine($"************************************************");
        //Console.Beep();
    }

    private void HandleErrorResponse(string symbol, RestResponse response)
    {
        Console.WriteLine($"Error for {symbol}: {response.ErrorMessage}");
        Console.WriteLine($"Status Code: {response.StatusCode}");
        Console.WriteLine($"Content: {response.Content}");
    }  
}
