using BinanceTestnet.Models;
using RestSharp;
using Skender.Stock.Indicators;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using BinanceTestnet.Trading;

namespace BinanceTestnet.Strategies
{
    public class EmaCrossoverVolumeStrategy : StrategyBase
    {
        // Strategy parameters (tweakable at runtime via Settings UI)
        public static int FastEmaLength = 25;
        public static int SlowEmaLength = 50;
        public static int VolumeMaLength = 20;
        public static decimal VolumeMultiplier = 1.0m;

        protected override bool SupportsClosedCandles => true;

        public EmaCrossoverVolumeStrategy(RestClient client, string apiKey, OrderManager orderManager, Wallet wallet)
            : base(client, apiKey, orderManager, wallet)
        {
        }

        public override async Task RunAsync(string symbol, string interval)
        {
            try
            {
                var request = Helpers.StrategyUtils.CreateGet("/fapi/v1/klines", new Dictionary<string, string>
                {
                    { "symbol", symbol },
                    { "interval", interval },
                    { "limit", "400" }
                });

                decimal lastPrice = 0;

                var response = await Client.ExecuteGetAsync(request);
                if (response.IsSuccessful && response.Content != null)
                {
                    var klines = Helpers.StrategyUtils.ParseKlines(response.Content);
                    if (klines != null && klines.Count > 0)
                    {
                        var (signalKline, previousKline) = SelectSignalPair(klines);
                        if (signalKline == null || previousKline == null) return;

                        var quotes = ToIndicatorQuotes(klines);
                        var emaFast = Indicator.GetEma(quotes, FastEmaLength).ToList();
                        var emaSlow = Indicator.GetEma(quotes, SlowEmaLength).ToList();

                        if (emaFast.Count > 1 && emaSlow.Count > 1)
                        {
                            var lastEmaFast = emaFast.Last();
                            var prevEmaFast = emaFast.Count > 1 ? emaFast[emaFast.Count - 2] : null;
                            var lastEmaSlow = emaSlow.Last();
                            var prevEmaSlow = emaSlow.Count > 1 ? emaSlow[emaSlow.Count - 2] : null;

                            if (lastEmaFast.Ema != null && lastEmaSlow.Ema != null && prevEmaFast?.Ema != null && prevEmaSlow?.Ema != null)
                            {
                                // Volume baseline (20-period simple average of volume)
                                var startIdx = Math.Max(0, klines.Count - VolumeMaLength);
                                var avgVol = klines.Skip(startIdx).Take(VolumeMaLength).Select(k => k.Volume).DefaultIfEmpty(0m).Average();
                                var currVol = signalKline.Volume;

                                // Crossover detection using previous vs current EMA
                                bool longCross = prevEmaFast.Ema <= prevEmaSlow.Ema && lastEmaFast.Ema > lastEmaSlow.Ema;
                                bool shortCross = prevEmaFast.Ema >= prevEmaSlow.Ema && lastEmaFast.Ema < lastEmaSlow.Ema;

                                bool volOk = avgVol == 0 ? true : currVol > avgVol * VolumeMultiplier;

                                if (longCross && volOk)
                                {
                                    await OrderManager.PlaceLongOrderAsync(symbol, signalKline.Close, "EMA25/50-Vol", signalKline.OpenTime);
                                    LogTradeSignal("LONG", symbol, signalKline.Close, currVol, avgVol, VolumeMultiplier);
                                    lastPrice = signalKline.Close;
                                }
                                else if (shortCross && volOk)
                                {
                                    await OrderManager.PlaceShortOrderAsync(symbol, signalKline.Close, "EMA25/50-Vol", signalKline.OpenTime);
                                    LogTradeSignal("SHORT", symbol, signalKline.Close, currVol, avgVol, VolumeMultiplier);
                                    lastPrice = signalKline.Close;
                                }
                            }
                        }

                        if (lastPrice > 0)
                        {
                            var currentPrices = new Dictionary<string, decimal> { { symbol, lastPrice } };
                            await OrderManager.CheckAndCloseTrades(currentPrices);
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
            var quotes = klines.Select(k => new BinanceTestnet.Models.Quote
            {
                Date = DateTimeOffset.FromUnixTimeMilliseconds(k.OpenTime).UtcDateTime,
                Close = k.Close,
                Volume = k.Volume
            }).ToList();

            var emaFast = Indicator.GetEma(quotes, FastEmaLength).ToList();
            var emaSlow = Indicator.GetEma(quotes, SlowEmaLength).ToList();

            // start at 1 because we compare with previous
            for (int i = 1; i < emaFast.Count; i++)
            {
                var currentKline = klines.ElementAtOrDefault(i);
                if (currentKline == null) continue;

                var currEmaFast = emaFast[i];
                var prevEmaFast = emaFast[i - 1];
                var currEmaSlow = emaSlow[i];
                var prevEmaSlow = emaSlow[i - 1];

                if (currEmaFast == null || prevEmaFast == null || currEmaSlow == null || prevEmaSlow == null) continue;

                // avg volume over previous 20 bars ending at i (inclusive)
                int volStart = Math.Max(0, i - VolumeMaLength + 1);
                var avgVol = klines.Skip(volStart).Take(VolumeMaLength).Select(k => k.Volume).DefaultIfEmpty(0m).Average();
                var currVol = currentKline.Volume;

                bool longCross = prevEmaFast.Ema <= prevEmaSlow.Ema && currEmaFast.Ema > currEmaSlow.Ema;
                bool shortCross = prevEmaFast.Ema >= prevEmaSlow.Ema && currEmaFast.Ema < currEmaSlow.Ema;

                bool volOk = avgVol == 0 ? true : currVol > avgVol * VolumeMultiplier;

                if (longCross && volOk)
                {
                    await OrderManager.PlaceLongOrderAsync(currentKline.Symbol, currentKline.Close, "EMA25/50-Vol", currentKline.OpenTime);
                    LogTradeSignal("LONG", currentKline.Symbol, currentKline.Close, currVol, avgVol, VolumeMultiplier);
                }
                else if (shortCross && volOk)
                {
                    await OrderManager.PlaceShortOrderAsync(currentKline.Symbol, currentKline.Close, "EMA25/50-Vol", currentKline.OpenTime);
                    LogTradeSignal("SHORT", currentKline.Symbol, currentKline.Close, currVol, avgVol, VolumeMultiplier);
                }

                var currentPrices = new Dictionary<string, decimal> { { currentKline.Symbol, currentKline.Close } };
                await OrderManager.CheckAndCloseTrades(currentPrices, currentKline.OpenTime);
            }
        }

        private void LogTradeSignal(string direction, string symbol, decimal price, decimal currentVolume, decimal avgVolume, decimal volumeMultiplier = 1.0m)
        {
            Console.WriteLine("****** EMA25/50 Crossover with Volume ******************");
            Console.WriteLine($"{direction}: {symbol} @ {price} (Volume: {currentVolume} > {avgVolume}×{volumeMultiplier})");
            Console.WriteLine("******************************************************");
        }

        private void HandleErrorResponse(string symbol, RestResponse response)
        {
            Console.WriteLine($"Error for {symbol}: {response.ErrorMessage}");
            Console.WriteLine($"Status Code: {response.StatusCode}");
            Console.WriteLine($"Content: {response.Content}");
        }
    }
}
