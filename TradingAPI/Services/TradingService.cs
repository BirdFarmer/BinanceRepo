using System.Threading;
using System.Threading.Tasks;
using BinanceTestnet.Enums;
using BinanceTestnet.Models;
using BinanceTestnet.Trading;
using RestSharp;
using BinanceTestnet.Strategies;
using System.Collections.Generic;
using Newtonsoft.Json;
using System.Globalization;
using Microsoft.Extensions.Logging;
using System.IO;

namespace TradingAPI.Services
{
    public class TradingService
    {
        private readonly RestClient _client;
        private readonly string? _apiKey;
        private Wallet _wallet;
        private CancellationTokenSource _cts; // Added for stopping trading
    private string _fileName = string.Empty;

        public TradingService(RestClient client)
        {
            _client = client;
            _apiKey = Environment.GetEnvironmentVariable("BINANCE_API_KEY");
            _wallet = new Wallet(1000); // Default wallet balancecd tra
            _cts = new CancellationTokenSource(); // Initialize cancellation token
        }
        
        public async Task RunTradingAsync(OperationMode operationMode, SelectedTradeDirection direction, 
                                          SelectedTradingStrategy strategy, double? takeProfitPercent, string userName)
        {
            _cts = new CancellationTokenSource(); // Reset token each time trading starts

            var entrySize = 20M;
            var leverage = 15M;
            var tradeDirection = direction;   
            var selectedStrategy = strategy;
            var takeProfitDec = (decimal)(takeProfitPercent ?? 1.0);
            var symbols = GetSymbols();
            
            _fileName = GenerateFileName(operationMode, entrySize, leverage, tradeDirection, selectedStrategy, takeProfitDec, userName);
            
            var interval = "1m";

            // Prepare dependencies for revised OrderManager signature
            using var loggerFactory = LoggerFactory.Create(builder => { });
            var logger = loggerFactory.CreateLogger<OrderManager>();
            var exchangeInfoProvider = new ExchangeInfoStub();
            var sessionId = $"live-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString()[..6]}";
            var databasePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TradingData.db");
            decimal stopLoss = takeProfitDec; // TEMP: mirror TP until provided separately
            decimal tpIteration = takeProfitDec; // retain existing behavior
            var orderManager = new OrderManager(
                wallet: _wallet,
                leverage: leverage,
                operationMode: operationMode,
                interval: interval,
                takeProfit: takeProfitDec,
                stopLoss: stopLoss,
                tradeDirection: tradeDirection,
                tradingStrategy: selectedStrategy,
                client: _client,
                tpIteration: tpIteration,
                margin: entrySize,
                databasePath: databasePath,
                sessionId: sessionId,
                exchangeInfoProvider: exchangeInfoProvider,
                logger: logger,
                onTradeEntered: (_, _, _, _, _) => { });

            if (_apiKey == null)
            {
                throw new System.InvalidOperationException("No API key provided. Cannot continue trading.");
            }

            var runner = new StrategyRunner(_client, _apiKey!, symbols, interval, _wallet, orderManager, new List<SelectedTradingStrategy>{ selectedStrategy });

            if (operationMode == OperationMode.Backtest)
            {
                while (!_cts.Token.IsCancellationRequested) // Will stop when cancellation is requested
                {
                    var backtestTakeProfits = new List<decimal> { 0.3M, 0.6M, 0.8M, 1.0M, 1.3M, 1.5M, 1.7M, 1.9M }; // Take profit percentages
                    foreach (var tp in backtestTakeProfits)
                    {
                        _wallet = new Wallet(1000); // Reset wallet balance

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

                        Console.WriteLine($" -- All TP Cycles done, BACKTEST COMPLETED! -- ");

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

        // Minimal stub until real exchange info wired for TradingAPI runtime
        private class ExchangeInfoStub : IExchangeInfoProvider
        {
            public Task<ExchangeInfo> GetExchangeInfoAsync() => Task.FromResult(new ExchangeInfo { Symbols = new List<SymbolInfo>() });
        }
        
        public void StopTrading()
        {
            if (_cts != null)
            {
                _cts.Cancel(); // Signal to stop trading
            }
        }
/*
        public async Task RunBacktestAsync(SelectedTradingStrategy selectedStrategy, decimal initialWalletSize, decimal takeProfit)
        {
            var entrySize = 20M;
            var leverage = 15M;
            var tradeDirection = SelectedTradeDirection.Both;   
            var symbols = GetSymbols();
            var interval = "1m";
            _fileName = GenerateFileName(OperationMode.Backtest, entrySize, leverage, tradeDirection, selectedStrategy, takeProfit);

            var orderManager = new OrderManager(_wallet, 15, new ExcelWriter(fileName: _fileName), OperationMode.Backtest, interval, _fileName, takeProfit, SelectedTradeDirection.Both, selectedStrategy, _client, takeProfit);
            var runner = new StrategyRunner(_client, _apiKey, symbols, interval, _wallet, orderManager, selectedStrategy);

            await RunBacktest(symbols, interval, _wallet, _fileName, selectedStrategy, orderManager, runner);
        }

        private async Task RunBacktest(List<string> symbols, string interval, Wallet wallet, string fileName, SelectedTradingStrategy selectedStrategy, OrderManager orderManager, StrategyRunner runner)
        {
            Console.WriteLine("Starting backtest...");
            var backtestTakeProfits = new List<decimal> { 0.3M, 0.6M, 0.8M, 1.0M, 1.3M, 1.5M, 1.7M, 1.9M }; // Take profit percentages

            foreach (var tp in backtestTakeProfits)
            {
                wallet = new Wallet(1000); // Reset wallet balance

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
*/
       static async Task<List<Kline>> FetchHistoricalData(RestClient client, string symbol, string interval)
        {
            var historicalData = new List<Kline>();
            var request = new RestRequest("/api/v3/klines", Method.Get);
            request.AddParameter("symbol", symbol);
            request.AddParameter("interval", interval);
            request.AddParameter("limit", 1000);

            var response = await client.ExecuteAsync<List<List<object>>>(request);

            if (response.IsSuccessful && !string.IsNullOrEmpty(response.Content))
            {
                var klineData = JsonConvert.DeserializeObject<List<List<object>>>(response.Content);
                if (klineData != null)
                {
                    foreach (var kline in klineData)
                    {
                        historicalData.Add(new Kline
                        {
                            OpenTime = (long)kline[0],
                            Open = decimal.Parse(kline[1]?.ToString() ?? "0", CultureInfo.InvariantCulture),
                            High = decimal.Parse(kline[2]?.ToString() ?? "0", CultureInfo.InvariantCulture),
                            Low = decimal.Parse(kline[3]?.ToString() ?? "0", CultureInfo.InvariantCulture),
                            Close = decimal.Parse(kline[4]?.ToString() ?? "0", CultureInfo.InvariantCulture),
                            CloseTime = (long)kline[6],
                            NumberOfTrades = int.Parse(kline[8]?.ToString() ?? "0", CultureInfo.InvariantCulture)
                        });
                    }
                }
                else
                {
                    Console.WriteLine($"Failed to parse historical data for {symbol}.");
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
        
        private static string GenerateFileName(OperationMode operationMode, decimal entrySize, decimal leverage, 
                                               SelectedTradeDirection tradeDirection, SelectedTradingStrategy selectedStrategy, 
                                               decimal takeProfit, string userName)
        {
            string title = $"{userName}_{(operationMode == OperationMode.Backtest ? "Backtest" : "PaperTrade")}_Entry{entrySize}_Leverage{leverage}_Direction{tradeDirection}_Strategy{selectedStrategy}_TakeProfitPercent{takeProfit}_{DateTime.Now:yyyyMMdd-HH-mm}";
            return title.Replace(" ", "_").Replace("%", "Percent").Replace(".", "p") + ".xlsx";
        }
    }
}
