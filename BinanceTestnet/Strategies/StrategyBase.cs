using BinanceTestnet.Models;
using RestSharp;
using BinanceTestnet.Trading;

namespace BinanceTestnet.Strategies
{
    public abstract class StrategyBase
    {
        protected RestClient Client { get; }
        protected string ApiKey { get; }
        protected OrderManager OrderManager { get; }
        protected Wallet Wallet { get; }
        // Strategy capability: can this strategy operate on closed candles?
        protected virtual bool SupportsClosedCandles => false;

        // How many historical candles this strategy requires to compute indicators correctly.
        // Runner will compute the per-cycle snapshot size as the max of all selected strategies' requirements.
        // Override in strategies that need large history (e.g. Aroon).
        public virtual int RequiredHistory => 150;

        // Effective policy: closed-candle toggle only applies if the strategy supports it
        protected bool UseClosedCandles => Helpers.StrategyRuntimeConfig.UseClosedCandles && SupportsClosedCandles;

        protected StrategyBase(RestClient client, string apiKey, OrderManager orderManager, Wallet wallet)
        {
            Client = client;
            ApiKey = apiKey;
            OrderManager = orderManager;
            Wallet = wallet;

            // One-time diagnostic to make it obvious in logs what mode will be used per strategy instance
            try
            {
                var name = GetType().Name;
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {name}: Candle policy = {(UseClosedCandles ? "Closed" : "Forming")} (supportsClosed={SupportsClosedCandles})");
            }
            catch { /* do not fail strategies due to logging issues */ }
        }

        public abstract Task RunAsync(string symbol, string interval);

        // New abstract method for historical data
        public abstract Task RunOnHistoricalDataAsync(IEnumerable<Kline> historicalData);

        // Shared helper for selecting the signal/previous kline following the global policy
        protected (Kline? signal, Kline? previous) SelectSignalPair(IReadOnlyList<Kline> klines)
            => Helpers.StrategyUtils.SelectSignalPair(klines, UseClosedCandles);

        protected List<Quote> ToIndicatorQuotes(IReadOnlyList<Kline> klines)
            => Helpers.StrategyUtils.ToIndicatorQuotes(klines, UseClosedCandles);
    }
}
