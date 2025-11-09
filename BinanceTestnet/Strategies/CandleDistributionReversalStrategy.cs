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
    public class CandleDistributionReversalStrategy : StrategyBase
    {
        protected override bool SupportsClosedCandles => true;
        public CandleDistributionReversalStrategy(RestClient client, string apiKey, OrderManager orderManager, Wallet wallet)
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
                        // Respect closed-candle policy by excluding forming candle for pattern stats
                        var workingKlines = UseClosedCandles ? Helpers.StrategyUtils.ExcludeForming(klines) : klines;
                        var signal = IdentifySignal(workingKlines);
                        var (signalKline, previousKline) = SelectSignalPair(klines);
                        if (signalKline == null || previousKline == null) return;

                        if (signal != 0)
                        {
                            if (signal == 1)
                            {
                                await OrderManager.PlaceLongOrderAsync(symbol, signalKline.Close, "CandleDistReversal", signalKline.OpenTime);
                                LogTradeSignal("LONG", symbol, signalKline.Close);
                            }
                            else if (signal == -1)
                            {
                                await OrderManager.PlaceShortOrderAsync(symbol, signalKline.Close, "CandleDistReversal", signalKline.OpenTime);
                                LogTradeSignal("SHORT", symbol, signalKline.Close);
                            }
                        }
                        else
                        {
                            //Console.WriteLine($"No signal identified for {symbol}.");
                        }
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
            var klines = historicalData.ToList();

            foreach (var kline in klines)
            {
                if (kline.Symbol == null) continue;

                var currentKlines = klines.TakeWhile(k => k.OpenTime <= kline.OpenTime).ToList();
                var signal = IdentifySignal(currentKlines);

                if (signal != 0)
                {
                    if (signal == 1)
                    {
                        await OrderManager.PlaceLongOrderAsync(kline.Symbol, kline.Close, "CandleDistReversal", kline.CloseTime);
                        LogTradeSignal("LONG", kline.Symbol, kline.Close);
                    }
                    else if (signal == -1)
                    {
                        await OrderManager.PlaceShortOrderAsync(kline.Symbol, kline.Close, "CandleDistReversal", kline.CloseTime);
                        LogTradeSignal("SHORT", kline.Symbol, kline.Close);
                    }
                }

                // Check for open trade closing conditions
                var currentPrices = new Dictionary<string, decimal> { { kline.Symbol, kline.Close } };
                await OrderManager.CheckAndCloseTrades(currentPrices, kline.CloseTime);
            }
        }

        // Request & parse centralized in StrategyUtils

        private int IdentifySignal(List<Kline> klines)
        {
            // Input parameters
            int longTermLookback = 100; // Lookback period for long-term trend
            int shortTermLookback = 6; // Lookback period for short-term exhaustion
            double greenThreshold = 0.62; // 65% green candles for strong uptrend
            double redThreshold = 0.62; // 65% red candles for strong downtrend
            // int rsiPeriod = 14; // RSI period
            // double rsiOverbought = 70; // RSI threshold for overbought
            // double rsiOversold = 30; // RSI threshold for oversold

            if (klines.Count < longTermLookback || klines.Count < shortTermLookback)
                return 0; // Not enough data

            // Count green and red candles in the long-term lookback period
            var longTermKlines = klines.TakeLast(longTermLookback).ToList();
            int greenCandlesLongTerm = longTermKlines.Count(k => k.Close > k.Open);
            int redCandlesLongTerm = longTermLookback - greenCandlesLongTerm;

            // Count green and red candles in the short-term lookback period
            var shortTermKlines = klines.TakeLast(shortTermLookback).ToList();
            int greenCandlesShortTerm = shortTermKlines.Count(k => k.Close > k.Open);
            int redCandlesShortTerm = shortTermLookback - greenCandlesShortTerm;

            // Calculate RSI
            // var quotes = klines.Select(k => new BinanceTestnet.Models.Quote
            // {
            //     Date = DateTimeOffset.FromUnixTimeMilliseconds(k.OpenTime).UtcDateTime,
            //     Close = k.Close
            // }).ToList();
            // var rsiResults = Indicator.GetRsi(quotes, rsiPeriod).ToList();
            // double? currentRsi = rsiResults.Last().Rsi;

            // Long-term trend conditions
            bool isStrongUptrend = (double)greenCandlesLongTerm / longTermLookback > greenThreshold;
            bool isStrongDowntrend = (double)redCandlesLongTerm / longTermLookback > redThreshold;

            // Short-term exhaustion conditions
            bool isShortTermBalanced = greenCandlesShortTerm == redCandlesShortTerm;

            // Short Condition (for catching tops in a strong uptrend)
            if (isStrongUptrend && isShortTermBalanced)// && currentRsi > rsiOverbought)
                return -1; // Short signal

            // Long Condition (for catching bottoms in a strong downtrend)
            if (isStrongDowntrend && isShortTermBalanced)// && currentRsi < rsiOversold)
                return 1; // Long signal

            return 0; // No signal
        }

        private void LogTradeSignal(string direction, string symbol, decimal price)
        {
            Console.WriteLine($"******Candle Distribution Reversal Strategy******************");
            Console.WriteLine($"Go {direction} on {symbol} @ {price} at {DateTime.Now:HH:mm:ss}");
            Console.WriteLine($"***********************************************************");
        }

        private void HandleErrorResponse(string symbol, RestResponse response)
        {
            Console.WriteLine($"Error for {symbol}: {response.ErrorMessage}");
            Console.WriteLine($"Status Code: {response.StatusCode}");
            Console.WriteLine($"Content: {response.Content}");
        }
    }
}