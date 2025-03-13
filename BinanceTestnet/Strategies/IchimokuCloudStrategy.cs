using BinanceTestnet.Models;
using Newtonsoft.Json;
using RestSharp;
using Skender.Stock.Indicators;
using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using BinanceTestnet.Trading;

namespace BinanceTestnet.Strategies
{
    public class IchimokuCloudStrategy : StrategyBase
    {
        public IchimokuCloudStrategy(RestClient client, string apiKey, OrderManager orderManager, Wallet wallet) : base(client, apiKey, orderManager, wallet)
        {
        }

        public override async Task RunOnHistoricalDataAsync(IEnumerable<Kline> historicalData)
        {
            try
            {
                // Convert historical data to Ichimoku input
                var quotes = historicalData.Select(k => new BinanceTestnet.Models.Quote
                {
                    Date = DateTimeOffset.FromUnixTimeMilliseconds(k.OpenTime).UtcDateTime,
                    High = k.High,
                    Low = k.Low,
                    Close = k.Close
                }).ToList();

                // Calculate Ichimoku Cloud components
                var ichimoku = CalculateIchimoku(quotes);

                // Loop through candles and analyze signals
                for (int i = 1; i < historicalData.Count(); i++)
                {
                    var currentKline = historicalData.ElementAt(i);
                    var currentIchimoku = ichimoku[i];
                    var prevIchimoku = ichimoku[i - 1];

                    string symbol = currentKline.Symbol;
                    decimal lastPrice = currentKline.Close;
                    long closeTime = currentKline.CloseTime;

                    // Long entry condition: Price above Kumo, Tenkan-Sen crosses above Kijun-Sen
                    if (lastPrice > currentIchimoku.SenkouSpanA && lastPrice > currentIchimoku.SenkouSpanB &&
                        prevIchimoku.TenkanSen <= prevIchimoku.KijunSen && 
                        currentIchimoku.TenkanSen > currentIchimoku.KijunSen)
                    {
                        await OrderManager.PlaceLongOrderAsync(symbol, lastPrice, "IchimokuCloud", closeTime);
                        LogTradeSignal("LONG", symbol, lastPrice);
                    }
                    // Short entry condition: Price below Kumo, Tenkan-Sen crosses below Kijun-Sen
                    else if (lastPrice < currentIchimoku.SenkouSpanA && lastPrice < currentIchimoku.SenkouSpanB &&
                        prevIchimoku.TenkanSen >= prevIchimoku.KijunSen && 
                        currentIchimoku.TenkanSen < currentIchimoku.KijunSen)
                    {
                        await OrderManager.PlaceShortOrderAsync(symbol, lastPrice, "IchimokuCloud", closeTime);
                        LogTradeSignal("SHORT", symbol, lastPrice);
                    }

                    // Check for open trade closing conditions
                    var currentPrices = new Dictionary<string, decimal> { { symbol, lastPrice } };
                    await OrderManager.CheckAndCloseTrades(currentPrices, currentKline.CloseTime);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during Ichimoku Cloud strategy backtest: {ex.Message}");
            }
        }

        public override async Task RunAsync(string symbol, string interval)
        {
            try
            {
                var request = CreateRequest("/fapi/v1/klines");
                request.AddParameter("symbol", symbol, ParameterType.QueryString);
                request.AddParameter("interval", interval, ParameterType.QueryString);
                request.AddParameter("limit", "400", ParameterType.QueryString);

                var response = await Client.ExecuteGetAsync(request);
                if (response.IsSuccessful && response.Content != null)
                {
                    var klines = ParseKlines(response.Content);

                    if (klines != null && klines.Count > 0)
                    {
                        var quotes = klines.Select(k => new BinanceTestnet.Models.Quote
                        {
                            Date = DateTimeOffset.FromUnixTimeMilliseconds(k.OpenTime).UtcDateTime,
                            High = k.High,
                            Low = k.Low,
                            Close = k.Close
                        }).ToList();

                        var ichimoku = Indicator.GetIchimoku(quotes).ToList();

                        if (ichimoku.Count > 1)
                        {
                            var lastKline = klines.Last(); // Get the most recent Kline
                            var lastIchimoku = ichimoku.Last(); // Get the latest Ichimoku data
                            var prevIchimoku = ichimoku[ichimoku.Count - 2]; // Get the previous Ichimoku data

                            // Long Signal: Price above Kumo, Tenkan-Sen crosses above Kijun-Sen
                            if (lastKline.Close > lastIchimoku.SenkouSpanA &&
                                lastKline.Close > lastIchimoku.SenkouSpanB &&
                                prevIchimoku.TenkanSen <= prevIchimoku.KijunSen && // Tenkan-Sen just crossed above Kijun-Sen
                                lastIchimoku.TenkanSen > lastIchimoku.KijunSen)   // Tenkan-Sen is now above Kijun-Sen
                            {
                                await OrderManager.PlaceLongOrderAsync(symbol, lastKline.Close, "Ichimoku", lastKline.CloseTime);
                                LogTradeSignal("LONG", symbol, lastKline.Close);
                            }

                            // Short Signal: Price below Kumo, Tenkan-Sen crosses below Kijun-Sen
                            else if (lastKline.Close < lastIchimoku.SenkouSpanA &&
                                    lastKline.Close < lastIchimoku.SenkouSpanB &&
                                    prevIchimoku.TenkanSen >= prevIchimoku.KijunSen && // Tenkan-Sen just crossed below Kijun-Sen
                                    lastIchimoku.TenkanSen < lastIchimoku.KijunSen)   // Tenkan-Sen is now below Kijun-Sen
                            {
                                await OrderManager.PlaceShortOrderAsync(symbol, lastKline.Close, "Ichimoku", lastKline.CloseTime);
                                LogTradeSignal("SHORT", symbol, lastKline.Close);
                            }
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
        private List<IchimokuCloud> CalculateIchimoku(List<BinanceTestnet.Models.Quote> quotes)
        {
            var ichimokuList = new List<IchimokuCloud>();
            for (int i = 0; i < quotes.Count; i++)
            {
                var high9 = quotes.Skip(Math.Max(0, i - 6 + 1)).Take(6).Max(q => q.High);
                var low9 = quotes.Skip(Math.Max(0, i - 6 + 1)).Take(6).Min(q => q.Low);
                var high26 = quotes.Skip(Math.Max(0, i - 18 + 1)).Take(18).Max(q => q.High);
                var low26 = quotes.Skip(Math.Max(0, i - 18 + 1)).Take(18).Min(q => q.Low);
                var high52 = quotes.Skip(Math.Max(0, i - 36 + 1)).Take(36).Max(q => q.High);
                var low52 = quotes.Skip(Math.Max(0, i - 36 + 1)).Take(36).Min(q => q.Low);

                var tenkanSen = (high9 + low9) / 2;
                var kijunSen = (high26 + low26) / 2;
                var senkouSpanA = (tenkanSen + kijunSen) / 2;
                var senkouSpanB = (high52 + low52) / 2;

                ichimokuList.Add(new IchimokuCloud
                {
                    TenkanSen = tenkanSen,
                    KijunSen = kijunSen,
                    SenkouSpanA = senkouSpanA,
                    SenkouSpanB = senkouSpanB
                });
            }

            return ichimokuList;
        }
    
        
        private RestRequest CreateRequest(string resource)
        {
            var request = new RestRequest(resource, Method.Get);
            request.AddHeader("Content-Type", "application/json");
            request.AddHeader("Accept", "application/json");

            return request;
        }
        

        private void LogTradeSignal(string direction, string symbol, decimal price)
        {
            Console.WriteLine($"****** Ichimoku Cloud Strategy ******************");
            Console.WriteLine($"Go {direction} on {symbol} @ {price} at {DateTime.Now:HH:mm:ss}");
            if(direction == "LONG")
                Console.WriteLine("Long entry condition: Price above Kumo, Tenkan-Sen crosses above Kijun-Sen");
            else
                Console.WriteLine("Short entry condition: Price below Kumo, Tenkan-Sen crosses below Kijun-Sen");
            Console.WriteLine($"************************************************");
        }

        private void HandleErrorResponse(string symbol, RestResponse response)
        {
            Console.WriteLine($"Error for {symbol}: {response.ErrorMessage}");
            Console.WriteLine($"Status Code: {response.StatusCode}");
            Console.WriteLine($"Content: {response.Content}");
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
    

        public class IchimokuCloud
        {
            public decimal TenkanSen { get; set; }
            public decimal KijunSen { get; set; }
            public decimal SenkouSpanA { get; set; }
            public decimal SenkouSpanB { get; set; }
        }
    }
}
