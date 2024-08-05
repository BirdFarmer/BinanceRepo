using BinanceLive.Models;
using Newtonsoft.Json;
using RestSharp;
using Skender.Stock.Indicators;
using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace BinanceLive.Strategies
{
    public class AroonStrategy : StrategyBase
    {
        public AroonStrategy(RestClient client, string apiKey, OrderManager orderManager, Wallet wallet)
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
                request.AddParameter("limit", "750", ParameterType.QueryString);  // Fetch 750 data points

                var response = await Client.ExecuteGetAsync(request);
                if (response.IsSuccessful)
                {
                    var klines = ParseKlines(response.Content);

                    if (klines != null && klines.Count > 0)
                    {
                        var quotes = klines.Select(k => new BinanceLive.Models.Quote
                        {
                            Date = DateTimeOffset.FromUnixTimeMilliseconds(k.OpenTime).UtcDateTime,
                            High = k.High,
                            Low = k.Low,
                            Close = k.Close
                        }).ToList();

                        var aroonResults = Indicator.GetAroon(quotes, 375).ToList();  // Aroon with 125-period
                        var lastSMA = Indicator.GetSma(quotes, 750).LastOrDefault();  // Calculate SMA with 750-period

                        if (lastSMA == null || lastSMA.Sma == null)
                        {
                            Console.WriteLine($"SMA calculation is not available for {symbol}.");
                            return;
                        }

                        bool isSMAAbovePrice = (double)quotes.Last().Low > lastSMA.Sma;
                        bool isSMABelowPrice = (double)quotes.Last().High < lastSMA.Sma;                        

                        int signal = IdentifyAroonSignal(aroonResults);
/*                        if(isSMAAbovePrice)
                            Console.WriteLine($"SMA for {symbol} is {lastSMA.Sma.Value} and Price is {quotes.Last().Low}. \n Bullish, but Aroon is {signal}");
                        if(isSMABelowPrice)
                            Console.WriteLine($"SMA for {symbol} is {lastSMA.Sma.Value} and Price is {quotes.Last().High}. \n Bearish, but Aroon is {signal}");
*/
                        if (signal != 0)
                        {
                            if (signal == 1 && isSMAAbovePrice)
                            {
                                await OrderManager.PlaceLongOrderAsync(symbol, klines.Last().Close, "Aroon");
                            }
                            else if (signal == -1 && isSMABelowPrice)
                            {
                                await OrderManager.PlaceShortOrderAsync(symbol, klines.Last().Close, "Aroon");
                            }
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

        private RestRequest CreateRequest(string resource)
        {
            var request = new RestRequest(resource, Method.Get);
            request.AddHeader("Content-Type", "application/json");
            request.AddHeader("Accept", "application/json");

            return request;
        }

        private int IdentifyAroonSignal(List<AroonResult> aroonResults)
        {
            if (aroonResults.Count < 2)
                return 0;

            var lastAroon = aroonResults[aroonResults.Count - 1];
            var prevAroon = aroonResults[aroonResults.Count - 2];

            if (lastAroon.AroonUp > prevAroon.AroonUp &&
                lastAroon.AroonDown < prevAroon.AroonDown)// &&                lastAroon.AroonUp >= prevAroon.AroonUp &&                )lastAroon.AroonDown <= 0
            {
                return 1; // Bullish crossover
            }
            else if (lastAroon.AroonDown > prevAroon.AroonDown &&
                     lastAroon.AroonUp < prevAroon.AroonUp)// &&     lastAroon.AroonUp <= 0                lastAroon.AroonDown >= prevAroon.AroonDown &&                     )
            {
                return -1; // Bearish crossover
            }

            return 0; // No crossover
        }

        private void LogTradeSignal(string direction, string symbol, decimal price)
        {
            Console.WriteLine($"******Aroon Strategy******************");
            Console.WriteLine($"Go {direction} on {symbol} @ {price} at {DateTime.Now:HH:mm:ss}");
            Console.WriteLine($"**************************************");
        }

        private void HandleErrorResponse(string symbol, RestResponse response)
        {
            Console.WriteLine($"Error for {symbol}: {response.ErrorMessage}");
            Console.WriteLine($"Status Code: {response.StatusCode}");
            Console.WriteLine($"Content: {response.Content}");
        }

        public override async Task RunOnHistoricalDataAsync(IEnumerable<Kline> historicalData)
        {
            var quotes = historicalData.Select(k => new BinanceLive.Models.Quote
            {
                Date = DateTimeOffset.FromUnixTimeMilliseconds(k.OpenTime).UtcDateTime,
                High = k.High,
                Low = k.Low,
                Close = k.Close
            }).ToList();

            var aroonResults = Indicator.GetAroon(quotes, 375).ToList();  // Aroon with 125-period

            foreach (var kline in historicalData)
            {
                var currentQuotes = quotes.TakeWhile(q => q.Date <= DateTimeOffset.FromUnixTimeMilliseconds(kline.OpenTime).UtcDateTime).ToList();

                // Recalculate SMA for the current subset of quotes
                //var currentSMA = Indicator.GetSma(currentQuotes, 750).LastOrDefault();
                var currentSMA = Indicator.GetSma(quotes, 750).LastOrDefault();

                if (currentSMA == null || currentSMA.Sma == null)
                {
                    Console.WriteLine($"Can't get SMA for {kline.Symbol}.");
                    continue;
                }

                bool isSMAAbovePrice = (double)kline.Low > currentSMA.Sma;
                bool isSMABelowPrice = (double)kline.High < currentSMA.Sma;

 //               Console.WriteLine($"SMA for {kline.Symbol} is {currentSMA.Sma.Value} and Price is {kline.Close}. Is Low above SMA? = {isSMAAbovePrice}");

                int signal = IdentifyAroonSignal(aroonResults);

                if (signal != 0)
                {
                    if (signal == 1 && isSMAAbovePrice)
                    {
                        await OrderManager.PlaceLongOrderAsync(kline.Symbol, kline.Close, "Aroon");
                    }
                    else if (signal == -1 && isSMABelowPrice)
                    {
                        await OrderManager.PlaceShortOrderAsync(kline.Symbol, kline.Close, "Aroon");
                    }
                }

                // Update Aroon results for the next iteration
                aroonResults = Indicator.GetAroon(currentQuotes, 125).ToList();

                var currentPrices = new Dictionary<string, decimal> { { kline.Symbol, kline.Close } };
                await OrderManager.CheckAndCloseTrades(currentPrices);
            }
        }
    }
}
