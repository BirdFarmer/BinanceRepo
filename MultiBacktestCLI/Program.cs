using BinanceTestnet.Tools;
using BinanceTestnet.Trading;
using BinanceTestnet.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using RestSharp;

class ExchangeInfoStub : IExchangeInfoProvider
{
    public Task<ExchangeInfo> GetExchangeInfoAsync()
        => Task.FromResult(new ExchangeInfo { Symbols = new List<SymbolInfo>() });
}

class Program
{
    static async Task<int> Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: MultiBacktestCLI <config.json> <databasePath>");
            return 1;
        }
        var configPath = args[0];
        var databasePath = args[1];
        if (!File.Exists(configPath))
        {
            Console.WriteLine($"Config file not found: {configPath}");
            return 1;
        }

        var client = new RestClient("https://fapi.binance.com");
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<BinanceTestnet.Trading.OrderManager>();
        var exchangeInfoProvider = new ExchangeInfoStub();

        var cfg = MultiBacktestRunner.LoadConfig(configPath);
        var runner = new MultiBacktestRunner(client, apiKey: "", databasePath: databasePath, logger: logger, exchangeInfoProvider: exchangeInfoProvider);
        await runner.RunAsync(cfg);

        Console.WriteLine("Done. See results/multi/multi_results.csv");
        return 0;
    }
}
