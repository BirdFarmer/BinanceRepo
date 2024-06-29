﻿// Program.cs
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BinanceLive.Strategies;
using BinanceLive.Tools;
using RestSharp;
using Newtonsoft.Json;
using System.Globalization;

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
                var currentPrices = await FetchCurrentPrices(client, symbols);
                await runner.RunStrategiesAsync(smaExpansionStrategy); //, fvgStrategy, macdDivergenceStrategy);

                orderManager.CheckAndCloseTrades(currentPrices);

                Console.WriteLine("---- Cycle Completed ----");
                orderManager.PrintActiveTrades(currentPrices);
                orderManager.PrintWalletBalance();

                var delay = TimeTools.GetTimeSpanFromInterval(interval);
                await Task.Delay(delay);
            }
        }

        static async Task<Dictionary<string, decimal>> FetchCurrentPrices(RestClient client, List<string> symbols)
        {
            var prices = new Dictionary<string, decimal>();

            foreach (var symbol in symbols)
            {
                var request = new RestRequest("/api/v3/ticker/price", Method.Get);
                request.AddParameter("symbol", symbol);
                var response = await client.ExecuteAsync<Dictionary<string, string>>(request);

                if (response.IsSuccessful)
                {
                    var priceData = JsonConvert.DeserializeObject<Dictionary<string, string>>(response.Content);
                    prices[symbol] = decimal.Parse(priceData["price"], CultureInfo.InvariantCulture);
                }
                else
                {
                    Console.WriteLine($"Failed to fetch price for {symbol}: {response.ErrorMessage}");
                }
            }

            return prices;
        }
    }
}