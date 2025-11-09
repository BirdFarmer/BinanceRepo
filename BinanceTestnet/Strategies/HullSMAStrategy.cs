using BinanceTestnet.Models;
using Newtonsoft.Json;
using RestSharp;
using Skender.Stock.Indicators;
using System.Globalization;
using BinanceTestnet.Trading;

namespace BinanceTestnet.Strategies
{
    public class HullSMAStrategy : StrategyBase
    {
        protected override bool SupportsClosedCandles => true;
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
                var request = Helpers.StrategyUtils.CreateGet("/fapi/v1/klines", new Dictionary<string,string>
                {
                    {"symbol", symbol},
                    {"interval", interval},
                    {"limit", "210"}
                });

                var response = await Client.ExecuteGetAsync(request);
                if (response.IsSuccessful && response.Content != null)
                {
                    var klines = Helpers.StrategyUtils.ParseKlines(response.Content);

                    if (klines != null && klines.Count > 1)
                    {
                        // Build indicator quotes using policy-aware helper
                        var quotes = ToIndicatorQuotes(klines);                        
                        
                        var rsiResult = Indicator.GetRsi(quotes, 20).LastOrDefault();
                        var rsi = rsiResult?.Rsi ?? 50; // neutral fallback

                        // Add a filter for RSI to avoid trades in a "boring" zone (between 40 and 60)
                        bool rsiNotInBoringZone = rsi < 40 || rsi > 60;

                        var hullShortResults = Helpers.StrategyUtils.CalculateEHMA(quotes, HullShortLength)
                            .Select(x => new HullSuiteResult { Date = x.Date, EHMA = x.EHMA, EHMAPrev = x.EHMAPrev}).ToList();
                        var hullLongResults = Helpers.StrategyUtils.CalculateEHMA(quotes, HullLongLength)
                            .Select(x => new HullSuiteResult { Date = x.Date, EHMA = x.EHMA, EHMAPrev = x.EHMAPrev}).ToList();

                        var (signalKline, previousKline) = SelectSignalPair(klines);
                        if (signalKline == null || previousKline == null) return;
                        var currentKline = signalKline;
                        var prevKline = previousKline;
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

                            // currentPrice removed (unused)

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

            var rsiHistResult = Indicator.GetRsi(quotes, 20).LastOrDefault();
            var rsi = rsiHistResult?.Rsi ?? 50; // neutral fallback

            var hullShortResults = Helpers.StrategyUtils.CalculateEHMA(quotes, HullShortLength)
                .Select(x => new HullSuiteResult { Date = x.Date, EHMA = x.EHMA, EHMAPrev = x.EHMAPrev}).ToList();
            var hullLongResults = Helpers.StrategyUtils.CalculateEHMA(quotes, HullLongLength)
                .Select(x => new HullSuiteResult { Date = x.Date, EHMA = x.EHMA, EHMAPrev = x.EHMAPrev}).ToList();
            
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
                        if (!string.IsNullOrEmpty(currentKline.Symbol))
                        {
                            await OrderManager.PlaceLongOrderAsync(currentKline.Symbol, currentKline.Close, "Hull 20/100", currentKline.CloseTime);
                        }
                    }
                    else if (isHullCrossingDown)
                    {
                        if (!string.IsNullOrEmpty(currentKline.Symbol))
                        {
                            await OrderManager.PlaceShortOrderAsync(currentKline.Symbol, currentKline.Close, "Hull 20/100", currentKline.CloseTime);
                        }
                    }

                    if (!string.IsNullOrEmpty(currentKline.Symbol) && currentKline.Close > 0)
                    {
                        var currentPrices = new Dictionary<string, decimal> { { currentKline.Symbol, currentKline.Close } };
                        await OrderManager.CheckAndCloseTrades(currentPrices, currentKline.CloseTime);
                    }
                }
            }
        }

        // EHMA is now provided by StrategyUtils.

        //private List<SmaResult> CalculateSMA(List<BinanceTestnet.Models.Quote> quotes, int period)
        //{
        //    return Indicator.GetSma(quotes, period).ToList();
        //}

        // Parsing centralized in StrategyUtils.
        
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
        
        // Decimal parsing centralized in StrategyUtils.

        // Request creation centralized in StrategyUtils.
    }
}
