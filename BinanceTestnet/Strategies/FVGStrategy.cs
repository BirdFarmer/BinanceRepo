using BinanceTestnet.Models;
using BinanceTestnet.Trading;
using Newtonsoft.Json;
using RestSharp;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace BinanceLive.Strategies
{
    public class FVGStrategy : StrategyBase
    {
        private readonly int _fvgLookbackPeriod = 100; // Number of periods to look back for FVGs
        private readonly decimal _fvgThreshold = 0; // Minimum percentage gap to consider

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
                request.AddParameter("limit", (_fvgLookbackPeriod + 1).ToString(), ParameterType.QueryString);

                var response = await Client.ExecuteGetAsync(request);
                if (response.IsSuccessful)
                {
                    var klines = ParseKlines(response.Content);

                    if (klines != null && klines.Count > _fvgLookbackPeriod)
                    {
                        var currentKline = klines.Last();
                        var previousKlines = klines.TakeLast(_fvgLookbackPeriod).ToList();

                        var fvgZones = IdentifyFVGs(previousKlines);

                        if (fvgZones.Count >= 2)
                        {
                            var lastFVG = fvgZones.Last();
                            var secondLastFVG = fvgZones[fvgZones.Count - 2];

                            // Long entry logic
                            if (lastFVG.Type == FVGType.Bullish && secondLastFVG.Type == FVGType.Bullish &&
                                currentKline.Low < lastFVG.UpperBound && currentKline.Low > lastFVG.LowerBound)
                            {
                                await OrderManager.PlaceLongOrderAsync(symbol, currentKline.Close, "FVG", currentKline.CloseTime);
                            }
                            // Short entry logic
                            else if (lastFVG.Type == FVGType.Bearish && secondLastFVG.Type == FVGType.Bearish &&
                                     currentKline.High > lastFVG.LowerBound && currentKline.High < lastFVG.UpperBound)
                            {
                                await OrderManager.PlaceShortOrderAsync(symbol, currentKline.Close, "FVG", currentKline.CloseTime);
                            }
                        }
                    }
                    else
                    {
                        Console.WriteLine($"Not enough klines data available for {symbol}.");
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

        public override async Task RunOnHistoricalDataAsync(IEnumerable<BinanceTestnet.Models.Kline> historicalData)
        {
            var quotes = historicalData.Select(k => new BinanceTestnet.Models.Quote
            {
                Date = DateTimeOffset.FromUnixTimeMilliseconds(k.OpenTime).UtcDateTime,
                High = k.High,
                Low = k.Low,
                Open = k.Open,
                Close = k.Close
            }).ToList();

            // Ensure we have enough data to start processing
            if (quotes.Count > _fvgLookbackPeriod)
            {
                for (int i = _fvgLookbackPeriod; i < quotes.Count; i++)
                {
                    var currentQuote = quotes[i];
                    var previousQuotes = quotes.Skip(i - _fvgLookbackPeriod).Take(_fvgLookbackPeriod).ToList();

                    var historicalKlines = ConvertQuotesToKlines(previousQuotes);
                    var fvgZones = IdentifyFVGs(historicalKlines);

                    if (fvgZones.Count >= 2)
                    {
                        var lastFVG = fvgZones.Last();
                        var secondLastFVG = fvgZones[fvgZones.Count - 2];

                        // Long entry logic
                        if (lastFVG.Type == FVGType.Bullish && secondLastFVG.Type == FVGType.Bullish &&
                            currentQuote.Low < lastFVG.UpperBound && currentQuote.Low > lastFVG.LowerBound)
                        {
                            await OrderManager.PlaceLongOrderAsync(historicalData.ElementAt(i).Symbol, currentQuote.Close, "FVG", historicalKlines.Last().CloseTime );
                        }
                        // Short entry logic
                        else if (lastFVG.Type == FVGType.Bearish && secondLastFVG.Type == FVGType.Bearish &&
                                currentQuote.High > lastFVG.LowerBound && currentQuote.High < lastFVG.UpperBound)
                        {
                            await OrderManager.PlaceShortOrderAsync(historicalData.ElementAt(i).Symbol, currentQuote.Close, "FVG", historicalKlines.Last().CloseTime );
                        }
                    }

                    if (currentQuote.Close > 0)
                    {
                        var currentPrices = new Dictionary<string, decimal> { { historicalData.ElementAt(i).Symbol, currentQuote.Close } };
                        await OrderManager.CheckAndCloseTrades(currentPrices);
                    }
                }
            }
        }

        private List<FVGZone> IdentifyFVGs(List<BinanceTestnet.Models.Kline> klines)
        {
            var fvgZones = new List<FVGZone>();

            for (int i = 1; i < klines.Count - 1; i++)
            {
                var previousKline = klines[i - 1];
                var currentKline = klines[i];
                var nextKline = klines[i + 1];

                // Ensure all three candles are green for a bullish FVG
                if (IsGreenCandle(previousKline) && IsGreenCandle(currentKline) && IsGreenCandle(nextKline))
                {
                    var gap = currentKline.High - previousKline.Low;
                    if (gap > 0)
                    {
                        fvgZones.Add(new FVGZone
                        {
                            LowerBound = previousKline.Low,
                            UpperBound = currentKline.High,
                            Type = FVGType.Bullish
                        });
                    }
                }
                // Ensure all three candles are red for a bearish FVG
                else if (IsRedCandle(previousKline) && IsRedCandle(currentKline) && IsRedCandle(nextKline))
                {
                    var gap = previousKline.High - currentKline.Low;
                    if (gap > 0)
                    {
                        fvgZones.Add(new FVGZone
                        {
                            LowerBound = currentKline.Low,
                            UpperBound = previousKline.High,
                            Type = FVGType.Bearish
                        });
                    }
                }
            }

            return fvgZones;
        }

        private bool IsGreenCandle(BinanceTestnet.Models.Kline kline)
        {

            return kline.Close > kline.Open;
        }

        private bool IsRedCandle(BinanceTestnet.Models.Kline kline)
        {
            return kline.Close < kline.Open;
        }


        private List<BinanceTestnet.Models.Kline>? ParseKlines(string content)
        {
            try
            {
                return JsonConvert.DeserializeObject<List<List<object>>>(content)
                    ?.Select(k =>
                    {
                        var kline = new BinanceTestnet.Models.Kline();
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

        private void LogTradeSignal(string direction, string symbol, decimal price, decimal tpPrice, decimal slPrice)
        {
            Console.WriteLine($"****** FVG Strategy ******************");
            Console.WriteLine($"Go {direction} on {symbol} @ {price} with TP @ {tpPrice} and SL @ {slPrice} at {DateTime.Now:HH:mm:ss}");
            Console.WriteLine($"**************************************");
        }

        private void HandleErrorResponse(string symbol, RestResponse response)
        {
            Console.WriteLine($"Error for {symbol}: {response.ErrorMessage}");
            Console.WriteLine($"Status Code: {response.StatusCode}");
            Console.WriteLine($"Content: {response.Content}");
        }

        // Convert Quotes to Klines for FVG identification
        private List<BinanceTestnet.Models.Kline> ConvertQuotesToKlines(IEnumerable<BinanceTestnet.Models.Quote> quotes)
        {
            return quotes.Select(q => new BinanceTestnet.Models.Kline
            {
                Open = q.Open, // Assigning close as open for simplicity, modify as needed
                High = q.High, // Assigning close as high for simplicity, modify as needed
                Low = q.Low, // Assigning close as low for simplicity, modify as needed
                Close = q.Close,
                OpenTime = new DateTimeOffset(q.Date).ToUnixTimeMilliseconds(),
                CloseTime = new DateTimeOffset(q.Date).AddMinutes(1).ToUnixTimeMilliseconds(), // Adjust as needed
                NumberOfTrades = 0 // Set default or computed value
            }).ToList();
        }
    }

    public class FVGZone
    {
        public decimal LowerBound { get; set; }
        public decimal UpperBound { get; set; }
        public FVGType Type { get; set; } // New property to determine if the FVG is bullish or bearish
    }

    public enum FVGType
    {
        Bullish,
        Bearish
    }
}

