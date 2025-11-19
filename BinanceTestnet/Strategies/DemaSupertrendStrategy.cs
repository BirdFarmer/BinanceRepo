using BinanceTestnet.Models;
using Newtonsoft.Json;
using RestSharp;
using Skender.Stock.Indicators;
using System.Globalization;
using BinanceTestnet.Trading;

namespace BinanceTestnet.Strategies
{
    public class DemaSupertrendStrategy : StrategyBase
    {
        // Enable/disable debug output for this strategy
        private const bool EnableDebug = true;

        protected override bool SupportsClosedCandles => true;
        
        // Supertrend parameters
        private const int SupertrendLength = 2;
        private const double SupertrendMultiple = 3.35;
        private const int DemaLength = 9;
        
        public DemaSupertrendStrategy(RestClient client, string apiKey, OrderManager orderManager, Wallet wallet)
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
                    {"limit", "100"}
                });

                var response = await Client.ExecuteGetAsync(request);
                if (response.IsSuccessful && response.Content != null)
                {
                    var klines = Helpers.StrategyUtils.ParseKlines(response.Content);

                    if (klines != null && klines.Count > SupertrendLength + 1)
                    {
                        // Build indicator quotes
                        var quotes = ToIndicatorQuotes(klines);
                        
                        // Calculate DEMA Supertrend
                        var demaSupertrendResults = CalculateDemaSupertrend(quotes, symbol);
                        
                        if (demaSupertrendResults.Count > 1)
                        {
                            var currentResult = demaSupertrendResults.Last();
                            var previousResult = demaSupertrendResults[demaSupertrendResults.Count - 2];
                            
                            // Use index-based selection to mirror historical behavior (last and previous klines)
                            var signalKline = klines.LastOrDefault();
                            var previousKline = klines.Count >= 2 ? klines[klines.Count - 2] : null;
                            if (signalKline == null || previousKline == null) return;
                            
                            // Check for trend changes
                            bool longSignal = currentResult.Direction > 0 && previousResult.Direction <= 0; // Trend turned bullish
                            bool shortSignal = currentResult.Direction < 0 && previousResult.Direction >= 0; // Trend turned bearish
                            
                            if (longSignal)
                            {
                                Console.WriteLine($"DEMA Supertrend turned bullish, attempting to go LONG for {symbol}");
                                if (EnableDebug)
                                {
                                    Console.WriteLine($"[DEBUG][{symbol}] Signal candle (async index): Time={signalKline.CloseTime}, Open={signalKline.Open}, Close={signalKline.Close}, High={signalKline.High}, Low={signalKline.Low}");
                                    Console.WriteLine($"[DEBUG][{symbol}] Previous candle: Time={previousKline.CloseTime}, Open={previousKline.Open}, Close={previousKline.Close}, High={previousKline.High}, Low={previousKline.Low}");
                                    Console.WriteLine($"[DEBUG][{symbol}] Current result: Date={currentResult.Date}, Dema={currentResult.Dema}, Supertrend={currentResult.Supertrend}, Direction={currentResult.Direction}");
                                    Console.WriteLine($"[DEBUG][{symbol}] Previous result: Date={previousResult.Date}, Dema={previousResult.Dema}, Supertrend={previousResult.Supertrend}, Direction={previousResult.Direction}");
                                }
                                await OrderManager.PlaceLongOrderAsync(symbol, signalKline.Close, "DEMA Supertrend", signalKline.CloseTime);
                            }
                            else if (shortSignal)
                            {
                                Console.WriteLine($"DEMA Supertrend turned bearish, attempting to go SHORT for {symbol}");
                                if (EnableDebug)
                                {
                                    Console.WriteLine($"[DEBUG][{symbol}] Signal candle (async index): Time={signalKline.CloseTime}, Open={signalKline.Open}, Close={signalKline.Close}, High={signalKline.High}, Low={signalKline.Low}");
                                    Console.WriteLine($"[DEBUG][{symbol}] Previous candle: Time={previousKline.CloseTime}, Open={previousKline.Open}, Close={previousKline.Close}, High={previousKline.High}, Low={previousKline.Low}");
                                    Console.WriteLine($"[DEBUG][{symbol}] Current result: Date={currentResult.Date}, Dema={currentResult.Dema}, Supertrend={currentResult.Supertrend}, Direction={currentResult.Direction}");
                                    Console.WriteLine($"[DEBUG][{symbol}] Previous result: Date={previousResult.Date}, Dema={previousResult.Dema}, Supertrend={previousResult.Supertrend}, Direction={previousResult.Direction}");
                                }
                                await OrderManager.PlaceShortOrderAsync(symbol, signalKline.Close, "DEMA Supertrend", signalKline.CloseTime);
                            }
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
            if (historicalCandles == null || historicalCandles.Count() <= SupertrendLength + 1)
                return;

            // Convert to quotes
            var quotes = historicalCandles.Select(k => new BinanceTestnet.Models.Quote
            {
                Date = DateTimeOffset.FromUnixTimeMilliseconds(k.OpenTime).UtcDateTime,
                Open = k.Open,
                High = k.High,
                Low = k.Low,
                Close = k.Close,
                Volume = k.Volume
            }).ToList();

            // Calculate DEMA Supertrend
            // Try to obtain the symbol from the historical candles (if present) to label debug output
            var histSymbol = historicalCandles.FirstOrDefault()?.Symbol;
            var demaSupertrendResults = CalculateDemaSupertrend(quotes, histSymbol);
            
            // Iterate through historical data to generate signals
            for (int i = SupertrendLength; i < historicalCandles.Count(); i++)
            {
                var currentKline = historicalCandles.ElementAt(i);
                var currentResult = demaSupertrendResults[i];
                var previousResult = demaSupertrendResults[i - 1];
                
                // Check for trend changes
                bool longSignal = currentResult.Direction > 0 && previousResult.Direction <= 0;
                bool shortSignal = currentResult.Direction < 0 && previousResult.Direction >= 0;
                
                if (longSignal && !string.IsNullOrEmpty(currentKline.Symbol))
                {
                    if (EnableDebug)
                    {
                        Console.WriteLine($"[DEBUG][HIST] LONG signal for {currentKline.Symbol} at Time={currentKline.CloseTime}, Close={currentKline.Close}");
                        Console.WriteLine($"[DEBUG][HIST] currentResult: Date={currentResult.Date}, Dema={currentResult.Dema}, Supertrend={currentResult.Supertrend}, Direction={currentResult.Direction}");
                        Console.WriteLine($"[DEBUG][HIST] previousResult: Date={previousResult.Date}, Dema={previousResult.Dema}, Supertrend={previousResult.Supertrend}, Direction={previousResult.Direction}");
                    }
                    await OrderManager.PlaceLongOrderAsync(currentKline.Symbol, currentKline.Close, 
                        "DEMA Supertrend Bullish", currentKline.CloseTime);
                }
                else if (shortSignal && !string.IsNullOrEmpty(currentKline.Symbol))
                {
                    if (EnableDebug)
                    {
                        Console.WriteLine($"[DEBUG][HIST] SHORT signal for {currentKline.Symbol} at Time={currentKline.CloseTime}, Close={currentKline.Close}");
                        Console.WriteLine($"[DEBUG][HIST] currentResult: Date={currentResult.Date}, Dema={currentResult.Dema}, Supertrend={currentResult.Supertrend}, Direction={currentResult.Direction}");
                        Console.WriteLine($"[DEBUG][HIST] previousResult: Date={previousResult.Date}, Dema={previousResult.Dema}, Supertrend={previousResult.Supertrend}, Direction={previousResult.Direction}");
                    }
                    await OrderManager.PlaceShortOrderAsync(currentKline.Symbol, currentKline.Close, 
                        "DEMA Supertrend Bearish", currentKline.CloseTime);
                }

                // Check for trade closures based on current price
                if (!string.IsNullOrEmpty(currentKline.Symbol) && currentKline.Close > 0)
                {
                    var currentPrices = new Dictionary<string, decimal> { { currentKline.Symbol, currentKline.Close } };
                    await OrderManager.CheckAndCloseTrades(currentPrices, currentKline.CloseTime);
                }
            }
        }

        private List<DemaSupertrendResult> CalculateDemaSupertrend(List<BinanceTestnet.Models.Quote> quotes, string? symbol = null)
        {
            var results = new List<DemaSupertrendResult>();
            
            // Calculate DEMA
            var demaResults = Indicator.GetDema(quotes, DemaLength).ToList();
            
            // Calculate ATR for Supertrend
            var atrResults = Indicator.GetAtr(quotes, SupertrendLength).ToList();
            
            decimal? previousUpperBand = null;
            decimal? previousLowerBand = null;
            decimal? previousSupertrend = null;
            int previousDirection = 0;

            for (int i = 0; i < quotes.Count; i++)
            {
                var quote = quotes[i];
                // Align indicator outputs to the same index as the input quotes.
                // Skender.Stock.Indicators returns result lists aligned with the quotes
                // (initial entries may have null values). Using offsets here caused
                // the indicator series to be shifted backwards and produced late signals.
                var dema = i < demaResults.Count ? (decimal?)demaResults[i].Dema : (decimal?)null;
                var atr = i < atrResults.Count ? (decimal?)atrResults[i].Atr : (decimal?)null;
                
                if (dema == null || atr == null)
                {
                    results.Add(new DemaSupertrendResult 
                    { 
                        Date = quote.Date,
                        Dema = dema,
                        Supertrend = null,
                        Direction = 0
                    });
                    continue;
                }

                // Calculate Supertrend bands
                decimal upperBand = dema.Value + (decimal)SupertrendMultiple * atr.Value;
                decimal lowerBand = dema.Value - (decimal)SupertrendMultiple * atr.Value;
                
                // Adjust bands based on previous values
                decimal adjustedLowerBand = lowerBand;
                decimal adjustedUpperBand = upperBand;
                
                if (previousLowerBand != null && previousUpperBand != null)
                {
                    adjustedLowerBand = (lowerBand > previousLowerBand || quotes[i-1].Close < previousLowerBand) ? 
                        lowerBand : previousLowerBand.Value;
                    
                    adjustedUpperBand = (upperBand < previousUpperBand || quotes[i-1].Close > previousUpperBand) ? 
                        upperBand : previousUpperBand.Value;
                }
                
                // Determine direction and Supertrend value
                int direction = previousDirection;
                decimal supertrendValue;
                
                if (previousSupertrend == null)
                {
                    direction = 1; // Start bullish
                    supertrendValue = adjustedLowerBand;
                }
                else if (previousSupertrend == previousUpperBand)
                {
                    // previous supertrend was the upper band (bearish). If price moves above the adjusted upper band, switch bullish.
                    direction = quote.Close > adjustedUpperBand ? 1 : -1;
                }
                else
                {
                    // previous supertrend was the lower band (bullish). If price moves below the adjusted lower band, switch bearish.
                    direction = quote.Close < adjustedLowerBand ? -1 : 1;
                }

                // Map direction: 1 => bullish uses lower band; -1 => bearish uses upper band
                supertrendValue = direction == 1 ? adjustedLowerBand : adjustedUpperBand;

                // if (EnableDebug)
                // {
                //     var tag = symbol != null ? $"[DEBUG][{symbol}]" : "[DEBUG]";
                //     Console.WriteLine($"{tag} Index={i}, Date={quote.Date:u}, Close={quote.Close}, Dema={dema}, ATR={atr}");
                //     Console.WriteLine($"{tag} upperBand={upperBand}, lowerBand={lowerBand}, adjustedUpperBand={adjustedUpperBand}, adjustedLowerBand={adjustedLowerBand}");
                //     Console.WriteLine($"{tag} prevUpper={previousUpperBand}, prevLower={previousLowerBand}, prevSupertrend={previousSupertrend}, prevDirection={previousDirection}");
                //     Console.WriteLine($"{tag} computed Direction={direction}, SupertrendValue={supertrendValue}");
                // }

                results.Add(new DemaSupertrendResult 
                { 
                    Date = quote.Date,
                    Dema = dema,
                    Supertrend = supertrendValue,
                    Direction = direction
                });
                
                // Update previous values
                previousUpperBand = adjustedUpperBand;
                previousLowerBand = adjustedLowerBand;
                previousSupertrend = supertrendValue;
                previousDirection = direction;
            }
            
            return results;
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

    public class DemaSupertrendResult
    {
        public DateTime Date { get; set; }
        public decimal? Dema { get; set; }
        public decimal? Supertrend { get; set; }
        public int Direction { get; set; } // 1 for bullish, -1 for bearish, 0 for neutral/unknown
    }
}