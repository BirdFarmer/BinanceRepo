// MACDDivergenceStrategy.cs
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
    public partial class MACDDivergenceStrategy : StrategyBase, ISnapshotAwareStrategy
    {
        protected override bool SupportsClosedCandles => true;
        public MACDDivergenceStrategy(RestClient client, string apiKey, OrderManager orderManager, Wallet wallet) 
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
                    {"limit", "401"}
                });

                var response = await Client.ExecuteGetAsync(request);
                if (response.IsSuccessful && response.Content != null)
                {
                    var klines = Helpers.StrategyUtils.ParseKlines(response.Content);

                    if (klines != null && klines.Count > 0)
                    {
                        var workingKlines = UseClosedCandles ? Helpers.StrategyUtils.ExcludeForming(klines) : klines;
                        await ProcessKlinesAsync(symbol, workingKlines);
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

        public override async Task RunOnHistoricalDataAsync(IEnumerable<Kline> historicalData)
        {
            var quotes = historicalData.Select(k => new BinanceTestnet.Models.Quote
            {
                Date = DateTimeOffset.FromUnixTimeMilliseconds(k.OpenTime).UtcDateTime,
                Close = k.Close
            }).ToList();

            var macdResults = Indicator.GetMacd(quotes, 12, 26, 9).ToList();

            foreach (var kline in historicalData)
            {
                if(kline.Symbol == null)    continue;
                
                var currentQuotes = quotes.TakeWhile(q => q.Date <= DateTimeOffset.FromUnixTimeMilliseconds(kline.OpenTime).UtcDateTime).ToList();
                var divergence = IdentifyDivergence(macdResults);

                if (divergence != 0)
                {
                    if (divergence == 1)
                    {
                        await OrderManager.PlaceLongOrderAsync(kline.Symbol, kline.Close, "MAC-D", kline.CloseTime);
                        //LogTradeSignal("LONG", kline.Symbol, kline.Close);
                    }
                    else if (divergence == -1)
                    {
                        await OrderManager.PlaceShortOrderAsync(kline.Symbol, kline.Close, "MAC-D", kline.CloseTime);
                        //LogTradeSignal("SHORT", kline.Symbol, kline.Close);
                    }
                }

                    // Check for open trade closing conditions
                var currentPrices = new Dictionary<string, decimal> { { kline.Symbol, kline.Close } };
                await OrderManager.CheckAndCloseTrades(currentPrices, kline.CloseTime);

                // Update MACD results for the next iteration
                macdResults = Indicator.GetMacd(currentQuotes, 25, 125, 9).ToList();
            }
        }

        // Parsing and request creation centralized in StrategyUtils

        private int IdentifyDivergence(List<MacdResult> macdResults)
        {
            if (macdResults.Count < 2)
                return 0;

            var lastMacd = macdResults[macdResults.Count - 1];
            var prevMacd = macdResults[macdResults.Count - 2];

            if (lastMacd.Macd > lastMacd.Signal && prevMacd.Macd < prevMacd.Signal)
            {
                return 1; // Bullish divergence
            }
            else if (lastMacd.Macd < lastMacd.Signal && prevMacd.Macd > prevMacd.Signal)
            {
                return -1; // Bearish divergence
            }

            return 0; // No divergence
        }

        private void LogTradeSignal(string direction, string symbol, decimal price)
        {
            Console.WriteLine($"******MACD Divergence Strategy******************");
            Console.WriteLine($"Go {direction} on {symbol} @ {price} at {DateTime.Now:HH:mm:ss}");
            Console.WriteLine($"************************************************");
        }

        private void HandleErrorResponse(string symbol, RestResponse response)
        {
            Console.WriteLine($"Error for {symbol}: {response.ErrorMessage}");
            Console.WriteLine($"Status Code: {response.StatusCode}");
            Console.WriteLine($"Content: {response.Content}");
        }
    }

    // Snapshot-aware partial for MACD divergence
    public partial class MACDDivergenceStrategy
    {
        public async Task RunAsyncWithSnapshot(string symbol, string interval, Dictionary<string, List<Kline>> snapshot)
        {
            try
            {
                List<Kline>? klines = null;
                if (snapshot != null && snapshot.TryGetValue(symbol, out var s) && s != null && s.Count > 0)
                {
                    klines = s;
                }

                if (klines == null)
                {
                    var request = Helpers.StrategyUtils.CreateGet("/fapi/v1/klines", new Dictionary<string,string>
                    {
                        {"symbol", symbol},
                        {"interval", interval},
                        {"limit", "401"}
                    });

                    var response = await Client.ExecuteGetAsync(request);
                    if (response.IsSuccessful && response.Content != null)
                    {
                        klines = Helpers.StrategyUtils.ParseKlines(response.Content);
                    }
                    else
                    {
                        HandleErrorResponse(symbol, response);
                        return;
                    }
                }

                if (klines != null && klines.Count > 0)
                {
                    var workingKlines = UseClosedCandles ? Helpers.StrategyUtils.ExcludeForming(klines) : klines;
                    await ProcessKlinesAsync(symbol, workingKlines);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing {symbol} with snapshot: {ex.Message}");
            }
        }

        private async Task ProcessKlinesAsync(string symbol, List<Kline> workingKlines)
        {
            var quotes = workingKlines.Select(k => new BinanceTestnet.Models.Quote
            {
                Date = DateTimeOffset.FromUnixTimeMilliseconds(k.OpenTime).UtcDateTime,
                Close = k.Close
            }).ToList();

            var macdResults = Indicator.GetMacd(quotes, 25, 125, 9).ToList();
            var divergence = IdentifyDivergence(macdResults);

            var (signalKline, previousKline) = SelectSignalPair(workingKlines);
            if (signalKline == null || previousKline == null) return;

            if (divergence != 0)
            {
                if (divergence == 1)
                {
                    await OrderManager.PlaceLongOrderAsync(symbol, signalKline.Close, "MAC-D", signalKline.OpenTime);
                }
                else if (divergence == -1)
                {
                    await OrderManager.PlaceShortOrderAsync(symbol, signalKline.Close, "MAC-D", signalKline.OpenTime);
                }
            }
        }
    }
}
