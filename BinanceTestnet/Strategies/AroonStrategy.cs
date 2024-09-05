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

namespace BinanceLive.Strategies
{
    public class AroonStrategy : StrategyBase
    {
        private const int AroonPeriod = 20; // Aroon Period
        private const int SmaPeriod = 200;  // SMA Period
        private const int HullLength = 70;  // Hull Length for EHMA
        
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
                request.AddParameter("limit", "800", ParameterType.QueryString);  // Fetch 750 data points

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

                        var aroonResults = Indicator.GetAroon(quotes, AroonPeriod).ToList();
                        var smaResults = Indicator.GetSma(quotes, SmaPeriod).ToList();
                        var hullResults = CalculateEHMA(quotes, HullLength).ToList();

                        var currentKline = klines.Last();
                        var prevKline = klines[klines.Count - 2];
                        var currentSMA = smaResults.LastOrDefault();
                        var previousSMA = smaResults.ElementAt(smaResults.Count - 2);
                        var currentAroon = aroonResults.LastOrDefault();
                        var currentHull = hullResults.LastOrDefault();
                        var prevHull = hullResults.Count > 1 ? hullResults[hullResults.Count - 2] : null;

                        if (previousSMA != null && previousSMA.Sma.HasValue 
                            && currentSMA != null && currentSMA.Sma.HasValue 
                            && currentAroon != null && currentHull != null)
                        {
                            bool isPriceAboveSMA = (double)currentKline.Low > currentSMA.Sma;
                            bool isPriceBelowSMA = (double)currentKline.High < currentSMA.Sma;
                            bool isSMAPointingUp = currentSMA.Sma.Value > previousSMA.Sma.Value;
                            bool isSMAPointingDown = currentSMA.Sma.Value < previousSMA.Sma.Value;;

                            int aroonSignal = IdentifyAroonSignal(aroonResults);

                            bool isAroonUptrend = aroonSignal > 0;
                            bool isAroonDowntrend = aroonSignal < 0;

                            bool isHullCrossingUp = prevHull != null && (currentKline.Low > currentHull.EHMA && prevKline.Low <= prevHull.EHMA);
                            bool isHullCrossingDown = prevHull != null && (currentKline.High < currentHull.EHMA && prevKline.High >= prevHull.EHMA);

                            if (isHullCrossingUp 
                                    && isPriceAboveSMA
                                    && isSMAPointingDown)
                            {
                                await OrderManager.PlaceLongOrderAsync(symbol, currentKline.Close, "Aroon + EHMA", currentKline.CloseTime);
                            }
                            else if (isHullCrossingDown
                                    && isPriceBelowSMA
                                    && isSMAPointingUp)
                            {
                                await OrderManager.PlaceShortOrderAsync(symbol, currentKline.Close, "Aroon + EHMA", currentKline.CloseTime);
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


        public override async Task RunOnHistoricalDataAsync(IEnumerable<Kline> historicalData)
        {
            var quotes = historicalData.Select(k => new BinanceTestnet.Models.Quote
            {
                Date = DateTimeOffset.FromUnixTimeMilliseconds(k.OpenTime).UtcDateTime,
                High = k.High,
                Low = k.Low,
                Close = k.Close
            }).ToList();

            var smaResults = Indicator.GetSma(quotes, SmaPeriod).ToList();
            var aroonResults = Indicator.GetAroon(quotes, AroonPeriod).ToList();
            var hullResults = CalculateEHMA(quotes, HullLength).ToList();

            for (int i = SmaPeriod - 1; i < historicalData.Count(); i++)
            {
                var currentKline = historicalData.ElementAt(i);
                var prevKline = historicalData.ElementAt(i - 1);
                var currentSMA = smaResults[i];
                var previousSMA = smaResults[i - 1];
                var currentAroon = aroonResults[i];
                var currentHull = hullResults[i];

                if (currentSMA != null && currentSMA.Sma.HasValue && currentAroon != null
                    && previousSMA != null && previousSMA.Sma.HasValue && currentHull != null)
                {
                    bool isPriceAboveSMA = (double)currentKline.Low > currentSMA.Sma.Value;
                    bool isPriceBelowSMA = (double)currentKline.High < currentSMA.Sma.Value;     
                    bool isSMAPointingUp = previousSMA.Sma.Value < currentSMA.Sma.Value;
                    bool isSMAPointingDown = previousSMA.Sma.Value > currentSMA.Sma.Value;                 

                    int aroonSignal = IdentifyAroonSignal(aroonResults);

                    bool isAroonUptrend = aroonSignal > 0;
                    bool isAroonDowntrend = aroonSignal < 0;

                    bool isHullCrossingUp = currentKline.Low > currentHull.EHMA  && prevKline.Low <= currentHull.EHMAPrev;
                    bool isHullCrossingDown = currentKline.High < currentHull.EHMA  && prevKline.High >= currentHull.EHMAPrev;
                    
                    if(currentKline.Symbol != null)
                    {
                        if (isHullCrossingUp 
                            && isSMAPointingDown 
                            && isPriceAboveSMA)
                        {
                            await OrderManager.PlaceLongOrderAsync(currentKline.Symbol, currentKline.Close, "Aroon + EHMA", currentKline.CloseTime);
                        }
                        else if (isHullCrossingDown 
                                && isSMAPointingUp 
                                && isPriceBelowSMA)
                        {
                            await OrderManager.PlaceShortOrderAsync(currentKline.Symbol, currentKline.Close, "Aroon + EHMA", currentKline.CloseTime);
                        }
                    }
                }

                if (currentKline.Symbol != null && currentKline.Close > 0)
                {
                    var currentPrices = new Dictionary<string, decimal> { { currentKline.Symbol, currentKline.Close } };
                    await OrderManager.CheckAndCloseTrades(currentPrices);
                }
            }
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

        private int IdentifyAroonSignal(List<AroonResult> aroonResults)
        {
            if (aroonResults.Count < 2)
                return 0;

            var lastAroon = aroonResults.Last();
            var prevAroon = aroonResults[aroonResults.Count - 2];

            if (lastAroon.AroonUp > prevAroon.AroonUp)
                return 1; // Bullish 

            if (lastAroon.AroonDown > prevAroon.AroonDown)
                return -1; // Bearish 

            return 0; // No signal
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

    public class HullSuiteResult
    {
        public DateTime Date { get; set; }
        public decimal EHMA { get; set; }
        public decimal EHMAPrev { get; set; }
    }
}
