using BinanceTestnet.Models;
using BinanceTestnet.Trading;
using Newtonsoft.Json;
using RestSharp;
using Skender.Stock.Indicators;
using System.Globalization;

namespace BinanceTestnet.Strategies
{
public class RSIMomentumStrategy : StrategyBase
{
    private readonly int _rsiPeriod = 14;
    private readonly int _lookbackPeriod = 150;
    private readonly object _lock = new();

    private static Dictionary<string, string> rsiStateMap = new();

    private readonly Dictionary<string, decimal> previousRsiMap = new();

    public RSIMomentumStrategy(RestClient client, string apiKey, OrderManager orderManager, Wallet wallet)
        : base(client, apiKey, orderManager, wallet)
    {
    }

    public override async Task RunAsync(string symbol, string interval)
    {
        try
        {
            var klines = await FetchKlinesAsync(symbol, interval);
            if (klines == null || klines.Count <= _rsiPeriod)
            {
                Console.WriteLine($"[WARNING] Insufficient data for {symbol}.");
                return;
            }

            var rsiValues = await FetchRSIFromKlinesAsync(klines);
            if (rsiValues.Count == 0) return;

            // Console.WriteLine($"[DEBUG] RSI State Map Count: {rsiStateMap.Count}");
            //Console.WriteLine($"[DEBUG] RSI State for {symbol}: {(rsiStateMap.ContainsKey(symbol) ? rsiStateMap[symbol] : "NOT SET")}");


            // **Always evaluate first before setting initial state**
            if (rsiStateMap.ContainsKey(symbol))
            {
                await EvaluateRSIConditions(rsiValues, symbol, klines);
            }

            // **Now initialize the RSI state if it does not exist**
            if (!rsiStateMap.ContainsKey(symbol))
            {
                InitializeRSIState(symbol, rsiValues, klines);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Processing {symbol}: {ex.Message}");
        }
    }

         public override async Task RunOnHistoricalDataAsync(IEnumerable<Kline> historicalData)
        {
            var quotes = historicalData.Select(k => new BinanceTestnet.Models.Quote
            {
                Date = DateTimeOffset.FromUnixTimeMilliseconds(k.OpenTime).UtcDateTime,
                Close = k.Close
            }).ToList();

            var rsiResults = Indicator.GetRsi(quotes, 14).ToList();
            Dictionary<string, string> rsiStateMap = new();

            for (int i = 1; i < rsiResults.Count; i++)
            {
                var lastRsi = rsiResults[i];
                var prevRsi = rsiResults[i - 1];
                var kline = historicalData.ElementAt(i);
                string symbol = kline.Symbol;

                if (!rsiStateMap.ContainsKey(symbol))
                    rsiStateMap[symbol] = "neutral";

                if (prevRsi.Rsi < 29 && lastRsi.Rsi >= 29)
                    rsiStateMap[symbol] = "coming_from_oversold";
                else if (prevRsi.Rsi > 71 && lastRsi.Rsi <= 71)
                    rsiStateMap[symbol] = "coming_from_overbought";

                // Long Entry Condition
                if (rsiStateMap[symbol] == "coming_from_oversold" && lastRsi.Rsi >= 71)
                {
                    await OrderManager.PlaceLongOrderAsync(symbol, kline.Close, "RSI Momentum", kline.CloseTime);
                    LogTradeSignal("LONG", symbol, kline.Close);
                    rsiStateMap[symbol] = "neutral";
                }
                
                // Short Entry Condition
                else if (rsiStateMap[symbol] == "coming_from_overbought" && lastRsi.Rsi <= 29)
                {
                    await OrderManager.PlaceShortOrderAsync(symbol, kline.Close, "RSI Momentum", kline.CloseTime);
                    LogTradeSignal("SHORT", symbol, kline.Close);
                    rsiStateMap[symbol] = "neutral";
                }

                // Check for open trade closing conditions
                var currentPrices = new Dictionary<string, decimal> { { symbol, kline.Close } };
                await OrderManager.CheckAndCloseTrades(currentPrices, kline.CloseTime);
            }
        }

    private void InitializeRSIState(string symbol, List<decimal> rsiValues, List<Kline> klines)
    {
        for (int i = rsiValues.Count - 1; i >= 0; i--)
        {
            decimal rsi = rsiValues[i];
            var kline = klines[i];  // Get the Kline corresponding to the RSI value

            if (rsi >= 71)
            {
                rsiStateMap[symbol] = "OVERBOUGHT";
                //Console.WriteLine($"[STATE] {symbol} is set to OVERBOUGHT at RSI {rsi} on {DateTimeOffset.FromUnixTimeMilliseconds(kline.CloseTime).UtcDateTime}.");
                return;
            }
            if (rsi <= 29)
            {
                rsiStateMap[symbol] = "OVERSOLD";
                //Console.WriteLine($"[STATE] {symbol} is set to OVERSOLD at RSI {rsi} on {DateTimeOffset.FromUnixTimeMilliseconds(kline.CloseTime).UtcDateTime}.");
                return;
            }
        }
        rsiStateMap[symbol] = "NEUTRAL";
        //Console.WriteLine($"[STATE] {symbol} is set to NEUTRAL.");
    }

    private async Task EvaluateRSIConditions(List<decimal> rsiValues, string symbol, List<Kline> klines)
    {
        lock (_lock)
        {
            if (rsiValues.Count < 2) return; // Ensure we have at least two RSI values

            decimal previousRsi = rsiValues[^2];  // RSI of previous candle
            decimal currentRsi = rsiValues[^1];   // RSI of current candle
            string currentState = rsiStateMap.ContainsKey(symbol) ? rsiStateMap[symbol] : "NEUTRAL";

            var prevKline = klines[^2];  // Previous Kline

            var currKline = klines[^1];  // Current Kline (real-time)

            // // Ensure the trade is placed only in real-time, not historical
            // if (currKline.CloseTime < DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) return;
            if(currentRsi >= 71 || currentRsi <= 29)
                Console.WriteLine($"******** {symbol} at {currKline.Close} with RSI {currentRsi} on {DateTimeOffset.FromUnixTimeMilliseconds(currKline.CloseTime).UtcDateTime}");

            if (currentState == "OVERSOLD" && previousRsi < 71 && currentRsi >= 71)
            {
                Console.WriteLine($"[TRADE] Long Entry for {symbol} at {currKline.Close} with RSI {currentRsi} on {DateTimeOffset.FromUnixTimeMilliseconds(currKline.CloseTime).UtcDateTime}");

                Task.Run(async () => await PlaceTradeAsync(symbol, "LONG", currKline.Close, currKline.CloseTime));

                Console.WriteLine($"[STATE CHANGE] {symbol} changed from OVERSOLD to OVERBOUGHT at RSI {currentRsi}.");
                rsiStateMap[symbol] = "OVERBOUGHT";
            }
            else if (currentState == "OVERBOUGHT" && previousRsi > 29 && currentRsi <= 29)
            {
                Console.WriteLine($"[TRADE] Short Entry for {symbol} at {currKline.Close} with RSI {currentRsi} on {DateTimeOffset.FromUnixTimeMilliseconds(currKline.CloseTime).UtcDateTime}");

                Task.Run(async () => await PlaceTradeAsync(symbol, "SHORT", currKline.Close, currKline.CloseTime));

                Console.WriteLine($"[STATE CHANGE] {symbol} changed from OVERBOUGHT to OVERSOLD at RSI {currentRsi}.");
                rsiStateMap[symbol] = "OVERSOLD";
            }
        }
    }

    private async Task PlaceTradeAsync(string symbol, string side, decimal price, long timestamp)
    {
        try
        {
            if (side == "LONG")
            {
                await OrderManager.PlaceLongOrderAsync(symbol, price, "RSI Momentum", timestamp);
                LogTradeSignal("LONG", symbol, price);
            }
            else if (side == "SHORT")
            {
                await OrderManager.PlaceShortOrderAsync(symbol, price, "RSI Momentum", timestamp);
                LogTradeSignal("LONG", symbol, price);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Order placement for {symbol}: {ex.Message}");
        }
    }

    private async Task<List<decimal>> FetchRSIFromKlinesAsync(List<Kline> klines)
    {
        var quotes = klines.Select(k => new BinanceTestnet.Models.Quote
        {
            Date = DateTimeOffset.FromUnixTimeMilliseconds(k.CloseTime).UtcDateTime,
            Open = k.Open,
            High = k.High,
            Low = k.Low,
            Close = k.Close,
            Volume = k.Volume
        }).ToList();

        var rsiResults = Indicator.GetRsi(quotes, _rsiPeriod).ToList();
        return rsiResults.Where(r => r.Rsi.HasValue).Select(r => (decimal)r.Rsi.Value).ToList();
    }

    private async Task<List<Kline>> FetchKlinesAsync(string symbol, string interval)
    {
        var request = CreateRequest("/fapi/v1/klines");
        request.AddParameter("symbol", symbol, ParameterType.QueryString);
        request.AddParameter("interval", interval, ParameterType.QueryString);
        request.AddParameter("limit", (_lookbackPeriod + 1).ToString(), ParameterType.QueryString);

        var response = await Client.ExecuteGetAsync(request);
        return response.IsSuccessful ? ParseKlines(response.Content) : null;
    }

    private RestRequest CreateRequest(string resource)
    {
        var request = new RestRequest(resource, Method.Get);
        request.AddHeader("Content-Type", "application/json");
        return request;
    }

    private List<Kline> ParseKlines(string content)
    {
        try
        {
            return JsonConvert.DeserializeObject<List<List<object>>>(content)
                ?.Select(k => new Kline
                {
                    Open = Convert.ToDecimal(k[1], CultureInfo.InvariantCulture),
                    High = Convert.ToDecimal(k[2], CultureInfo.InvariantCulture),
                    Low = Convert.ToDecimal(k[3], CultureInfo.InvariantCulture),
                    Close = Convert.ToDecimal(k[4], CultureInfo.InvariantCulture),
                    OpenTime = Convert.ToInt64(k[0]),
                    CloseTime = Convert.ToInt64(k[6])
                })
                .ToList();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Parsing klines: {ex.Message}");
            return null;
        }
    }

    private void LogTradeSignal(string direction, string symbol, decimal price)
    {
        Console.WriteLine($"[RSI Momentum] {direction} signal for {symbol} at {price} ({DateTime.UtcNow})");
    }
}
}
