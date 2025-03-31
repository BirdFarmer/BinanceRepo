using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using BinanceTestnet.Enums; 
using BinanceTestnet.Database;
using BinanceTestnet.Trading;
using BinanceLive.Strategies;
using BinanceTestnet.Models;
using BinanceLive.Tools;
using BinanceTestnet.Services;
using RestSharp;
using Newtonsoft.Json;
using RestSharp;
using System.Diagnostics;
using System.Linq.Expressions;
using System.IO;
using System.Windows;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace TradingAppDesktop.Services
{
    public class BinanceTradingService
    {
        private static TradeLogger _tradeLogger;
        private static string _sessionId;
        private static OrderManager _orderManager;
        private static RestClient _client; // Lift client to class level
        private static Wallet _wallet;
        private CancellationTokenSource _cancellationTokenSource;
        private bool _isStopping = false;
        public bool IsRunning => _cancellationTokenSource != null && !_cancellationTokenSource.IsCancellationRequested;
        private readonly ILogger<BinanceTradingService> _logger;
        
        public BinanceTradingService(ILogger<BinanceTradingService> logger)
        {
            _logger = logger;
            _client = new RestClient("https://fapi.binance.com");
            _wallet = new Wallet(300);
            _cancellationTokenSource = new CancellationTokenSource();
        }

        public async Task StartTrading(OperationMode operationMode, SelectedTradeDirection tradeDirection, 
                                    SelectedTradingStrategy selectedStrategy, string interval, 
                                    decimal entrySize, decimal leverage, decimal takeProfit,
                                    DateTime? startDate = null, DateTime? endDate = null)
        {
            _logger.LogInformation("Starting trading session...");
            _logger.LogDebug($"Mode: {operationMode}, Strategy: {selectedStrategy}, Direction: {tradeDirection}");
            _logger.LogDebug($"Params: Interval={interval}, Entry={entrySize}USDT, Leverage={leverage}x, TP={takeProfit}%");

            _sessionId = GenerateSessionId();
            _logger.LogInformation($"New trading session ID: {_sessionId}");

            // Fetch API Keys
            var (apiKey, apiSecret) = GetApiKeys();
            if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(apiSecret))
            {
                _logger.LogError("API key or secret is not set. Please set BINANCE_API_KEY and BINANCE_API_SECRET environment variables.");
                return;
            }
            else
            {
                _logger.LogDebug("API keys retrieved successfully");
            }

            // Initialize Database
            string databasePath = @"C:\Repo\BinanceAPI\db\DataBase.db";
            _logger.LogDebug($"Initializing database at: {databasePath}");
            
            var databaseManager = new DatabaseManager(databasePath);
            try
            {
                databaseManager.InitializeDatabase();
                _tradeLogger = new TradeLogger(databasePath);
                _logger.LogInformation("Database initialized successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize database");
                throw;
            }

            // Get Symbols
            _logger.LogInformation("Fetching symbols to trade...");
            var symbols = await GetBestListOfSymbols(_client, databaseManager, DateTime.Today.AddDays(-1));
            _logger.LogInformation($"Selected {symbols.Count} symbols: {string.Join(", ", symbols.Take(5))}...");

            // Initialize OrderManager
            _logger.LogDebug("Initializing OrderManager...");
            _orderManager = CreateOrderManager(_wallet, leverage, operationMode, interval, 
                                        takeProfit, tradeDirection, selectedStrategy, 
                                        _client, takeProfit, entrySize, databasePath, _sessionId);

            try
            {
                // Initialize StrategyRunner
                _logger.LogDebug("Initializing StrategyRunner...");
                var runner = new StrategyRunner(_client, apiKey, symbols, interval, _wallet, _orderManager, selectedStrategy);

                // Add timeout to the trading operation
                var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(1)); // 1 minute timeout
                var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cancellationTokenSource.Token, timeoutCts.Token);

                _logger.LogInformation($"Starting {operationMode} trading...");
                
                switch (operationMode)
                {
                    case OperationMode.Backtest:
                        await RunBacktest(_client, symbols, interval, _wallet, "", 
                                        selectedStrategy, _orderManager, runner, 
                                        new DateTime(2025, 1, 5), new DateTime(2025, 1, 6), //startDate.Value, endDate.Value, 
                                        _cancellationTokenSource.Token);
                        break;
                    case OperationMode.LiveRealTrading:
                        await RunLiveTrading(_client, symbols, interval, _wallet, "", 
                                        selectedStrategy, takeProfit, _orderManager, 
                                        runner, _cancellationTokenSource.Token);
                        break;
                    default: // LivePaperTrading
                        await RunLivePaperTrading(_client, symbols, interval, _wallet, "", 
                                                selectedStrategy, takeProfit, _orderManager, 
                                                runner, _cancellationTokenSource.Token);
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Trading session failed");
                throw;
            }

            _logger.LogInformation("Trading session completed");
            GenerateReport();
        }


        private async Task<bool> CheckApiHealth()
        {
            _logger.LogInformation("[1] Starting health check...");
            try
            {
                _logger.LogInformation("[2] Creating request...");
                var pingRequest = new RestRequest("/fapi/v1/ping", Method.Get);

                _logger.LogInformation("[3] Executing request...");
                var response = await _client.ExecuteAsync(pingRequest);

                _logger.LogInformation($"[4] Response received: {response.StatusCode}");
                return response.IsSuccessful;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[5] Health check failed: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> TestConnection()
        {
            var healthChecker = new HealthChecker(_client, _logger);
            var result = await healthChecker.CheckAllEndpointsAsync();

            if (result.FullyOperational)
            {
                _logger.LogInformation("All endpoints healthy!");
                return true;
            }
            else
            {
                _logger.LogWarning($"Degraded: Ping={result.PingHealthy}, ExchangeInfo={result.ExchangeInfoHealthy}");
                return false;
            }            
        }

        // When creating OrderManager:
        private OrderManager CreateOrderManager(Wallet wallet, decimal leverage, OperationMode operationMode,
                                                string interval, decimal takeProfit,
                                                SelectedTradeDirection tradeDirection, SelectedTradingStrategy selectedStrategy,
                                                RestClient client, decimal tpIteration, decimal entrySize, string databasePath, string sessionId)
        {
            return new OrderManager(_wallet, leverage, operationMode, interval,
                                    takeProfit, tradeDirection, selectedStrategy,
                                    _client, takeProfit, entrySize, databasePath, _sessionId
            );
        }

        public void StopTrading(bool closeAllTrades)
        {
            if (_isStopping)
            {
                _logger.LogWarning("Stop operation already in progress");
                return;
            }

            _logger.LogInformation($"Initiating stop trading (closeAllTrades: {closeAllTrades})");
            _isStopping = true;

            try
            {
                _logger.LogDebug("Cancelling running operations...");
                _cancellationTokenSource?.Cancel();
                
                if (closeAllTrades)
                {
                    _logger.LogInformation("Closing all open trades...");
                    Task.Run(() => CloseAllTrades()).ContinueWith(t => 
                    {
                        if (t.IsFaulted)
                        {
                            _logger.LogError(t.Exception, "Error while closing trades");
                        }
                        else
                        {
                            _logger.LogInformation("All trades closed successfully");
                        }
                    });
                }
            }
            finally
            {
                _isStopping = false;
                _logger.LogInformation("Trading stopped successfully");
            }
        }

        private void OnTermination(object sender, ConsoleCancelEventArgs e)
        {
            if (e != null)
            {
                e.Cancel = true;
            }

            _logger.LogInformation("\nTermination detected. Closing all open trades...");

            if (_client == null)
            {
                _logger.LogError("Error: RestClient is not initialized.");
                return;
            }

            if (_orderManager == null)
            {
                _logger.LogError("Error: OrderManager is not initialized.");
                return;
            }

            var openTrades = _orderManager.GetActiveTrades();
            _logger.LogInformation($"Number of open trades: {openTrades.Count}");

            var symbols = openTrades.Select(t => t.Symbol).Distinct().ToList();
            _logger.LogInformation($"Symbols to fetch prices for: {string.Join(", ", symbols)}");

            Dictionary<string, decimal> currentPrices = null;
            try
            {
                currentPrices = FetchCurrentPrices(_client, symbols, GetApiKeys().apiKey, GetApiKeys().apiSecret).Result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching current prices");
                return;
            }

            if (currentPrices == null || currentPrices.Count == 0)
            {
                _logger.LogError("Error: Failed to fetch current prices or no prices returned.");
                return;
            }

            _logger.LogInformation($"Fetched prices: {string.Join(", ", currentPrices.Select(kv => $"{kv.Key}: {kv.Value}"))}");

            _orderManager.CloseAllActiveTrades(currentPrices, DateTime.UtcNow.Ticks);

            _logger.LogInformation("All open trades closed.");
            GenerateReport();
            Environment.Exit(0);
        }

        private void CloseAllTrades()
        {
            _logger.LogInformation("Starting to close all trades...");
            
            if (_client == null || _orderManager == null)
            {
                _logger.LogWarning("Cannot close trades - client or order manager not initialized");
                return;
            }

            var openTrades = _orderManager.GetActiveTrades();
            if (!openTrades.Any())
            {
                _logger.LogInformation("No open trades to close");
                return;
            }

            _logger.LogInformation($"Closing {openTrades.Count} open trades...");
            
            var symbols = openTrades.Select(t => t.Symbol).Distinct().ToList();
            _logger.LogDebug($"Fetching prices for {symbols.Count} symbols");

            try
            {
                var currentPrices = FetchCurrentPrices(_client, symbols, 
                    GetApiKeys().apiKey, GetApiKeys().apiSecret).Result;
                    
                if (currentPrices?.Any() == true)
                {
                    _logger.LogDebug("Closing trades with current prices...");
                    _orderManager.CloseAllActiveTrades(currentPrices, DateTime.UtcNow.Ticks);
                    _logger.LogInformation("All trades closed successfully");
                }
                else
                {
                    _logger.LogError("Failed to fetch current prices for closing trades");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while closing trades");
            }
        }


        private void GenerateReport()
        {
            _logger.LogInformation("Generating performance report...");
            
            try
            {
                var metrics = _tradeLogger.CalculatePerformanceMetrics(_sessionId);
                var reportGenerator = new ReportGenerator(_tradeLogger);

                string reportsFolder = @"C:\Repo\BinanceAPI\BinanceTestnet\Excels";
                Directory.CreateDirectory(reportsFolder);

                string reportPath = Path.Combine(reportsFolder, $"performance_report_{_sessionId}.txt");
                
                var activeCoinPairs = _orderManager.DatabaseManager.GetClosestCoinPairList(DateTime.UtcNow);
                string coinPairsFormatted = string.Join(",", activeCoinPairs.Select(cp => $"\"{cp}\""));

                reportGenerator.GenerateSummaryReport(_sessionId, reportPath, coinPairsFormatted);
                
                _logger.LogInformation($"Report generated successfully at: {reportPath}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate report");
            }
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

        private static async Task RunBacktest(RestClient client, List<string> symbols, string interval, 
                                            Wallet wallet, string fileName, SelectedTradingStrategy selectedStrategy,
                                            OrderManager orderManager, StrategyRunner runner, DateTime startDate,
                                            DateTime endDate, CancellationToken cancellationToken)
        {
            var backtestTakeProfits = new List<decimal> { 3m };
            var intervals = new[] { "1m" };
            var leverage = 15;

            try
            {
                foreach (var tp in backtestTakeProfits)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    for (int i = 0; i < intervals.Length; i++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        
                        wallet = new Wallet(3000);
                        orderManager.UpdateParams(wallet, tp);
                        orderManager.UpdateSettings(leverage, intervals[i]);

                        foreach (var symbol in symbols)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            
                            var historicalData = await FetchHistoricalData(client, symbol, intervals[i], startDate, endDate)
                                .ConfigureAwait(false);

                            if (!historicalData.Any())
                            {
                                Console.WriteLine($"Skipping {symbol}: Insufficient historical data");
                                continue;
                            }

                            foreach (var kline in historicalData)
                            {
                                kline.Symbol = symbol;
                            }

                            Console.WriteLine($" -- Coin: {symbol} TF: {intervals[i]} Lev: 15 TP: {tp} -- ");

                            // Pass cancellation token to strategy runner
                            await runner.RunStrategiesOnHistoricalDataAsync(historicalData)
                                .ConfigureAwait(false);

                            if (historicalData.Any())
                            {
                                var finalKline = historicalData.Last();
                                orderManager.DatabaseManager.UpsertCoinPairData(
                                    symbol, 
                                    finalKline.Close, 
                                    historicalData.Sum(k => k.Volume)
                                );
                            }
                        }

                        orderManager.PrintWalletBalance();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("Backtest cancelled by user request");
                throw; // Re-throw if you want calling code to know it was cancelled
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Backtest failed: {ex.Message}");
                throw; // Preserve the original exception stack
            }
            finally
            {
                Console.WriteLine("Backtest resources cleaned up");
            }
        }

        private async Task RunLivePaperTrading(RestClient client, List<string> symbols, string interval, 
                                            Wallet wallet, string fileName, 
                                            SelectedTradingStrategy selectedStrategy, decimal takeProfit, 
                                            OrderManager orderManager, StrategyRunner runner,
                                            CancellationToken cancellationToken)
        {
            var startTime = DateTime.Now;
            int cycles = 0;
            _logger.LogInformation($"Starting live paper trading with {symbols.Count} symbols");

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    _logger.LogDebug($"Starting trading cycle {cycles + 1}");
                    
                    // if (Console.KeyAvailable)
                    // {
                    //     var key = Console.ReadKey(intercept: true).Key;
                    //     if (key == ConsoleKey.Q)
                    //     {
                    //         _logger.LogInformation("Q key pressed. Terminating gracefully...");
                    //         OnTermination(null, null);
                    //         break;
                    //     }
                    // }

                    _logger.LogDebug("Fetching current prices...");
                    var currentPrices = await FetchCurrentPrices(client, symbols, GetApiKeys().apiKey, GetApiKeys().apiSecret);
                    
                    _logger.LogDebug("Running strategies...");
                    await runner.RunStrategiesAsync();

                    _logger.LogInformation($"---- Cycle {cycles + 1} Completed ----");
                    
                    if (currentPrices != null)
                    {
                        _logger.LogDebug("Checking and closing trades...");
                        await orderManager.CheckAndCloseTrades(currentPrices);
                        
                        _logger.LogDebug("Logging active trades...");
                        orderManager.PrintActiveTrades(currentPrices);
                        
                        _logger.LogDebug("Logging wallet balance...");
                        orderManager.PrintWalletBalance();
                    }

                    var elapsedTime = DateTime.Now - startTime;
                    _logger.LogInformation($"Elapsed Time: {elapsedTime.Days}d {elapsedTime.Hours}h {elapsedTime.Minutes}m");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error in trading cycle {cycles + 1}");
                }

                cycles++;
                if (cycles % 20 == 0)
                {
                    _logger.LogInformation("Updating symbol list...");
                    Stopwatch timer = Stopwatch.StartNew();
                    
                    symbols = await GetBestListOfSymbols(client, orderManager.DatabaseManager);
                    
                    timer.Stop();
                    _logger.LogInformation($"Updated {symbols.Count} symbols. Took {timer.ElapsedMilliseconds}ms");
                }

                var delay = TimeTools.GetTimeSpanFromInterval(interval);
                _logger.LogDebug($"Waiting {delay.TotalSeconds} seconds until next cycle");
                await Task.Delay(delay);
            }

            _logger.LogInformation("Live paper trading terminated gracefully");
        }

        private static async Task RunLiveTrading(RestClient client, List<string> symbols, string interval, 
                                            Wallet wallet, string fileName, SelectedTradingStrategy selectedStrategy, 
                                            decimal takeProfit, OrderManager orderManager, StrategyRunner runner,
                                            CancellationToken cancellationToken)
        {
            var startTime = DateTime.Now;
            int cycles = 0;
            BinanceActivities onBinance = new BinanceActivities(client);

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // Critical: Pass cancellation token to delay
                    var delay = TimeTools.GetTimeSpanFromInterval(interval);
                    await Task.Delay(delay, cancellationToken);

                    bool handledOrders = await onBinance.HandleOpenOrdersAndActiveTrades(symbols);
                    if (!handledOrders) continue;

                    var currentPrices = await FetchCurrentPrices(client, symbols, GetApiKeys().apiKey, GetApiKeys().apiSecret);
                    await runner.RunStrategiesAsync();

                    if (++cycles % 20 == 0)
                    {
                        symbols = await GetBestListOfSymbols(client, orderManager.DatabaseManager);
                    }
                }
                catch (TaskCanceledException)
                {
                    break; // Expected during shutdown
                }
                catch (Exception)
                {
                    // Basic error handling without logging
                    if (cancellationToken.IsCancellationRequested)
                        break;
                }
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