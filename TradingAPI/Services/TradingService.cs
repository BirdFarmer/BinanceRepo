using System.Threading;
using System.Threading.Tasks;
using BinanceTestnet.Enums;
using BinanceTestnet.Models;
using BinanceTestnet.Trading;
using RestSharp;
using BinanceLive.Strategies;
using System.Collections.Generic;
using Newtonsoft.Json;
using System.Globalization;

namespace TradingAPI.Services
{
    public class TradingService
    {
        private readonly RestClient _client;
        private readonly string? _apiKey;
        private Wallet _wallet;
        private CancellationTokenSource _cts; // Added for stopping trading

        public TradingService(RestClient client)
        {
            _client = client;
            _apiKey = Environment.GetEnvironmentVariable("BINANCE_API_KEY");
            _wallet = new Wallet(300); // Default wallet balancecd tra
            _cts = new CancellationTokenSource(); // Initialize cancellation token
        }

        public async Task RunTradingAsync(OperationMode operationMode)
        {
            _cts = new CancellationTokenSource(); // Reset token each time trading starts

            var entrySize = 20M;
            var leverage = 15M;
            var tradeDirection = SelectedTradeDirection.Both;   
            var selectedStrategy = SelectedTradingStrategy.All;
            var takeProfit = 1.5M;
            var symbols = GetSymbols();
            
            var interval = "1m";

            var orderManager = new OrderManager(_wallet, leverage, new ExcelWriter(fileName: "default.xlsx"), operationMode, interval, "default.xlsx", takeProfit, tradeDirection, selectedStrategy, _client, takeProfit);

            if (_apiKey == null)
            {
                throw new System.InvalidOperationException("No API key provided. Cannot continue trading.");
            }

            var runner = new StrategyRunner(_client, _apiKey, symbols, interval, _wallet, orderManager, selectedStrategy);

            if (operationMode == OperationMode.Backtest)
            {
                while (!_cts.Token.IsCancellationRequested) // Will stop when cancellation is requested
                {
                    var backtestTakeProfits = new List<decimal> { 0.3M, 0.6M, 0.8M, 1.0M, 1.3M, 1.5M, 1.7M, 1.9M }; // Take profit percentages
                    foreach (var tp in backtestTakeProfits)
                    {
                        _wallet = new Wallet(300); // Reset wallet balance

                        orderManager.UpdateParams(_wallet, tp); // Update OrderManager with new parameters

                        foreach (var symbol in symbols)
                        {
                            var historicalData = await FetchHistoricalData(_client, symbol, interval);
                            foreach (var kline in historicalData)
                            {
                                kline.Symbol = symbol;
                            }

                            Console.WriteLine($" -- Coin: {symbol} TF: {interval} Lev: 15 TP: {tp} -- ");
                            await runner.RunStrategiesOnHistoricalDataAsync(historicalData);
                        }

                        orderManager.PrintWalletBalance();
                    }

                    StopTrading();
                }
            }
            else
            {
                while (!_cts.Token.IsCancellationRequested) // Will stop when cancellation is requested
                {
                    await runner.RunStrategiesAsync();
                    await Task.Delay(60000, _cts.Token); // Respect cancellation during delay
                }
            }
        }

        public void StopTrading()
        {
            if (_cts != null)
            {
                _cts.Cancel(); // Signal to stop trading
            }
        }

        public async Task RunBacktestAsync(SelectedTradingStrategy selectedStrategy, decimal initialWalletSize, decimal takeProfit)
        {
            var symbols = GetSymbols();
            var interval = "1m";
            var fileName = "backtest_results.xlsx";

            var orderManager = new OrderManager(_wallet, 15, new ExcelWriter(fileName: fileName), OperationMode.Backtest, interval, fileName, takeProfit, SelectedTradeDirection.Both, selectedStrategy, _client, takeProfit);
            var runner = new StrategyRunner(_client, _apiKey, symbols, interval, _wallet, orderManager, selectedStrategy);

            await RunBacktest(symbols, interval, _wallet, fileName, selectedStrategy, orderManager, runner);
        }

        private async Task RunBacktest(List<string> symbols, string interval, Wallet wallet, string fileName, SelectedTradingStrategy selectedStrategy, OrderManager orderManager, StrategyRunner runner)
        {
            Console.WriteLine("Starting backtest...");
            var backtestTakeProfits = new List<decimal> { 0.3M, 0.6M, 0.8M, 1.0M, 1.3M, 1.5M, 1.7M, 1.9M }; // Take profit percentages

            foreach (var tp in backtestTakeProfits)
            {
                wallet = new Wallet(300); // Reset wallet balance

                orderManager.UpdateParams(wallet, tp); // Update OrderManager with new parameters

                foreach (var symbol in symbols)
                {
                    var historicalData = await FetchHistoricalData(_client, symbol, interval);
                    foreach (var kline in historicalData)
                    {
                        kline.Symbol = symbol;
                    }

                    Console.WriteLine($" -- Coin: {symbol} TF: {interval} Lev: 15 TP: {tp} -- ");
                    await runner.RunStrategiesOnHistoricalDataAsync(historicalData);
                }

                orderManager.PrintWalletBalance();
            }
        }

       static async Task<List<Kline>> FetchHistoricalData(RestClient client, string symbol, string interval)
        {
            var historicalData = new List<Kline>();
            var request = new RestRequest("/api/v3/klines", Method.Get);
            request.AddParameter("symbol", symbol);
            request.AddParameter("interval", interval);
            request.AddParameter("limit", 1000);

            var response = await client.ExecuteAsync<List<List<object>>>(request);

            if (response.IsSuccessful && response.Content != null)
            {
                var klineData = JsonConvert.DeserializeObject<List<List<object>>>(response.Content);
                foreach (var kline in klineData)
                {
                    historicalData.Add(new Kline
                    {
                        OpenTime = (long)kline[0],
                        Open = decimal.Parse(kline[1].ToString(), CultureInfo.InvariantCulture),
                        High = decimal.Parse(kline[2].ToString(), CultureInfo.InvariantCulture),
                        Low = decimal.Parse(kline[3].ToString(), CultureInfo.InvariantCulture),
                        Close = decimal.Parse(kline[4].ToString(), CultureInfo.InvariantCulture),
                        CloseTime = (long)kline[6],
                        NumberOfTrades = int.Parse(kline[8].ToString(), CultureInfo.InvariantCulture)
                    });
                }
            }
            else
            {
                Console.WriteLine($"Failed to fetch historical data for {symbol}: {response.ErrorMessage}");
            }
            return historicalData;
        }

        private static List<string> GetSymbols()
        {
            return new List<string>
            {
                "FTMUSDT", "XRPUSDT", "BTCUSDT", "ETHUSDT", "TIAUSDT", "SUIUSDT", "BCHUSDT", "AVAXUSDT", "MATICUSDT", "YGGUSDT", 
                "DOGEUSDT", "HBARUSDT", "SEIUSDT", "STORJUSDT", "ADAUSDT", "DOTUSDT", "FETUSDT", "THETAUSDT", "ONTUSDT", "QNTUSDT",
                "ARUSDT", "LINKUSDT", "ATOMUSDT", "TRBUSDT", "SUSHIUSDT", "BNBUSDT", "ORDIUSDT", "SANDUSDT", "INJUSDT", "AXSUSDT", 
                "ENSUSDT", "LTCUSDT", "XLMUSDT"
            };
        }
    }
}
