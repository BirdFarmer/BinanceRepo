// Program.cs
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BinanceLive.Strategies;
using BinanceLive.Tools;
using RestSharp;

namespace BinanceLive
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var apiKey = Environment.GetEnvironmentVariable("BINANCE_API_KEY");
            var apiSecret = Environment.GetEnvironmentVariable("BINANCE_API_SECRET");

            if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(apiSecret))
            {
                Console.WriteLine("API key or secret is not set. Please set them as environment variables.");
                return;
            }

            var client = new RestClient("https://api.binance.com");
            Console.WriteLine("RestClient initialized.");

            var symbols = new List<string> 
            {
                "BTCUSDT", "ETHUSDT", "TIAUSDT", "SUIUSDT", "XRPUSDT", "BCHUSDT", "AVAXUSDT", "MATICUSDT", "RNDRUSDT", 
                "DOGEUSDT", "HBARUSDT", "SEIUSDT", "STORJUSDT", "EOSUSDT", "FETUSDT", "SHIBUSDT", "THETAUSDT", "PEPEUSDT", 
                "ARUSDT", "LINKUSDT", "FTMUSDT", "ATOMUSDT", "TRBUSDT", "SUSHIUSDT", "BNBUSDT", "ORDIUSDT"
            };

            var interval = args.Length > 0 ? args[0] : "1m";
            Console.WriteLine($"Interval: {interval}");

            var wallet = new Wallet(200);
            var orderManager = new OrderManager(wallet);

            var runner = new StrategyRunner(client, apiKey, symbols, interval, wallet, orderManager);

            var smaExpansionStrategy = new SMAExpansionStrategy(client, apiKey, orderManager, wallet);
            var fvgStrategy = new FVGStrategy(client, apiKey, orderManager, wallet);
            var macdDivergenceStrategy = new MACDDivergenceStrategy(client, apiKey, orderManager, wallet);

            while (true)
            {
                await runner.RunStrategiesAsync(smaExpansionStrategy, fvgStrategy, macdDivergenceStrategy);

                // Log active trades and wallet balance at the end of each cycle
                Console.WriteLine("---- Cycle Completed ----");                
                var currentPrices = await runner.GetCurrentPricesAsync();
                orderManager.CheckAndCloseTrades(currentPrices);
                orderManager.PrintActiveTrades(currentPrices);
                orderManager.PrintWalletBalance();

                var delay = TimeTools.GetTimeSpanFromInterval(interval);
                await Task.Delay(delay);
            }
        }
    }
}
