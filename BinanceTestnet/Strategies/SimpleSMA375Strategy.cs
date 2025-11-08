using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using BinanceTestnet.Models;
using Newtonsoft.Json;
using RestSharp;
using Skender.Stock.Indicators;
using BinanceTestnet.Trading;

namespace BinanceTestnet.Strategies
{
public class SimpleSMA375Strategy : StrategyBase
{
    private const int SmaPeriod = 375;  // Period for the SMA
    private ConcurrentDictionary<string, decimal> lastSMA375 = new ConcurrentDictionary<string, decimal>();

    public SimpleSMA375Strategy(RestClient client, string apiKey, OrderManager orderManager, Wallet wallet)
        : base(client, apiKey, orderManager, wallet)
    {
    }
    public override async Task RunAsync(string symbol, string interval)
    {
        if (string.IsNullOrEmpty(symbol))
        {
            Console.WriteLine("Error: Symbol is null or empty.");
            return;
        }

        // Fetch more data points to ensure enough for SMA calculation
        const int dataPointsRequired = 800;
        var request = Helpers.StrategyUtils.CreateGet("/fapi/v1/klines", new Dictionary<string,string>
        {
            {"symbol", symbol},
            {"interval", interval},
            {"limit", dataPointsRequired.ToString()}
        });
        var response = await Client.ExecuteGetAsync(request);
        var klines = response.IsSuccessful && response.Content != null
            ? Helpers.StrategyUtils.ParseKlines(response.Content)
            : null;
        if (klines == null || klines.Count < SmaPeriod)
        {
            Console.WriteLine($"Error: Not enough kline data fetched for {symbol}. Required: {SmaPeriod}, Available: {klines?.Count}");
            return;
        }

        var closes = klines.Select(k => k.Close).ToArray();
        var history = ConvertToQuoteList(klines, closes);

        // Calculate SMA 375
        var sma375 = Indicator.GetSma(history, SmaPeriod)
            .Where(q => q.Sma.HasValue)
            .Select(q => (decimal)q.Sma!.Value)
            .ToList();

        if (sma375.Count < SmaPeriod)
        {
            Console.WriteLine($"Error: Not enough SMA values calculated. Required: {SmaPeriod}, Available: {sma375.Count}");
            return;
        }

        // Get the latest kline (the most recent one)
        var latestKline = klines.Last();
        decimal currentPriceClose = latestKline.Close;
        decimal currentPriceLow = latestKline.Low;
        decimal currentPriceHigh = latestKline.High;
        decimal previousPriceClose = klines[klines.Count - 2].Close;
        decimal previousPriceLow = klines[klines.Count - 2].Low;
        decimal previousPriceHigh = klines[klines.Count - 2].High;
        decimal currentSMA375 = (decimal)sma375.Last(); // Get the latest SMA value
        decimal previousSMA375 = (decimal)sma375[sma375.Count - 2]; // Get the previous SMA value [sma375.Count - 2]
        bool isUpwards = currentSMA375 > previousSMA375;
        bool IsDowntrend = currentSMA375 < previousSMA375;

        // Check for a crossover
        bool crossedAbove = previousPriceLow < previousSMA375 && currentPriceLow > currentSMA375;
        bool crossedBelow = previousPriceHigh > previousSMA375 && currentPriceHigh < currentSMA375;

        if (crossedAbove && isUpwards)
        {
            Console.WriteLine($"Long Signal for {symbol} at {currentPriceClose}");
            await OrderManager.PlaceLongOrderAsync(symbol, currentPriceClose, "SMA375 CrossUp", latestKline.CloseTime);
        }
        else if (crossedBelow && IsDowntrend)
        {
            Console.WriteLine($"Short Signal for {symbol} at {currentPriceClose}");
            await OrderManager.PlaceShortOrderAsync(symbol, currentPriceClose, "SMA375 CrossDown", latestKline.CloseTime);
        }

        // Check and close existing trades
        var currentPrices = new Dictionary<string, decimal> { { symbol, currentPriceClose } };
        await OrderManager.CheckAndCloseTrades(currentPrices);
    }

    public override async Task RunOnHistoricalDataAsync(IEnumerable<Kline> historicalData)
    {
    var klines = historicalData.ToList();
        if (klines.Count < SmaPeriod)
        {
            Console.WriteLine($"Error: Not enough historical kline data. Required: {SmaPeriod}, Available: {klines.Count}");
            return;
        }

        var closes = klines.Select(k => k.Close).ToArray();
        var history = ConvertToQuoteList(klines, closes);

        // Calculate SMA 375
        var sma375 = Indicator.GetSma(history, SmaPeriod)
            .Where(q => q.Sma.HasValue)
            .Select(q => (decimal)q.Sma!.Value)
            .ToList();

        // Ensure that we have enough SMA values to work with
        if (sma375.Count < 2)
        {
            Console.WriteLine($"Error: Not enough SMA values calculated. Required: at least 2, Available: {sma375.Count}");
            return;
        }

        int smaStartIndex = SmaPeriod - 1; // The index in the original list where SMA starts
        int loopStartIndex = smaStartIndex + 1; // We can start checking crossovers after at least one full SMA is calculated

        for (int i = loopStartIndex; i < klines.Count; i++)
        {
            decimal currentPriceClose = klines[i].Close;
            decimal previousPriceClose = klines[i - 1].Close;

            decimal currentPriceLow = klines[i].Low;
            decimal currentPriceHigh = klines[i].High;
            decimal previousPriceLow = klines[klines.Count - 2].Low;
            decimal previousPriceHigh = klines[klines.Count - 2].High;

            int smaIndex = i - smaStartIndex;
            decimal currentSMA375 = (decimal)sma375[smaIndex];
            decimal previousSMA375 = (decimal)sma375[smaIndex - 1];
            bool isUpwards = currentSMA375 > previousSMA375;
            bool isDownwards = currentSMA375 < previousSMA375;

            // Check for a crossover
            bool crossedAbove = previousPriceLow < previousSMA375 && currentPriceLow > currentSMA375;
            bool crossedBelow = previousPriceHigh > previousSMA375 && currentPriceHigh < currentSMA375;

            if (!string.IsNullOrEmpty(klines[i].Symbol) && crossedAbove && isUpwards)
            {
                Console.WriteLine($"Long Signal (Historical) for {klines[i].Symbol} at {currentPriceClose}");
                await OrderManager.PlaceLongOrderAsync(klines[i].Symbol, currentPriceClose, "SMA375", historicalData.Last().CloseTime);
            }
            else if (!string.IsNullOrEmpty(klines[i].Symbol) && crossedBelow && isDownwards)
            {

                Console.WriteLine($"Short Signal (Historical) for {klines[i].Symbol} at {currentPriceClose}");
                
                await OrderManager.PlaceShortOrderAsync(klines[i].Symbol, currentPriceClose, "SMA375", historicalData.Last().CloseTime);
            }
            // Check and close existing trades
            if (!string.IsNullOrEmpty(klines[i].Symbol))
            {
                string sym = klines[i].Symbol!;
                var currentPrices = new Dictionary<string, decimal> { { sym, currentPriceClose } };
                await OrderManager.CheckAndCloseTrades(currentPrices, historicalData.Last().CloseTime);
            }
        }
    }




    // Request creation and parsing centralized in StrategyUtils

    private List<BinanceTestnet.Models.Quote> ConvertToQuoteList(IEnumerable<Kline> klines, decimal[] closes)
    {
        var quotes = new List<BinanceTestnet.Models.Quote>();
        foreach (var kline in klines)
        {
            quotes.Add(new BinanceTestnet.Models.Quote
            {
                Open = kline.Open,
                High = kline.High,
                Low = kline.Low,
                Close = kline.Close
            });
        }
        return quotes;
    }

    // Request creation centralized in StrategyUtils

    private void HandleErrorResponse(string symbol, RestResponse response)
    {
        Console.WriteLine($"Error for {symbol}: {response.ErrorMessage}");
        Console.WriteLine($"Status Code: {response.StatusCode}");
        Console.WriteLine($"Content: {response.Content}");
    }
}
}
