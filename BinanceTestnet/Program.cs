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
using System.Security.Cryptography;
using System.Text;
using BinanceTestnet.Database;
using System.Diagnostics;
using BinanceTestnet.Services;
using System.Linq.Expressions;

namespace BinanceLive
{
    class Program
    {
        private static TradeLogger _tradeLogger;
        private static string _sessionId;
        private static OrderManager _orderManager;
        private static RestClient _client; // Lift client to class level
        private static Wallet _wallet; // Lift wallet to class level

        static async Task Main(string[] args)
        {
            Console.WriteLine("Welcome to the Trade Input Program!");
            Console.WriteLine("Press 'Q' to quit gracefully (or type 'exit' and press Enter).");

            // Generate a unique SessionId        
            _sessionId = GenerateSessionId();
            Console.WriteLine($"SessionId: {_sessionId}");

            // Choose Mode
            var operationMode = GetOperationMode();

            DateTime startDate = DateTime.UtcNow.AddDays(-7); // Default: Last 7 days
            DateTime endDate = DateTime.UtcNow; // Default: Now

            string backtestSessionName = string.Empty;

            // If mode is backtest, prompt for start and end datetime
            if (operationMode == OperationMode.Backtest)
            {
                Console.Write("Enter a name for the backtest session: ");
                backtestSessionName = Console.ReadLine();

                Console.Write("Enter start datetime (yyyy-MM-dd HH:mm): ");
                string startDateInput = Console.ReadLine();
                if (DateTime.TryParseExact(startDateInput, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsedStartDate))
                {
                    startDate = parsedStartDate;
                }
                else
                {
                    Console.WriteLine("Invalid datetime format. Using default start datetime (last 7 days).");
                }

                Console.Write("Enter end datetime (yyyy-MM-dd HH:mm) or press Enter to use current time: ");
                string endDateInput = Console.ReadLine();
                if (!string.IsNullOrEmpty(endDateInput)
                    && DateTime.TryParseExact(endDateInput, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsedEndDate))
                {
                    endDate = parsedEndDate;
                }
                else
                {
                    Console.WriteLine("Using current time as end datetime.");
                }

                // Validate the datetime range
                if (startDate >= endDate)
                {
                    Console.WriteLine("Error: Start date must be before end date.");
                    return;
                }
            }

            // Get User Inputs
            var tradeDirection = GetTradeDirection();
            var selectedStrategies = GetTradingStrategy();
            var interval = GetInterval(operationMode);
            var (entrySize, leverage) = GetEntrySizeAndLeverage(operationMode);
            var takeProfit = GetTakeProfit(operationMode);
            var fileName = GenerateFileName(operationMode, entrySize, leverage, tradeDirection, selectedStrategies, takeProfit, backtestSessionName);

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
            _client = new RestClient("https://fapi.binance.com");
            var intervals = new[] { interval }; // Default interval
            var wallet = new Wallet(300); // Initial wallet size

            Stopwatch timer = new Stopwatch();
            timer.Start();

            // Define your database path
            string databasePath = @"C:\Repo\BinanceAPI\db\DataBase.db";
            var databaseManager = new DatabaseManager(databasePath);
            databaseManager.InitializeDatabase();

            _tradeLogger = new TradeLogger(databasePath);

            var symbols = new List<string>();

            if (operationMode == OperationMode.Backtest)
            {                
                symbols = await GetBestListOfSymbols(_client, databaseManager, startDate);
                // Use the hardcoded list of symbols
                //symbols = new List<string> { "BNBUSDT", "SUIUSDT", "AUCTIONUSDT", "BANANAUSDT", "TRUMPUSDT", "LINKUSDT", "LTCUSDT", "ENAUSDT", "AAVEUSDT", "AVAXUSDT", "WIFUSDT", "ARKMUSDT", "BNXUSDT", "TAOUSDT", "HBARUSDT" };
            
            }
            else
            {
                // Get the list of symbols dynamically
                symbols = await GetBestListOfSymbols(_client, databaseManager);
                //symbols = new List<string> { "BNBUSDT", "SUIUSDT", "AUCTIONUSDT", "BANANAUSDT", "TRUMPUSDT", "LINKUSDT", "LTCUSDT", "ENAUSDT", "AAVEUSDT", "AVAXUSDT", "WIFUSDT", "ARKMUSDT", "BNXUSDT", "TAOUSDT", "HBARUSDT" };
            
            }

            // Now you can use the `symbols` list as needed
            Console.WriteLine($"New list of coin pairs: {string.Join(", ", symbols)}, took {timer.Elapsed} to load and update in db");
            timer.Stop();

            // Initialize OrderManager and StrategyRunner
            _orderManager = new OrderManager(wallet, leverage, new ExcelWriter(fileName: fileName), operationMode, intervals[0], fileName, takeProfit, tradeDirection, selectedStrategies, _client, takeProfit, entrySize, databasePath, _sessionId);
            var runner = new StrategyRunner(_client, apiKey, symbols, intervals[0], wallet, _orderManager, selectedStrategies);

            // Run the appropriate mode
            if (operationMode == OperationMode.Backtest)
            {
                await RunBacktest(_client, symbols, intervals[0], wallet, fileName, selectedStrategies, _orderManager, runner, startDate, endDate);
            }
            else if (operationMode == OperationMode.LiveRealTrading)
            {
                await RunLiveTrading(_client, symbols, intervals[0], wallet, fileName, selectedStrategies, takeProfit, _orderManager, runner);
            }
            else // LivePaperTrading
            {
                await RunLivePaperTrading(_client, symbols, intervals[0], wallet, fileName, selectedStrategies, takeProfit, _orderManager, runner);
            }

            GenerateReport();
            Console.WriteLine("Program terminated gracefully.");
        }

        private static void OnTermination(object sender, ConsoleCancelEventArgs e)
        {
            if (e != null)
            {
                e.Cancel = true; // Prevent the program from terminating immediately
            }

            Console.WriteLine("\nTermination detected. Closing all open trades...");

            // Check if _client is initialized
            if (_client == null)
            {
                Console.WriteLine("Error: RestClient is not initialized.");
                return;
            }

            // Check if _orderManager is initialized
            if (_orderManager == null)
            {
                Console.WriteLine("Error: OrderManager is not initialized.");
                return;
            }

            // Fetch all active trades
            var openTrades = _orderManager.GetActiveTrades();
            Console.WriteLine($"Number of open trades: {openTrades.Count}");

            var symbols = openTrades.Select(t => t.Symbol).Distinct().ToList();
            Console.WriteLine($"Symbols to fetch prices for: {string.Join(", ", symbols)}");

            // Fetch current prices for these symbols
            Dictionary<string, decimal> currentPrices = null;
            try
            {
                currentPrices = FetchCurrentPrices(_client, symbols, GetApiKeys().apiKey, GetApiKeys().apiSecret).Result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching current prices: {ex.Message}");
                return;
            }

            if (currentPrices == null || currentPrices.Count == 0)
            {
                Console.WriteLine("Error: Failed to fetch current prices or no prices returned.");
                return;
            }

            Console.WriteLine($"Fetched prices: {string.Join(", ", currentPrices.Select(kv => $"{kv.Key}: {kv.Value}"))}");

            // Close all open trades using the fetched prices
            _orderManager.CloseAllActiveTrades(currentPrices, DateTime.UtcNow.Ticks);

            Console.WriteLine("All open trades closed.");
            GenerateReport();
            Environment.Exit(0);
        }

        private static void GenerateReport()
        {
            var metrics = _tradeLogger.CalculatePerformanceMetrics(_sessionId);
            var reportGenerator = new ReportGenerator(_tradeLogger);

            string reportsFolder = @"C:\Repo\BinanceAPI\BinanceTestnet\Excels";
            if (!Directory.Exists(reportsFolder))
            {
                Directory.CreateDirectory(reportsFolder);
            }

            string reportPath = Path.Combine(reportsFolder, $"performance_report_{_sessionId}.txt");

            // Retrieve the active coin pair list for the session
            var activeCoinPairs = _orderManager.DatabaseManager.GetClosestCoinPairList(DateTime.UtcNow);
            string coinPairsFormatted = string.Join(",", activeCoinPairs.Select(cp => $"\"{cp}\""));

            // Add the coin pair list to the report
            reportGenerator.GenerateSummaryReport(_sessionId, reportPath, coinPairsFormatted);

            Console.WriteLine($"Report generated successfully at: {reportPath}");
        }

        private static string GenerateFileName(OperationMode operationMode, decimal entrySize, decimal leverage, SelectedTradeDirection tradeDirection, SelectedTradingStrategy selectedStrategy, decimal takeProfit, string backtestSessionName)
        {
            string title = $"{(operationMode == OperationMode.Backtest ? "Backtest" : "PaperTrade")}_Direction{tradeDirection}_Strategy{selectedStrategy}_";
            if (operationMode == OperationMode.LivePaperTrading)
            {
                title += $"_TakeProfitPercent{takeProfit}_";
            }
            if (!string.IsNullOrEmpty(backtestSessionName))
            {
                title += $"_Session{backtestSessionName}_";
            }
            title += $"{DateTime.Now:yyyyMMdd-HH-mm}";
            return title.Replace(" ", "_").Replace("%", "Percent").Replace(".", "p") + ".xlsx";
        }

        private static string GetInterval(OperationMode operationMode)
        {
            if (operationMode == OperationMode.LivePaperTrading || operationMode == OperationMode.LiveRealTrading)
            {
                Console.Write("Enter Interval 1m, 5m, 15m, 30m, 1h, 4h (default 5m): ");
                string intervalInput = Console.ReadLine();
                return string.IsNullOrEmpty(intervalInput) ? "5m" : intervalInput;
            }
            return "5m"; // Default for backtesting
        }

        // Methods to Get User Inputs
        private static OperationMode GetOperationMode()
        {
            Console.WriteLine("Choose Mode:");
            Console.WriteLine("1. Paper Trading");
            Console.WriteLine("2. Back testing");
            Console.WriteLine("3. Live Real Trading");
            Console.Write("Enter choice (1/2/3): ");
            string? modeInput = Console.ReadLine();
            
            return modeInput switch
            {
                "2" => OperationMode.Backtest,
                "3" => OperationMode.LiveRealTrading,
                _ => OperationMode.LivePaperTrading,
            };
        }


        private static (decimal entrySize, decimal leverage) GetEntrySizeAndLeverage(OperationMode operationMode)
        {
             decimal leverage = 15; // Default leverage
             decimal entrySize = 20; // Default entry size
            if (operationMode == OperationMode.LivePaperTrading || operationMode == OperationMode.LiveRealTrading)
            {                
                Console.Write("Enter Entry Size (default 20 USDT): ");
                string entrySizeInput = Console.ReadLine();
                entrySize = decimal.TryParse(entrySizeInput, out var parsedEntrySize) ? parsedEntrySize : 10;
   
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
            if (operationMode == OperationMode.LivePaperTrading || operationMode == OperationMode.LiveRealTrading)
            {
                Console.Write("Enter Take Profit multiplier (default 5): ");
                
                string tpInput = Console.ReadLine();
                return decimal.TryParse(tpInput, out var parsedTP) ? parsedTP : (decimal)5.0M;
            }
            return 1.5M; // Default for backtesting
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
                "FTMUSDT", "XRPUSDT", "BTCUSDT", "ETHUSDT", "TIAUSDT", "SUIUSDT", "BCHUSDT", "AVAXUSDT", "YGGUSDT", 
                "DOGEUSDT", "HBARUSDT", "SEIUSDT", "STORJUSDT", "ADAUSDT", "DOTUSDT", "FETUSDT", "THETAUSDT", "ONTUSDT", "QNTUSDT",
                "ARUSDT", "LINKUSDT", "ATOMUSDT", "TRBUSDT", "SUSHIUSDT", "BNBUSDT", "ORDIUSDT", "SANDUSDT", "INJUSDT", "AXSUSDT", 
                "ENSUSDT", "LTCUSDT", "XLMUSDT"
            };
        }

        private static async Task<List<string>> GetBestListOfSymbols(RestClient client, DatabaseManager dbManager, DateTime? targetDateTime = null)
        {
            // If targetDateTime is provided (backtesting), try to retrieve the closest coin pair list
            if (targetDateTime.HasValue)
            {
                var closestCoinPairList = dbManager.GetClosestCoinPairList(targetDateTime.Value);
                if (closestCoinPairList.Any())
                {
                    return closestCoinPairList; // Use the retrieved list
                }
                else
                {
                    // If no list is found, fall back to the top 80 biggest coins by volume
                    var topBiggestCoins = dbManager.GetTopCoinPairsByVolume(80);
                    if (topBiggestCoins.Any())
                    {
                        return topBiggestCoins; // Use the top 80 biggest coins
                    }
                }
            }

            // For live trading or if the database is empty, fetch symbols from Binance Futures API
            var symbols = await FetchCoinPairsFromFuturesAPI(client);

            // Upsert symbols into the database (with volume, price, etc.)
            foreach (var symbolInfo in symbols)
            {
                dbManager.UpsertCoinPairData(symbolInfo.Symbol, symbolInfo.Price, symbolInfo.Volume);
            }

            // Query the database to get the top 80 symbols by price change
            var topSymbolsByPriceChange = dbManager.GetTopCoinPairs(80);

            // Store the coin pair list
            dbManager.UpsertCoinPairList(topSymbolsByPriceChange, DateTime.UtcNow);

            return topSymbolsByPriceChange;
        }
        private static async Task<List<CoinPairInfo>> FetchCoinPairsFromFuturesAPI(RestClient client)
        {
            var coinPairList = new List<CoinPairInfo>();

            // Prepare request to fetch all coin pairs
            var request = new RestRequest("/fapi/v1/exchangeInfo", Method.Get);

            // Send the request
            var response = await client.ExecuteAsync(request);

            if (response.IsSuccessful && response.Content != null)
            {
                var exchangeInfo = JsonConvert.DeserializeObject<ExchangeInfoResponse>(response.Content);
                
                // Filter the symbols based on active trading pairs
                foreach (var symbol in exchangeInfo.Symbols)
                {
                    if (symbol.Status == "TRADING")
                    {
                        // Optionally fetch more data such as volume and price (e.g., via another API call)
                        var volume = await FetchSymbolVolume(client, symbol.Symbol);
                        var price = await FetchSymbolPrice(client, symbol.Symbol);

                        coinPairList.Add(new CoinPairInfo
                        {
                            Symbol = symbol.Symbol,
                            Volume = volume,
                            Price = price
                        });
                    }
                }
            }
            else
            {
                Console.WriteLine($"Failed to fetch coin pairs: {response.ErrorMessage}");
            }

            return coinPairList;
        }

        private static async Task<decimal> FetchSymbolVolume(RestClient client, string symbol)
        {
            // Example to fetch symbol's volume
            var request = new RestRequest("/fapi/v1/ticker/24hr", Method.Get);
            request.AddParameter("symbol", symbol);

            var response = await client.ExecuteAsync(request);
            if (response.IsSuccessful && response.Content != null)
            {
                var symbolData = JsonConvert.DeserializeObject<SymbolDataResponse>(response.Content);
                return symbolData.Volume;
            }

            return 0;
        }

        public class SymbolDataResponse
        {
            [JsonProperty("volume")]
            public decimal Volume { get; set; }
        }

        private static async Task<decimal> FetchSymbolPrice(RestClient client, string symbol)
        {
            // Example to fetch symbol's current price
            var request = new RestRequest("/fapi/v1/ticker/price", Method.Get);
            request.AddParameter("symbol", symbol);

            var response = await client.ExecuteAsync(request);
            if (response.IsSuccessful && response.Content != null)
            {
                var priceData = JsonConvert.DeserializeObject<PriceResponse>(response.Content);
                return priceData.Price;
            }

            return 0;
        }

        private static async Task RunBacktest(RestClient client, List<string> symbols, string interval, Wallet wallet, string fileName, SelectedTradingStrategy selectedStrategy, OrderManager orderManager, StrategyRunner runner, DateTime startDate, DateTime endDate)
        {
            var backtestTakeProfits = new List<decimal> { 3m }; // Take profit percentages
            var intervals = new[] { "5m" }; // Time intervals for backtesting
            var leverage = 15;

            foreach (var tp in backtestTakeProfits)
            {
                for (int i = 0; i < intervals.Length; i++)
                {
                    wallet = new Wallet(3000); // Reset wallet balance

                    orderManager.UpdateParams(wallet, tp); // Update OrderManager with new parameters
                    orderManager.UpdateSettings(leverage, intervals[i]); // Update OrderManager settings (leverage, etc.)

                    foreach (var symbol in symbols)
                    {
                        // Fetch historical data for the symbol
                        var historicalData = await FetchHistoricalData(client, symbol, intervals[i], startDate, endDate);

                        // Skip symbols with insufficient historical data
                        if (!historicalData.Any())
                        {
                            Console.WriteLine($"Skipping {symbol}: Insufficient historical data for the specified period.");
                            continue;
                        }

                        // Assign the symbol to each kline
                        foreach (var kline in historicalData)
                        {
                            kline.Symbol = symbol;
                        }

                        Console.WriteLine($" -- Coin: {symbol} TF: {intervals[i]} Lev: 15 TP: {tp} -- ");

                        // Run the strategy on the historical data
                        await runner.RunStrategiesOnHistoricalDataAsync(historicalData);

                        // After backtest, get final price and volume for the symbol
                        if (historicalData.Any())
                        {
                            var finalKline = historicalData.Last();
                            decimal finalPrice = finalKline.Close;
                            decimal volume = historicalData.Sum(k => k.Volume);

                            // Insert or update the coin pair data in the database
                            orderManager.DatabaseManager.UpsertCoinPairData(symbol, finalPrice, volume);
                        }
                    }

                    orderManager.PrintWalletBalance();
                }
            }
        }

        private static async Task RunLivePaperTrading(RestClient client, List<string> symbols, string interval, Wallet wallet, string fileName, SelectedTradingStrategy selectedStrategy, decimal takeProfit, OrderManager orderManager, StrategyRunner runner)
        {
            var startTime = DateTime.Now;
            int cycles = 0;

            while (true)
            {
                try
                {
                    // Check for termination command
                    if (Console.KeyAvailable)
                    {
                        var key = Console.ReadKey(intercept: true).Key;
                        if (key == ConsoleKey.Q)
                        {
                            Console.WriteLine("Q key pressed. Terminating gracefully...");
                            OnTermination(null, null); // Manually trigger termination
                            break; // Exit the loop
                        }
                    }

                    // Fetch current prices and run strategies
                    var currentPrices = await FetchCurrentPrices(client, symbols, GetApiKeys().apiKey, GetApiKeys().apiSecret);
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
                }
                catch (Exception ex)
                {
                    // Log the exception without stopping the loop
                    Console.WriteLine($"An error occurred: {ex.Message}");
                    Console.WriteLine($"Stack Trace: {ex.StackTrace}");
                }

                cycles++;
                if (cycles == 20) // Update the coin pair list every 20 cycles
                {
                    cycles = 0;
                    Stopwatch timer = new Stopwatch();
                    timer.Start();

                    // Update the coin pair list and store it in the new table
                    symbols = await GetBestListOfSymbols(client, orderManager.DatabaseManager);

                    Console.WriteLine($"New list of coin pairs: {string.Join(", ", symbols)}, took {timer.Elapsed} to load and update in db");
                    timer.Stop();
                }

                // Simulate delay based on the interval
                var delay = TimeTools.GetTimeSpanFromInterval(interval); // e.g., "1m"
                await Task.Delay(delay);
            }

            Console.WriteLine("Live paper trading terminated gracefully.");
        }
        
        private static async Task RunLiveTrading(RestClient client, List<string> symbols, string interval, Wallet wallet, string fileName, SelectedTradingStrategy selectedStrategy, decimal takeProfit, OrderManager orderManager, StrategyRunner runner)
        {
            var startTime = DateTime.Now;
            int cycles = 0;

            BinanceActivities onBinance = new BinanceActivities(client);
            while (true)
            {
                try
                {                    
                    bool handledOrders = await onBinance.HandleOpenOrdersAndActiveTrades(symbols);
                    if (!handledOrders)
                    {
                        Console.WriteLine("Failed to handle open orders and trades.");
                        // Consider handling the error or retrying
                    }

                    //await onBinance.UpdateStopLossAndTakeProfit(trailingStopPercentage: 4m, dynamicTakeProfitMultiplier: 1.1m);

                    // Fetch current prices for symbols
                    var currentPrices = await FetchCurrentPrices(client, symbols, GetApiKeys().apiKey, GetApiKeys().apiSecret);

                    // Execute trading strategies and get buy/sell signals
                    await runner.RunStrategiesAsync();

                    // Calculate elapsed time and log it
                    var elapsedTime = DateTime.Now - startTime;
                    Console.WriteLine($"Elapsed Time: {elapsedTime.Days} days, {elapsedTime.Hours} hours, {elapsedTime.Minutes} minutes");
                }
                catch (Exception ex)
                {
                    // Log the exception without stopping the loop
                    Console.WriteLine($"An error occurred: {ex.Message}");
                    Console.WriteLine($"Stack Trace: {ex.StackTrace}");
                }

                cycles++;
                 if(cycles == 20)
                 {
                    cycles = 0;
                    Stopwatch timer = new Stopwatch();
                    timer.Start();
                    symbols = await GetBestListOfSymbols(client, orderManager.DatabaseManager);
                    Console.WriteLine($"New list of coin pairs: {string.Join(", ", symbols)}, took {timer.Elapsed} to load and update in db");
                    timer.Stop();
                 }

                // Determine delay based on the selected interval
                var delay = TimeTools.GetTimeSpanFromInterval(interval); // e.g., "1m"
                await Task.Delay(delay);
            }
        }


        // Method to generate a signature
        private static string GenerateSignature(string queryString, string apiSecret)
        {
            using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(apiSecret)))
            {
                var hashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(queryString));
                return BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
            }
        }

        static async Task<Dictionary<string, decimal>> FetchCurrentPrices(RestClient client, List<string> symbols, string apiKey, string apiSecret)
        {
            var prices = new Dictionary<string, decimal>();

            Console.WriteLine($"Fetching prices for symbols: {string.Join(", ", symbols)}");

            foreach (var symbol in symbols)
            {
                var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                var request = new RestRequest("/fapi/v1/ticker/price", Method.Get);
                request.AddParameter("symbol", symbol);
                request.AddHeader("X-MBX-APIKEY", apiKey);

                // Create a query string using the generated timestamp
                string queryString = $"symbol={symbol}&timestamp={timestamp}";

                // Generate the signature based on the query string and secret
                string signature = GenerateSignature(queryString, apiSecret);

                // Add the timestamp and signature to the request
                request.AddParameter("timestamp", timestamp.ToString());
                request.AddParameter("signature", signature);

                // Log the request URL and parameters
                var requestUrl = client.BuildUri(request).ToString();
                //Console.WriteLine($"Request URL for {symbol}: {requestUrl}");

                try
                {
                    // Send the request
                    var response = await client.ExecuteAsync(request);

                    // Log the response details
                    //Console.WriteLine($"Response for {symbol}: {response.StatusCode} - {response.Content}");

                    if (response.IsSuccessful && response.Content != null)
                    {
                        var priceData = JsonConvert.DeserializeObject<PriceResponse>(response.Content);
                        if (priceData != null)
                        {
                            prices[symbol] = priceData.Price;
                            //Console.WriteLine($"Fetched price for {symbol}: {priceData.Price}");
                        }
                        else
                        {
                            Console.WriteLine($"Failed to deserialize price data for {symbol}.");
                        }
                    }
                    else
                    {
                        // Log the response details for debugging
                        Console.WriteLine($"Error fetching price for {symbol}: {response.StatusCode} - {response.Content}");
                    }
                }
                catch (Exception ex)
                {
                    // Log the exception details for debugging
                    Console.WriteLine($"Exception fetching price for {symbol}: {ex.Message}");
                }
            }

            return prices;
        }


        public async Task<long> GetServerTimeAsync(RestClient _client)
        {
            var request = new RestRequest("/fapi/v1/time", Method.Get);
            var response = await _client.ExecuteAsync(request);
            if (response.IsSuccessful)
            {
                var serverTimeData = JsonConvert.DeserializeObject<ServerTimeResponse>(response.Content);
                return serverTimeData.ServerTime;
            }
            else
            {
                throw new Exception("Failed to fetch server time.");
            }
        }

        // Helper Classes for Deserialization
        public class ExchangeInfoResponse
        {
            [JsonProperty("symbols")]
            public List<SymbolInfo> Symbols { get; set; }
        }      

        public class ServerTimeResponse
        {
            [JsonProperty("serverTime")]
            public long ServerTime { get; set; }
        }

        // Create a class for the expected response structure
        public class PriceResponse
        {
            public string Symbol { get; set; }
            public decimal Price { get; set; }
        }

        static async Task<List<Kline>> FetchHistoricalData(RestClient client, string symbol, string interval, DateTime startDate, DateTime endDate)
        {
            var historicalData = new List<Kline>();
            var request = new RestRequest("/fapi/v1/klines", Method.Get);
            request.AddParameter("symbol", symbol);
            request.AddParameter("interval", interval);
            request.AddParameter("startTime", new DateTimeOffset(startDate).ToUnixTimeMilliseconds());
            request.AddParameter("endTime", new DateTimeOffset(endDate).ToUnixTimeMilliseconds());

            var response = await client.ExecuteAsync<List<List<object>>>(request);

            if (response.IsSuccessful && response.Content != null)
            {
                var klineData = JsonConvert.DeserializeObject<List<List<object>>>(response.Content);

                // Check if the first candle's OpenTime is after the startDate
                if (klineData.Any())
                {
                    var firstCandleOpenTime = DateTimeOffset.FromUnixTimeMilliseconds((long)klineData[0][0]).UtcDateTime;
                    if (firstCandleOpenTime > startDate)
                    {
                        // Skip this symbol if the first candle is after the startDate
                        Console.WriteLine($"Skipping {symbol}: Insufficient historical data before {startDate}");
                        return historicalData; // Return an empty list
                    }
                }

                // Process the candles
                foreach (var kline in klineData)
                {
                    historicalData.Add(new Kline
                    {
                        OpenTime = (long)kline[0],
                        Open = decimal.Parse(kline[1].ToString(), CultureInfo.InvariantCulture),
                        High = decimal.Parse(kline[2].ToString(), CultureInfo.InvariantCulture),
                        Low = decimal.Parse(kline[3].ToString(), CultureInfo.InvariantCulture),
                        Close = decimal.Parse(kline[4].ToString(), CultureInfo.InvariantCulture),
                        Volume = decimal.Parse(kline[5].ToString(), CultureInfo.InvariantCulture),
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

        public static string GenerateSessionId()
        {
            return $"{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N").Substring(0, 6)}";
        }
        
        public class CoinPairInfo
        {
            public string Symbol { get; set; }
            public decimal Volume { get; set; }
            public decimal Price { get; set; }
        }        
    }
}
