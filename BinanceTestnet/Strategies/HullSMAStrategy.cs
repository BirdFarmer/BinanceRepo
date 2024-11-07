using BinanceTestnet.Models;
using Newtonsoft.Json;
using RestSharp;
using Skender.Stock.Indicators;
using System.Globalization;
using BinanceTestnet.Trading;

namespace BinanceLive.Strategies
{
    public class HullSMAStrategy : StrategyBase
    {
        private const int HullShortLength = 35; // Short Hull length
        private const int HullLongLength = 100; // Long Hull length
        //private const int SmaPeriod = 50; // SMA Period (commented out)

        public HullSMAStrategy(RestClient client, string apiKey, OrderManager orderManager, Wallet wallet)
            : base(client, apiKey, orderManager, wallet)
        {
        }

        public override async Task RunAsync(string symbol, string interval)
        {
            try
            {
                var request = CreateRequest("/fapi/v1/klines");
                request.AddParameter("symbol", symbol, ParameterType.QueryString);
                request.AddParameter("interval", interval, ParameterType.QueryString);
                request.AddParameter("limit", "210", ParameterType.QueryString);  // Fetch 210 data points

                var response = await Client.ExecuteGetAsync(request);
                if (response.IsSuccessful && response.Content != null)
                {
                    var klines = ParseKlines(response.Content);

                    if (klines != null && klines.Count > 1)
                    {
                        var quotes = klines.Select(k => new BinanceTestnet.Models.Quote
                        {
                            Date = DateTimeOffset.FromUnixTimeMilliseconds(k.OpenTime).UtcDateTime,
                            High = k.High,
                            Low = k.Low,
                            Close = k.Close,
                            Open = k.Open,
                            Volume = k.Volume
                        }).ToList();                        
                        
                        var rsi = Indicator.GetRsi(quotes, 20).LastOrDefault().Rsi;

                        // Add a filter for RSI to avoid trades in a "boring" zone (between 40 and 60)
                        bool rsiNotInBoringZone = rsi < 40 || rsi > 60;

                        var hullShortResults = CalculateEHMA(quotes, HullShortLength).ToList();
                        var hullLongResults = CalculateEHMA(quotes, HullLongLength).ToList();

                        var currentKline = klines.Last();
                        var prevKline = klines[klines.Count - 2];
                        var currentHullShort = hullShortResults.LastOrDefault();
                        var currentHullLong = hullLongResults.LastOrDefault();
                        var prevHullShort = hullShortResults[hullShortResults.Count - 2];
                        var prevHullLong = hullLongResults[hullLongResults.Count - 2];
                        
                        if(!rsiNotInBoringZone)
                            return;

                        if (currentHullShort != null && currentHullLong != null && prevHullShort != null && prevHullLong != null)
                        {
                            bool isHullCrossingUp = currentHullShort.EHMA > currentHullLong.EHMA 
                                                    && prevHullShort.EHMA <= prevHullLong.EHMA
                                                    && currentHullLong.EHMA > prevHullLong.EHMA;
                            bool isHullCrossingDown = currentHullShort.EHMA < currentHullLong.EHMA 
                                                      && prevHullShort.EHMA >= prevHullLong.EHMA
                                                      && currentHullLong.EHMA < prevHullLong.EHMA;

                            decimal currentPrice;

                            if (isHullCrossingUp)
                            {
                                Console.WriteLine($"Hull 20 crossing above Hull 100, attempting to go LONG");
                                await OrderManager.PlaceLongOrderAsync(symbol, currentKline.Close, "Hull 20/100", currentKline.CloseTime);
                            }
                            else if (isHullCrossingDown)
                            {
                                Console.WriteLine($"Hull 20 crossing below Hull 100, attempting to go SHORT");
                                await OrderManager.PlaceShortOrderAsync(symbol, currentKline.Close, "Hull 20/100", currentKline.CloseTime);
                            }
                        }
                        else
                        {
                            LogError($"Required indicators data is not available for {symbol}.");
                        }
                    }
                    else
                    {
                        LogError($"Not enough klines data available for {symbol}.");
                    }
                }
                else
                {
                    HandleErrorResponse(symbol, response);
                }
            }
            catch (Exception ex)
            {
                LogError($"Error processing {symbol}: {ex.Message}");
            }
        }

        public override async Task RunOnHistoricalDataAsync(IEnumerable<Kline> historicalCandles)
        {            
            // Process quotes for signals
            var quotes = historicalCandles.Select(k => new BinanceTestnet.Models.Quote
            {
                Date = DateTimeOffset.FromUnixTimeMilliseconds(k.OpenTime).UtcDateTime,
                High = k.High,
                Low = k.Low,
                Close = k.Close
            }).ToList();

            var rsi = Indicator.GetRsi(quotes, 20).LastOrDefault().Rsi;

            var hullShortResults = CalculateEHMA(quotes, HullShortLength).ToList();
            var hullLongResults = CalculateEHMA(quotes, HullLongLength).ToList();
            
            // Iterate over candles to generate signals
            for (int i = HullLongLength - 1; i < historicalCandles.Count(); i++)
            {               
                
                // Add a filter for RSI to avoid trades in a "boring" zone (between 40 and 60)
                bool rsiNotInBoringZone = rsi < 40 || rsi > 60;
                var currentKline = historicalCandles.ElementAt(i);
                var prevKline = historicalCandles.ElementAt(i - 1);
                var currentHullShort = hullShortResults[i];
                var prevHullShort = hullShortResults[i - 1];
                var currentHullLong = hullLongResults[i];
                var prevHullLong = hullLongResults[i - 1];
                
                if(!rsiNotInBoringZone)
                    continue;

                if (currentHullShort != null && currentHullLong != null && prevHullShort != null && prevHullLong != null)
                {
                    bool isHullCrossingUp = currentHullShort.EHMA > currentHullLong.EHMA && prevHullShort.EHMA <= prevHullLong.EHMA && rsi < 40;
                    bool isHullCrossingDown = currentHullShort.EHMA < currentHullLong.EHMA && prevHullShort.EHMA >= prevHullLong.EHMA && rsi > 60;

                    if (isHullCrossingUp)
                    {
                        await OrderManager.PlaceLongOrderAsync(currentKline.Symbol, currentKline.Close, "Hull 20/100", currentKline.CloseTime);
                    }
                    else if (isHullCrossingDown)
                    {
                        await OrderManager.PlaceShortOrderAsync(currentKline.Symbol, currentKline.Close, "Hull 20/100", currentKline.CloseTime);
                    }

                    if (currentKline.Symbol != null && currentKline.Close > 0)
                    {
                        var currentPrices = new Dictionary<string, decimal> { { currentKline.Symbol, currentKline.Close } };
                        await OrderManager.CheckAndCloseTrades(currentPrices);
                    }
                }
            }
        }

        private List<HullSuiteResult> CalculateEHMA(List<BinanceTestnet.Models.Quote> quotes, int length)
        {
            var results = new List<HullSuiteResult>();
            
            var emaShort = Indicator.GetEma(quotes, length / 2).ToList();
            var emaLong = Indicator.GetEma(quotes, length).ToList();
            
            for (int i = 0; i < quotes.Count; i++)
            {
                if (i < length)
                {
                    results.Add(new HullSuiteResult
                    {
                        Date = quotes[i].Date,
                        EHMA = 0,
                        EHMAPrev = 0
                    });
                    continue;
                }

                var ehmaValue = emaShort[i].Ema * 2 - emaLong[i].Ema;
                var ehmaprevValue = i > 0 ? emaShort[i - 1].Ema * 2 - emaLong[i - 1].Ema : ehmaValue;

                results.Add(new HullSuiteResult
                {
                    Date = quotes[i].Date,
                    EHMA = (decimal)ehmaValue!,
                    EHMAPrev = (decimal)ehmaprevValue!
                });
            }

            return results;
        }

        //private List<SmaResult> CalculateSMA(List<BinanceTestnet.Models.Quote> quotes, int period)
        //{
        //    return Indicator.GetSma(quotes, period).ToList();
        //}

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
                            kline.Open = ParseDecimal(k[1]);
                            kline.High = ParseDecimal(k[2]);
                            kline.Low = ParseDecimal(k[3]);
                            kline.Close = ParseDecimal(k[4]);
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
                LogError($"JSON Deserialization error: {ex.Message}");
                return null;
            }
        }
        
        private void LogError(string message)
        {
            Console.WriteLine($"Error: {message}");
        }

        private void HandleErrorResponse(string symbol, RestResponse response)
        {
            LogError($"Error for {symbol}: {response.ErrorMessage}");
            LogError($"Status Code: {response.StatusCode}");
            LogError($"Content: {response.Content}");
        }

               static async Task<decimal> GetCurrentPrice(RestClient client, string symbol)
        {
            var request = new RestRequest("/fapi/v1/ticker/price", Method.Get);
            request.AddParameter("symbol", symbol);
            var response = await client.ExecuteAsync<Dictionary<string, string>>(request);

            if (response.IsSuccessful && response.Content != null)
            {
                var priceData = JsonConvert.DeserializeObject<Dictionary<string, string>>(response.Content);
                if (priceData?.ContainsKey("price") == true)
                {
                    return decimal.Parse(priceData["price"], CultureInfo.InvariantCulture);
                }
            }

            throw new Exception("Failed to get current price");
        }
        
        private decimal ParseDecimal(object value)
        {
            return decimal.TryParse(value?.ToString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var result) ? result : 0;
        }

        private RestRequest CreateRequest(string resource)
        {
            var request = new RestRequest(resource, Method.Get);
            request.AddHeader("Content-Type", "application/json");
            request.AddHeader("Accept", "application/json");

            return request;
        }
    }
}
