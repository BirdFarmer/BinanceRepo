using System.Threading.Tasks;
using BinanceTestnet.Enums;
using BinanceTestnet.Models;
using BinanceTestnet.Trading;
using RestSharp;
using BinanceLive.Strategies;
using System.Collections.Generic;

namespace TradingAPI.Services
{
    public class TradingService
    {
        private readonly RestClient _client;
        private readonly string? _apiKey;
        private readonly Wallet _wallet;

        public TradingService(RestClient client)
        {
            _client = client;
            _apiKey = Environment.GetEnvironmentVariable("BINANCE_API_KEY");
            _wallet = new Wallet(1000); // Default wallet balance
        }

        public async Task RunTradingAsync(OperationMode operationMode)
        {
            // Default values
            var entrySize = 20M;
            var leverage = 15M;
            var tradeDirection = SelectedTradeDirection.Both;
            var selectedStrategy = SelectedTradingStrategy.All;
            var takeProfit = 1.5M;
            var symbols = new List<string>
            {
                "BTCUSDT", "ETHUSDT", "BNBUSDT", // Default symbols, add more as needed
            };
            var interval = "1m"; // Default interval

            var orderManager = new OrderManager(_wallet, leverage, new ExcelWriter(fileName: "default.xlsx"), operationMode, interval, "default.xlsx", takeProfit, tradeDirection, selectedStrategy, _client);

            if(_apiKey == null) 
            { 
                Console.WriteLine("No API key provided. Cannot continue trading.");
                throw new System.InvalidOperationException("No API key provided. Cannot continue trading.");          
            }
            var runner = new StrategyRunner(_client, _apiKey, symbols, interval, _wallet, orderManager, selectedStrategy);

            if (operationMode == OperationMode.Backtest)
            {
                List<Kline> historicalData = await FetchHistoricalData(symbols, interval);
                await runner.RunStrategiesOnHistoricalDataAsync(historicalData);
            }
            else
            {
                while (true)
                {
                    await runner.RunStrategiesAsync();
                    await Task.Delay(60000); // Delay of 1 minute between cycles
                }
            }
        }
        private async Task<List<Kline>> FetchHistoricalData(List<string> symbols, string interval)
        {
            return await Task.Run(() =>
            {
            var historicalData = new List<Kline>();
            // Fetch historical data implementation...
            return historicalData;
            });
        }
    }
}
