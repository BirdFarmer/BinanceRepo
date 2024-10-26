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
        private const int HullLength = 70; // Hull Length for HMA
        private const int SmaPeriod = 50; // SMA Period
        private const int HigherTimeFrame = 4; // Higher time frame (e.g., 4-hour chart)

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
                request.AddParameter("limit", "210", ParameterType.QueryString);  // Fetch 750 data points

                var response = await Client.ExecuteGetAsync(request);
                if (response.IsSuccessful && response.Content != null)
                {
                    var klines = ParseKlines(response.Content);

                    if (klines != null && klines.Count > 1) // Ensure there are at least two data points
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
                        var smaResults = Indicator.GetSma(quotes, SmaPeriod).ToList();
                        var hullResults = CalculateEHMA(quotes, HullLength).ToList();

                        var currentKline = klines.Last();
                        var prevKline = klines[klines.Count - 2];
                        var currentSMA = smaResults.LastOrDefault();
                        var previousSMA = smaResults.ElementAt(smaResults.Count - 2);
                        var currentHull = hullResults.LastOrDefault();
                        var prevHull = hullResults.Count > 1 ? hullResults[hullResults.Count - 2] : null;

                        if (previousSMA != null && previousSMA.Sma.HasValue 
                            && currentSMA != null && currentSMA.Sma.HasValue 
                            && currentHull != null && prevHull != null)
                        {
                            bool isPriceAboveSMA = (double)currentKline.Low > currentSMA.Sma;
                            bool isPriceBelowSMA = (double)currentKline.High < currentSMA.Sma;
                            bool isSMAPointingUp = currentSMA.Sma.Value > previousSMA.Sma.Value;
                            bool isSMAPointingDown = currentSMA.Sma.Value < previousSMA.Sma.Value;

                            bool isHullCrossingUp = currentHull.EHMA > currentHull.EHMAPrev 
                                                    && prevHull.EHMA <= prevHull.EHMAPrev
                                                    && currentKline.Low > currentHull.EHMA;
                            bool isHullCrossingDown = currentHull.EHMA < currentHull.EHMAPrev 
                                                      && prevHull.EHMA >= prevHull.EHMAPrev
                                                      && currentKline.High < currentHull.EHMA;
                            
                            decimal currentPrice;

                            if (isHullCrossingUp 
                                //&& isPriceBelowSMA
                                && isSMAPointingUp
                                )
                            {
                                
                                Console.WriteLine($"Hull Crossing UP, SMA200 pointing up, trying to go LONG");
                                //currentPrice = await GetCurrentPrice(Client, currentKline.Symbol); 
                                await OrderManager.PlaceLongOrderAsync(symbol, currentKline.Close, "Hull SMA", currentKline.CloseTime);
                            }
                            else if (isHullCrossingDown
                                    //&& isPriceAboveSMA
                                    && isSMAPointingDown
                                    )
                            {
                                Console.WriteLine($"Hull Crossing DOWN, SMA200 pointing down, trying to go SHORT");
                                //currentPrice = await GetCurrentPrice(Client, currentKline.Symbol); 
                                await OrderManager.PlaceShortOrderAsync(symbol, currentKline.Close, "Hull SMA", currentKline.CloseTime);
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
            // Process 15-min quotes for signals
            var quotes = historicalCandles.Select(k => new BinanceTestnet.Models.Quote
            {
                Date = DateTimeOffset.FromUnixTimeMilliseconds(k.OpenTime).UtcDateTime,
                High = k.High,
                Low = k.Low,
                Close = k.Close
            }).ToList();

            var smaResults = Indicator.GetSma(quotes, SmaPeriod).ToList();
            var hullResults = CalculateEHMA(quotes, HullLength).ToList();
            
            // Iterate over 15-minute candles to generate signals
            for (int i = SmaPeriod - 1; i < historicalCandles.Count(); i++)
            {
                var currentKline = historicalCandles.ElementAt(i);
                var prevKline = historicalCandles.ElementAt(i - 1);
                var currentSMA = smaResults[i];
                var previousSMA = smaResults[i - 1];
                var currentHull = hullResults[i];
                var prevHull = hullResults[i - 1];

                if (currentSMA != null && currentSMA.Sma.HasValue 
                    && previousSMA != null && previousSMA.Sma.HasValue 
                    && currentHull != null && prevHull != null)
                {
                    bool isPriceAboveSMA = (double)currentKline.Low > currentSMA.Sma.Value;
                    bool isPriceBelowSMA = (double)currentKline.High < currentSMA.Sma.Value;     
                    bool isSMAPointingUp = previousSMA.Sma.Value < currentSMA.Sma.Value;
                    bool isSMAPointingDown = previousSMA.Sma.Value > currentSMA.Sma.Value;  

                    bool isHullCrossingUp = currentHull.EHMA > currentHull.EHMAPrev && prevHull.EHMA <= prevHull.EHMAPrev;
                    bool isHullCrossingDown = currentHull.EHMA < currentHull.EHMAPrev && prevHull.EHMA >= prevHull.EHMAPrev;
                    
                    // Check if the condition to buy/sell is met based on 15-minute candles
                    if (isHullCrossingUp || isHullCrossingDown)
                    {
                        var signalTime = DateTimeOffset.FromUnixTimeMilliseconds(currentKline.CloseTime).UtcDateTime;

                        if (isHullCrossingUp && isSMAPointingUp)
                        {
                            await OrderManager.PlaceLongOrderAsync(currentKline.Symbol, currentKline.Close, "Hull SMA", currentKline.CloseTime);
                        }
                        else if (isHullCrossingDown && isSMAPointingDown)
                        {
                            await OrderManager.PlaceShortOrderAsync(currentKline.Symbol,  currentKline.Close, "Hull SMA", currentKline.CloseTime);
                        }
                    }                    

                    if (currentKline.Symbol != null && currentKline.Close > 0)
                    {
                        var currentPrices = new Dictionary<string, decimal> { { currentKline.Symbol, currentKline.Close } };
                        await OrderManager.CheckAndCloseTrades(currentPrices);
                    }
                }
            }
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

        private List<HullSuiteResult> CalculateEHMA(List<BinanceTestnet.Models.Quote> quotes, int length)
        {
            var results = new List<HullSuiteResult>();
            
            // Calculate EMA for half-length
            var emaShort = Indicator.GetEma(quotes, length / 2).ToList();
            
            // Calculate EMA for full length
            var emaLong = Indicator.GetEma(quotes, length).ToList();
            
            // Calculate EHMA
            for (int i = 0; i < quotes.Count; i++)
            {
                if (i < length) // Skip until enough data points are available
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
    }
}