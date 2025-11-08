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
        protected bool UseClosedCandles => Helpers.CandlePolicy.UseClosed;

        protected StrategyBase(RestClient client, string apiKey, OrderManager orderManager, Wallet wallet)
        {
            Client = client;
            ApiKey = apiKey;
            OrderManager = orderManager;
            Wallet = wallet;
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
