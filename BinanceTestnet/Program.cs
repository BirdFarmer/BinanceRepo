using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using BinanceLive.Strategies;
using BinanceLive.Tools;
using Newtonsoft.Json;
using RestSharp;
using Serilog;
using BinanceTestnet.Enums;
using BinanceLive.Services;
using BinanceTestnet.Models;
using BinanceTestnet.Trading;

namespace BinanceLive
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("Welcome to the Trade Input Program!");

            // Choose Mode
            var operationMode = GetOperationMode();

            // Get User Inputs
            var (entrySize, leverage) = GetEntrySizeAndLeverage(operationMode);
            var tradeDirection = GetTradeDirection();
            var selectedStrategy = GetTradingStrategy();
            var takeProfit = GetTakeProfit(operationMode);
            var fileName = GenerateFileName(operationMode, entrySize, leverage, tradeDirection, selectedStrategy, takeProfit);

            Console.WriteLine($"Excel File: {fileName}");

            // Initialize Logger
            Log.Logger = new LoggerConfiguration()
                .WriteTo.File("program.log")
                .CreateLogger();

            // Fetch API Keys
            var (apiKey, apiSecret) = GetApiKeys();
            if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(apiSecret))
            {
                Console.WriteLine("API key or secret is not set. Please set them as environment variables.");
                return;
            }

            // Initialize Client, Symbols, Wallet
            var client = new RestClient("https://api.binance.com");
            var symbols = GetSymbols(); // List of more than 30 coin pairs
            var intervals = new[] { "1m" }; // Default interval
            var wallet = new Wallet(300); // Initial wallet size

            // Initialize OrderManager and StrategyRunner
            var orderManager = new OrderManager(wallet, leverage, new ExcelWriter(fileName: fileName), operationMode, intervals[0], fileName, takeProfit, tradeDirection, selectedStrategy, client, takeProfit);
            var runner = new StrategyRunner(client, apiKey, symbols, intervals[0], wallet, orderManager, selectedStrategy);

            if (operationMode == OperationMode.Backtest)
            {
                await RunBacktest(client, symbols, intervals[0], wallet, fileName, selectedStrategy, orderManager, runner);
            }
            else
            {
                await RunLiveTrading(client, symbols, intervals[0], wallet, fileName, selectedStrategy, takeProfit, orderManager, runner);
            }
        }

        // Methods to Get User Inputs
        private static OperationMode GetOperationMode()
        {
            Console.WriteLine("Choose Mode:");
            Console.WriteLine("1. Paper Trading");
            Console.WriteLine("2. Backtesting");
            Console.Write("Enter choice (1/2): ");
            string? modeInput = Console.ReadLine();
            return modeInput == "2" ? OperationMode.Backtest : OperationMode.LivePaperTrading;
                }
        private static (decimal entrySize, decimal leverage) GetEntrySizeAndLeverage(OperationMode operationMode)
        {
            Console.Write("Enter Entry Size (default 20 USDT): ");
            string entrySizeInput = Console.ReadLine();
            decimal entrySize = decimal.TryParse(entrySizeInput, out var parsedEntrySize) ? parsedEntrySize : 20;

            decimal leverage = 15; // Default leverage

            if (operationMode == OperationMode.LivePaperTrading)
            {
                Console.Write("Enter Leverage (1 to 25, default 15): ");
                string leverageInput = Console.ReadLine();
                leverage = decimal.TryParse(leverageInput, out var parsedLeverage) ? parsedLeverage : 15;
            }

            return (entrySize, leverage);
        }


        private static SelectedTradeDirection GetTradeDirection()
        {
            Console.WriteLine("Choose Trade Direction (default is both):");
            Console.WriteLine("1. Both Longs and Shorts");
            Console.WriteLine("2. Only Longs");
            Console.WriteLine("3. Only Shorts");
            Console.Write("Enter choice (1/2/3): ");
            string dirInput = Console.ReadLine();
            int directionChoice = int.TryParse(dirInput, out var parsedDirection) ? parsedDirection : 1;

            return directionChoice switch
            {
                2 => SelectedTradeDirection.OnlyLongs,
                3 => SelectedTradeDirection.OnlyShorts,
                _ => SelectedTradeDirection.Both,
            };
        }

        private static SelectedTradingStrategy GetTradingStrategy()
        {
            Console.WriteLine("Select Trading Strategies (default is All combined):");
            Console.WriteLine("1. Loop through all strategies");
            Console.WriteLine("2. 3 SMAs expanding, trade reversal.");
            Console.WriteLine("3. MACD Diversion");
            Console.WriteLine("4. Hull with 200SMA");
            Console.Write("Enter choice (1/2/3/4): ");            
            string stratInput = Console.ReadLine();
            int strategyChoice = int.TryParse(stratInput, out var parsedStrat) ? parsedStrat : 1;

            return strategyChoice switch
            {
                2 => SelectedTradingStrategy.SMAExpansion,
                3 => SelectedTradingStrategy.MACD,
                4 => SelectedTradingStrategy.Aroon,
                _ => SelectedTradingStrategy.All,
            };
        }

        private static decimal GetTakeProfit(OperationMode operationMode)
        {
            if (operationMode == OperationMode.LivePaperTrading)
            {
                Console.Write("Enter Take Profit % (default 1.5%): ");
                
                string tpInput = Console.ReadLine();
                return decimal.TryParse(tpInput, out var parsedTP) ? parsedTP : (decimal)1.5;
            }
            return 1.5M; // Default for backtesting
        }

        private static string GenerateFileName(OperationMode operationMode, decimal entrySize, decimal leverage, SelectedTradeDirection tradeDirection, SelectedTradingStrategy selectedStrategy, decimal takeProfit)
        {
            string title = $"{(operationMode == OperationMode.Backtest ? "Backtest" : "PaperTrade")}_Entry{entrySize}_Leverage{leverage}_Direction{tradeDirection}_Strategy{selectedStrategy}_TakeProfitPercent{takeProfit}_{DateTime.Now:yyyyMMdd-HH-mm}";
            return title.Replace(" ", "_").Replace("%", "Percent").Replace(".", "p") + ".xlsx";
        }

        private static (string apiKey, string apiSecret) GetApiKeys()
        {
            string? apiKey = Environment.GetEnvironmentVariable("BINANCE_API_KEY");
            string? apiSecret = Environment.GetEnvironmentVariable("BINANCE_API_SECRET");
            return (apiKey ?? string.Empty, apiSecret ?? string.Empty);
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

        private static async Task RunBacktest(RestClient client, List<string> symbols, string interval, Wallet wallet, string fileName, SelectedTradingStrategy selectedStrategy, OrderManager orderManager, StrategyRunner runner)
        {
            var backtestTakeProfits = new List<decimal> { 0.3M, 0.6M, 0.8M, 1.0M, 1.3M, 1.5M, 1.7M, 1.9M }; // Take profit percentages

            foreach (var tp in backtestTakeProfits)
            {
                wallet = new Wallet(300); // Reset wallet balance

                orderManager.UpdateParams(wallet, tp); // Update OrderManager with new parameters

                foreach (var symbol in symbols)
                {
                    var historicalData = await FetchHistoricalData(client, symbol, interval);
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

        private static async Task RunLiveTrading(RestClient client, List<string> symbols, string interval, Wallet wallet, string fileName, SelectedTradingStrategy selectedStrategy, decimal takeProfit, OrderManager orderManager, StrategyRunner runner)
        {
            var startTime = DateTime.Now;
            while (true)
            {
                var currentPrices = await FetchCurrentPrices(client, symbols);
                await runner.RunStrategiesAsync();

                Console.WriteLine($"---- Cycle Completed ----");
                if (currentPrices != null)
                {
                    await orderManager.CheckAndCloseTrades(currentPrices);
                    orderManager.PrintActiveTrades(currentPrices);
                    orderManager.PrintWalletBalance();
                }

                var elapsedTime = DateTime.Now - startTime;
                Console.WriteLine($"Elapsed Time: {elapsedTime.Days} days, {elapsedTime.Hours} hours, {elapsedTime.Minutes} minutes");

                var delay = TimeTools.GetTimeSpanFromInterval(interval);
                await Task.Delay(delay);
            }
        }

        // Methods for Fetching Data
        static async Task<Dictionary<string, decimal>> FetchCurrentPrices(RestClient client, List<string> symbols)
        {
            var prices = new Dictionary<string, decimal>();

            foreach (var symbol in symbols)
            {
                var request = new RestRequest("/api/v3/ticker/price", Method.Get);
                request.AddParameter("symbol", symbol);
                var response = await client.ExecuteAsync<Dictionary<string, string>>(request);

                if (response.IsSuccessful && response.Content != null)
                {
                    var priceData = JsonConvert.DeserializeObject<Dictionary<string, string>>(response.Content);
                    if (priceData?.ContainsKey("price") == true)
                    {
                        prices[symbol] = decimal.Parse(priceData["price"], CultureInfo.InvariantCulture);
                    }
                    else
                    {
                        Console.WriteLine($"Failed to fetch price for {symbol}: {response.ErrorMessage}");
                    }
                }
                else
                {
                    Console.WriteLine($"Failed to fetch price for {symbol}: {response.ErrorMessage}");
                }
            }

            return prices;
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
    }
}
