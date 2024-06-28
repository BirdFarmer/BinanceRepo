// SMAExpansionStrategy.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using BinanceLive.Models;
using BinanceLive.Tools;
using Newtonsoft.Json;
using RestSharp;
using Skender.Stock.Indicators;

namespace BinanceLive.Strategies
{
    public class SMAExpansionStrategy : StrategyBase
    {
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
                        if (expansionResult == 1)
                        {
                            OrderManager.PlaceLongOrder(symbol, (decimal)closes[index]);
                        }

                        if (expansionResult == -1)
                        {
                            OrderManager.PlaceShortOrder(symbol, (decimal)closes[index]);
                        }
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
        
        private RestRequest CreateRequest(string resource)
        {
            var request = new RestRequest(resource, Method.Get);
            request.AddHeader("Content-Type", "application/json");
            request.AddHeader("Accept", "application/json");

            // Add any additional headers or parameters as needed

            return request;
        }
    }
}
