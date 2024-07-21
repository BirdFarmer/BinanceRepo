using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BinanceLive.Strategies;
using BinanceLive.Tools;
using RestSharp;
using Newtonsoft.Json;
using System.Globalization;
using Serilog;
using OfficeOpenXml;
using BinanceTestnet.Enums;
using BinanceLive.Models;

namespace BinanceLive
{
    class Program
    {
        static async Task Main(string[] args)
        {
            // Step-by-step user input
            Console.WriteLine("Welcome to the Trade Input Program!");

            // Step 1: Choose Mode
            Console.WriteLine("Choose Mode:");
            Console.WriteLine("1. Paper Trading");
            Console.WriteLine("2. Backtesting");
            Console.Write("Enter choice (1/2): ");
            string modeInput = Console.ReadLine();
            int modeChoice = string.IsNullOrEmpty(modeInput) ? 1 : int.Parse(modeInput);

            // Set operation mode based on user choice
            OperationMode operationMode = modeChoice switch
            {
                2 => OperationMode.Backtest,
                _ => OperationMode.LivePaperTrading,
            };

            // Step 2: Choose Entry Size
            Console.Write("Enter Entry Size (default 20 USDT): ");
            string entrySizeInput = Console.ReadLine();
            decimal entrySize = string.IsNullOrEmpty(entrySizeInput) ? 20 : decimal.Parse(entrySizeInput);

            decimal leverage;
            if (operationMode == OperationMode.LivePaperTrading)
            {
                // Step 3: Leverage (for Paper Trading)
                Console.Write("Enter Leverage (1 to 25, default 15): ");
                string leverageInput = Console.ReadLine();
                leverage = string.IsNullOrEmpty(leverageInput) ? 15 : decimal.Parse(leverageInput);
            }
            else
            {
                leverage = 1; // Default starting leverage for backtesting
            }

            // Step 4: Direction
            Console.WriteLine("Choose Trade Direction (default is both):");
            Console.WriteLine("1. Both Longs and Shorts");
            Console.WriteLine("2. Only Longs");
            Console.WriteLine("3. Only Shorts");
            Console.Write("Enter choice (1/2/3): ");
            string directionInput = Console.ReadLine();
            int directionChoice = string.IsNullOrEmpty(directionInput) ? 1 : int.Parse(directionInput);

            SelectedTradeDirection tradeDirection = directionChoice switch
            {
                2 => SelectedTradeDirection.OnlyLongs,
                3 => SelectedTradeDirection.OnlyShorts,
                _ => SelectedTradeDirection.Both,
            };

            // Step 5: Strategies
            Console.WriteLine("Select Trading Strategies (default is both):");
            Console.WriteLine("1. SMA Expansion");
            Console.WriteLine("2. MACD");
            Console.WriteLine("3. Both");
            Console.Write("Enter choice (1/2/3): ");
            string strategyInput = Console.ReadLine();
            int strategyChoice = string.IsNullOrEmpty(strategyInput) ? 3 : int.Parse(strategyInput);

            SelectedTradingStrategy selectedStrategy = strategyChoice switch
            {
                1 => SelectedTradingStrategy.SMAExpansion,
                2 => SelectedTradingStrategy.MACD,
                _ => SelectedTradingStrategy.Both,
            };

            // Step 6: Take Profit %
            Console.Write("Enter Take Profit % (default 1.5%): ");
            string takeProfitInput = Console.ReadLine();
            decimal takeProfit = string.IsNullOrEmpty(takeProfitInput) ? 1.5M : decimal.Parse(takeProfitInput);

            // Concatenate user inputs to form a title, excluding Take Profit percentage
            string title = $"{(operationMode == OperationMode.Backtest ? "Backtest" : "PaperTrade")}_Entry{entrySize}_Leverage{leverage}_Direction{tradeDirection.ToString().Replace(" ", "")}_Strategy{selectedStrategy.ToString().Replace(" ", "")}";

            // Clean up the title to be a valid file name
            string fileName = $"{title.Replace(" ", "_").Replace("%", "Percent").Replace(".", "p")}.xlsx";

            // Output the collected data
            Console.WriteLine("\nSummary:");
            Console.WriteLine($"Mode: {(operationMode == OperationMode.Backtest ? "Backtesting" : "Paper Trading")}");
            Console.WriteLine($"Entry Size: {entrySize} USDT");
            Console.WriteLine($"Leverage: {leverage}x");
            Console.WriteLine($"Trade Direction: {tradeDirection}");
            Console.WriteLine($"Trading Strategies: {selectedStrategy}");
            Console.WriteLine($"Take Profit: {takeProfit}%");
            Console.WriteLine($"Excel File: {fileName}");

            // Proceed with the rest of the program
            Log.Logger = new LoggerConfiguration()
                .WriteTo.File("program.log")
                .CreateLogger();

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
                "FTMUSDT", "XRPUSDT", "BTCUSDT", "ETHUSDT", "TIAUSDT", "SUIUSDT", "BCHUSDT", "AVAXUSDT", "MATICUSDT", "RNDRUSDT",
                "DOGEUSDT", "HBARUSDT", "SEIUSDT", "STORJUSDT", "EOSUSDT", "FETUSDT", "SHIBUSDT", "THETAUSDT", "PEPEUSDT",
                "ARUSDT", "LINKUSDT", "ATOMUSDT", "TRBUSDT", "SUSHIUSDT", "BNBUSDT", "ORDIUSDT"
            };

            var intervals = new string[] { "1m" }; // Default interval

            var wallet = new Wallet(10000);

            // Pass fileName to OrderManager
            var orderManager = new OrderManager(wallet, leverage, new ExcelWriter(fileName: fileName), operationMode, 
                                                intervals[0], fileName, takeProfit, tradeDirection, selectedStrategy);

             var runner = new StrategyRunner(client, apiKey, symbols, intervals[0], wallet, orderManager, selectedStrategy);

            var smaExpansionStrategy = new SMAExpansionStrategy(client, apiKey, orderManager, wallet);
            var macdDivergenceStrategy = new MACDDivergenceStrategy(client, apiKey, orderManager, wallet);

            if (operationMode == OperationMode.Backtest)
            {
                List<Kline> historicalData = new List<Kline>();
                var currentPrices = new Dictionary<string, decimal>();

                while (leverage <= 25)
                {
                    foreach (var interval in new[] { "1m", "5m", "15m", "1h" }) // Example intervals for backtesting
                    {
                        orderManager._interval = interval;
                        runner._interval = interval;

                        foreach (var symbol in symbols)
                        {
                            historicalData = await FetchHistoricalData(client, symbol, interval);
                            foreach (var kline in historicalData)
                            {
                                kline.Symbol = symbol;
                            }

                            currentPrices = await FetchCurrentPrices(client, symbols);
                            Console.WriteLine($" -- Coin: " + symbol + " TF: " + interval + " Lev: " + leverage + " -- ");
                            await runner.RunStrategiesOnHistoricalDataAsync(historicalData); 
                        }
                    }
                    if(leverage == 1)
                    {
                        leverage += 4;
                    }
                    else
                    {
                        leverage += 5;
                    }                    
                    orderManager._leverage = leverage;
                }

                orderManager.PrintWalletBalance();
                orderManager.PrintActiveTrades(currentPrices);
            }
            else
            {
                var startTime = DateTime.Now;

                while (true)
                {
                    var currentPrices = await FetchCurrentPrices(client, symbols);
                    await runner.RunStrategiesAsync();

                    Console.WriteLine("---- Cycle Completed ----");
                    if (currentPrices != null)
                    {
                        orderManager.CheckAndCloseTrades(currentPrices);
                        orderManager.PrintActiveTrades(currentPrices);
                        orderManager.PrintWalletBalance();
                    }

                    var elapsedTime = DateTime.Now - startTime;
                    Console.WriteLine($"Elapsed Time: {elapsedTime.Days} days, {elapsedTime.Hours} hours, {elapsedTime.Minutes} minutes");

                    var delay = TimeTools.GetTimeSpanFromInterval(intervals[0]); // Using the first interval for delay

                    await Task.Delay(delay);
                }
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

        static async Task<List<Kline>> FetchHistoricalData(RestClient client, string symbol, string interval)
        {
            var historicalData = new List<Kline>();
            var request = new RestRequest("/api/v3/klines", Method.Get);
            request.AddParameter("symbol", symbol);
            request.AddParameter("interval", interval);
            request.AddParameter("limit", 1000);

            var response = await client.ExecuteAsync<List<List<object>>>(request);

            if (response.IsSuccessful)
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
