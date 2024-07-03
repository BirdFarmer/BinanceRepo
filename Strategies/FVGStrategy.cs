using System.Globalization;
using BinanceLive.Models;
using Newtonsoft.Json;
using RestSharp;

public class FVGStrategy : StrategyBase
{
    public FVGStrategy(RestClient client, string apiKey, OrderManager orderManager, Wallet wallet)
        : base(client, apiKey, orderManager, wallet)
    {
    }

    public override async Task RunAsync(string symbol, string interval)
    {
        try
        {
            var request = CreateRequest("/api/v3/klines");
            request.AddParameter("symbol", symbol, ParameterType.QueryString);
            request.AddParameter("interval", interval, ParameterType.QueryString);
            request.AddParameter("limit", "401", ParameterType.QueryString);

            var response = await Client.ExecuteGetAsync(request);

            if (response.IsSuccessful)
            {
                var klines = ParseKlines(response.Content);

                if (klines != null && klines.Count > 0)
                {
                    var fairValueGaps = IdentifyFairValueGaps(klines);

                    if (fairValueGaps.Any())
                    {
                        var lastGap = fairValueGaps.Last();
                        if (klines.Last().Low < lastGap.Low && klines.Last().High > lastGap.High)
                        {
                            OrderManager.PlaceLongOrder(symbol, klines.Last().Close, "FVG");
                            LogTradeSignal("LONG", symbol, klines.Last().Close, lastGap.Low);
                        }
                    }
                    else
                    {
                        //Console.WriteLine($"No Fair Value Gaps identified for {symbol}.");
                    }
                }
                else
                {
                    Console.WriteLine($"No klines data available for {symbol}.");
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

    private RestRequest CreateRequest(string resource)
    {
        var request = new RestRequest(resource, Method.Get);
        request.AddHeader("Content-Type", "application/json");
        request.AddHeader("Accept", "application/json");

        // Add any additional headers or parameters as needed

        return request;
    }

    private List<Kline>? ParseKlines(string content)
    {
        try
        {
            return JsonConvert.DeserializeObject<List<List<object>>>(content)
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
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"JSON Deserialization error: {ex.Message}");
            return null;
        }
    }

    private List<Kline> IdentifyFairValueGaps(List<Kline> klines)
    {
        var fairValueGaps = new List<Kline>();

        for (int i = 2; i < klines.Count; i++)
        {
            var current = klines[i];
            var previous = klines[i - 1];
            var beforePrevious = klines[i - 2];

            if (current.Low > previous.High && previous.Low > beforePrevious.High)
            {
                fairValueGaps.Add(previous);
            }
        }

        return fairValueGaps;
    }

    private void LogTradeSignal(string direction, string symbol, decimal price, decimal stopLoss)
    {
        Console.WriteLine($"******FVG Strategy***************************");
        Console.WriteLine($"Go {direction} on {symbol} @ {price} at {DateTime.Now:HH:mm:ss}");
        Console.WriteLine($"Stop Loss below {stopLoss}");
        Console.WriteLine($"*********************************************");
        //Console.Beep();
    }

    private void HandleErrorResponse(string symbol, RestResponse response)
    {
        Console.WriteLine($"Error for {symbol}: {response.ErrorMessage}");
        Console.WriteLine($"Status Code: {response.StatusCode}");
        Console.WriteLine($"Content: {response.Content}");
    }
}
