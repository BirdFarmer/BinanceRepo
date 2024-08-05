using BinanceLive.Models;
using RestSharp;

public abstract class StrategyBase
{
    protected RestClient Client { get; }
    protected string ApiKey { get; }
    protected OrderManager OrderManager { get; }
    protected Wallet Wallet { get; }

    protected StrategyBase(RestClient client, string apiKey, OrderManager orderManager, Wallet wallet)
    {
        Client = client;
        ApiKey = apiKey;
        OrderManager = orderManager;
        Wallet = wallet;
    }

    public abstract Task RunAsync(string symbol, string interval);

    // New abstract method for historical data
    public abstract Task  RunOnHistoricalDataAsync(IEnumerable<Kline> historicalData);
}
