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
    public class AroonStrategy : StrategyBase
    {
        protected override bool SupportsClosedCandles => true;
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
                var request = Helpers.StrategyUtils.CreateGet("/fapi/v1/klines", new Dictionary<string,string>
                {
                    {"symbol", symbol},
                    {"interval", interval},
                    {"limit", "800"}
                });
                var response = await Client.ExecuteGetAsync(request);
                if (response.IsSuccessful && response.Content != null)
                {
                    var klines = Helpers.StrategyUtils.ParseKlines(response.Content);

                    if (klines != null && klines.Count > 1) // Ensure there are at least two data points
                    {
                        // Build indicator quotes respecting policy
                        var quotes = ToIndicatorQuotes(klines);

                        var aroonResults = Indicator.GetAroon(quotes, AroonPeriod).ToList();
                        var smaResults = Indicator.GetSma(quotes, SmaPeriod).ToList();
                        var hullResults = Helpers.StrategyUtils.CalculateEHMA(quotes, HullLength)
                            .Select(hr => new HullSuiteResult { Date = hr.Date, EHMA = hr.EHMA, EHMAPrev = hr.EHMAPrev })
                            .ToList();

                        var (signalKline, previousKline) = SelectSignalPair(klines);
                        if (signalKline == null || previousKline == null) return;
                        var currentKline = signalKline;
                        var prevKline = previousKline;
                        var currentSMA = smaResults.LastOrDefault();
                        var previousSMA = smaResults.ElementAt(smaResults.Count - 2);
                        var currentAroon = aroonResults.LastOrDefault();
                        var currentHull = hullResults.LastOrDefault();
                        var prevHull = hullResults.Count > 1 ? hullResults[hullResults.Count - 2] : null;

                        if (previousSMA != null && previousSMA.Sma.HasValue 
                            && currentSMA != null && currentSMA.Sma.HasValue 
                            && currentAroon != null 
                            && currentHull != null && prevHull != null)
                        {
                            bool isPriceAboveSMA = (double)currentKline.Low > currentSMA.Sma;
                            bool isPriceBelowSMA = (double)currentKline.High < currentSMA.Sma;
                            bool isSMAPointingUp = currentSMA.Sma.Value > previousSMA.Sma.Value;
                            bool isSMAPointingDown = currentSMA.Sma.Value < previousSMA.Sma.Value;;

                            int aroonSignal = IdentifyAroonSignal(aroonResults);

                            bool isAroonUptrend = aroonSignal > 0;
                            bool isAroonDowntrend = aroonSignal < 0;

                            bool isHullCrossingUp = currentHull.EHMA > currentHull.EHMAPrev && prevHull.EHMA <= prevHull.EHMAPrev;
                            bool isHullCrossingDown = currentHull.EHMA > currentHull.EHMAPrev && prevHull.EHMA >= prevHull.EHMAPrev;

                            if (isHullCrossingUp 
                                    && isPriceBelowSMA
                                    && isSMAPointingUp
                                )
                            {
                                await OrderManager.PlaceLongOrderAsync(symbol, currentKline.Close, "Aroon + EHMA", currentKline.OpenTime);
                            }
                            else if (isHullCrossingDown
                                    && isPriceAboveSMA
                                    && isSMAPointingDown
                                    )
                            {
                                await OrderManager.PlaceShortOrderAsync(symbol, currentKline.Close, "Aroon + EHMA", currentKline.OpenTime);
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
            var hullResults = Helpers.StrategyUtils.CalculateEHMA(quotes, HullLength)
                .Select(hr => new HullSuiteResult { Date = hr.Date, EHMA = hr.EHMA, EHMAPrev = hr.EHMAPrev })
                .ToList();

            for (int i = SmaPeriod - 1; i < historicalData.Count(); i++)
            {
                var currentKline = historicalData.ElementAt(i);
                var prevKline = historicalData.ElementAt(i - 1);
                var currentSMA = smaResults[i];
                var previousSMA = smaResults[i - 1];
                var currentAroon = aroonResults[i];
                var currentHull = hullResults[i];
                var prevHull = hullResults[i - 1];

                if (currentSMA != null && currentSMA.Sma.HasValue && currentAroon != null
                    && previousSMA != null && previousSMA.Sma.HasValue 
                    && currentHull != null && prevHull != null)
                {
                    bool isPriceAboveSMA = (double)currentKline.Low > currentSMA.Sma.Value;
                    bool isPriceBelowSMA = (double)currentKline.High < currentSMA.Sma.Value;     
                    bool isSMAPointingUp = previousSMA.Sma.Value < currentSMA.Sma.Value;
                    bool isSMAPointingDown = previousSMA.Sma.Value > currentSMA.Sma.Value;                 

                    int aroonSignal = IdentifyAroonSignal(aroonResults);

                    bool isAroonUptrend = aroonSignal > 0;
                    bool isAroonDowntrend = aroonSignal < 0;

                    bool isHullCrossingUp = currentHull.EHMA > currentHull.EHMAPrev && prevHull.EHMA <= prevHull.EHMAPrev;
                    bool isHullCrossingDown = currentHull.EHMA < currentHull.EHMAPrev && prevHull.EHMA >= prevHull.EHMAPrev;
                    
                    if(!string.IsNullOrEmpty(currentKline.Symbol))
                    {
                        if (isHullCrossingUp 
                            && isSMAPointingUp 
                            && isPriceBelowSMA
                            )
                        {
                            await OrderManager.PlaceLongOrderAsync(currentKline.Symbol, currentKline.Close, "Aroon + EHMA", currentKline.OpenTime);
                        }
                        else if (isHullCrossingDown 
                                && isSMAPointingDown
                                && isPriceAboveSMA
                                )
                        {
                            await OrderManager.PlaceShortOrderAsync(currentKline.Symbol, currentKline.Close, "Aroon + EHMA", currentKline.OpenTime);
                        }
                    }
                }

                if (!string.IsNullOrEmpty(currentKline.Symbol) && currentKline.Close > 0)
                {
                    var currentPrices = new Dictionary<string, decimal> { { currentKline.Symbol, currentKline.Close } };
                    await OrderManager.CheckAndCloseTrades(currentPrices, currentKline.OpenTime);
                }
            }
        }

        // EHMA and parsing are provided by StrategyUtils; local versions removed.

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

        private void LogTradeSignal(string direction, string symbol, decimal price)
        {
            Console.WriteLine($"****** Aroon Strategy ******************");
            Console.WriteLine($"Go {direction} on {symbol} @ {price} with at {DateTime.Now:HH:mm:ss}");
            if(direction == "LONG")
            {
                Console.WriteLine($"There are two bullish FVGs.");    
                Console.WriteLine($"Price is in the first bullish FVG retest zone for {symbol}.");
            }
            else
            {
                Console.WriteLine($"There are two bearish FVGs.");                                
                Console.WriteLine($"Price is in the first bearish FVG retest zone for {symbol}.");
            }
            Console.WriteLine($"**************************************");
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
