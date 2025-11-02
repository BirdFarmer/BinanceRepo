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
using System.Diagnostics;
using System.Linq.Expressions;
using System.IO;
using System.Windows; // Ensure ReportSettings is part of this namespace or define it
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System.Net.Http;
using System.Threading;
using BinanceTestnet.MarketAnalysis;
using TradingAppDesktop.Views;
using TradingAppDesktop.Models;

namespace TradingAppDesktop.Services
{
    public class BinanceTradingService : IExchangeInfoProvider
    {
        private RecentTradesViewModel? _recentTradesVm;
        private PaperWalletViewModel? _paperWalletVm;
        private decimal _paperStartingBalance;
        
        // Trailing config supplied by UI (applied when starting a session)
        private bool _uiUseTrailing = false;
        private decimal _uiTrailingActivationPercent = 1.0m;
        private decimal _uiTrailingCallbackPercent = 1.0m;
        
        private static TradeLogger _tradeLogger;
        private readonly ReportSettings _reportSettings;
        private static string _sessionId;
        private static OrderManager _orderManager;
        private static RestClient _client; // Lift client to class level
        
        private readonly TimeSpan _defaultTimeout = TimeSpan.FromSeconds(10);
        private static Wallet _wallet;
    private CancellationTokenSource? _cancellationTokenSource;
        private volatile bool _isStopping = false;
        private volatile bool _startInProgress = false;
        
        public bool IsStopping => _isStopping;
        public bool StartInProgress => _startInProgress;
        private volatile bool _isRunning = false;

    public bool IsRunning => _cancellationTokenSource != null && 
                !_cancellationTokenSource.IsCancellationRequested;

        private readonly ILogger<BinanceTradingService> _logger;
        
    private MarketContextAnalyzer? _marketAnalyzer;
        private readonly ILoggerFactory _loggerFactory;
        private readonly object _startLock = new();
        
        public BinanceTradingService(ILogger<BinanceTradingService> logger, ILoggerFactory loggerFactory)
        {
            _logger = logger;
            _loggerFactory = loggerFactory;
            
            // Initialize report settings with defaults
            _reportSettings = new ReportSettings(); // Ensure the ReportSettings class is defined or imported

            // Load configuration
            var config = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json")
                .Build();
            

            _client = new RestClient(new HttpClient()
            {
                Timeout = _defaultTimeout,
                BaseAddress = new Uri("https://fapi.binance.com")
            });
            _wallet = new Wallet(1000);
            //_cancellationTokenSource = new CancellationTokenSource();
        }
            

        public async Task StartTrading(OperationMode operationMode, SelectedTradeDirection tradeDirection, 
                                    List<SelectedTradingStrategy> selectedStrategies, string interval, 
                                    decimal entrySize, decimal leverage, decimal takeProfit, decimal stopLoss, 
                                    DateTime? startDate = null, DateTime? endDate = null,
                                    List<string> customCoinSelection = null)
        {
            _logger.LogDebug($"State update - IsRunning: {_isRunning}, StartInProgress: {_startInProgress}, IsStopping: {_isStopping}");
            lock (_startLock)
            {
                if (_startInProgress || (_cancellationTokenSource != null && !_cancellationTokenSource.IsCancellationRequested))
                {
                    _logger.LogWarning("Start operation already in progress");
                    return;
                }
                _startInProgress = true;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = new CancellationTokenSource();                
                _isRunning = true;
            }

            _logger.LogInformation("Starting trading session...");
            _logger.LogDebug($"Mode: {operationMode}, Strategy: {selectedStrategies}, Direction: {tradeDirection}");
            if (_uiUseTrailing)
            {
                _logger.LogDebug($"Params: Interval={interval}, Entry={entrySize}USDT, Leverage={leverage}x, Exit=Trailing (Act={_uiTrailingActivationPercent:F1}%, Cb={_uiTrailingCallbackPercent:F1}%, RR=1:{stopLoss:F1})");
            }
            else
            {
                _logger.LogDebug($"Params: Interval={interval}, Entry={entrySize}USDT, Leverage={leverage}x, TP={takeProfit}%, SL={stopLoss}%");
            }

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

            // Initialize Database - Updated Version
            string databasePath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,  // .exe's folder
                "TradingData.db"                       // Single file, no subfolder
            );
            // 1. Ensure directory exists
            var dbDir = Path.GetDirectoryName(databasePath);
            if (!Directory.Exists(dbDir))
            {
                Directory.CreateDirectory(dbDir);
                _logger.LogDebug($"Created database directory: {dbDir}");
            }

            // 2. Initialize database (with migration for existing users)
            var databaseManager = new DatabaseManager(databasePath);
            try
            {
                // 3. Check if this is first run (no DB exists)
                bool isFirstRun = !File.Exists(databasePath);
                
                databaseManager.InitializeDatabase();
                _tradeLogger = new TradeLogger(databasePath);
                _marketAnalyzer = new MarketContextAnalyzer(_client, _logger);

                if (isFirstRun)
                {
                    _logger.LogInformation("Created new database with default schema");
                    // Optional: Add starter data here if needed
                }
                else
                {
                    _logger.LogInformation($"Loaded existing database (v{databaseManager.GetSchemaVersion()})");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Database initialization failed");
                throw;
            }

            // Get Symbols
            _logger.LogInformation("Fetching symbols to trade...");
            List<string> symbols;
            if (customCoinSelection != null && customCoinSelection.Any())
            {
                // Use the custom coin selection from the coin selection window
                symbols = customCoinSelection;
                _logger.LogInformation($"Using custom coin selection: {symbols.Count} symbols");

                // Save this selection to database for persistence
                databaseManager.UpsertCoinPairList(symbols, DateTime.UtcNow);
            }
            else
            {
                // Use the default auto-selection logic
                _logger.LogInformation("Fetching symbols to trade using auto-selection...");
                symbols = await GetBestListOfSymbols(_client, databaseManager);
                _logger.LogInformation($"Auto-selected {symbols.Count} symbols: {string.Join(", ", symbols.Take(5))}...");
            }    

            // Reset Paper Wallet balance and panel at the start of a Paper session
            if (operationMode == OperationMode.LivePaperTrading)
            {
                _wallet = new Wallet(1000); // fresh session baseline
                if (_paperWalletVm != null)
                {
                    _paperStartingBalance = _wallet.GetBalance();
                    _paperWalletVm.Reset(_paperStartingBalance, DateTime.UtcNow);
                }
            }

            // Initialize OrderManager
            _logger.LogDebug("Initializing OrderManager...");
            _orderManager = CreateOrderManager(_wallet, leverage, operationMode, interval, 
                                        takeProfit, stopLoss, tradeDirection, selectedStrategies.First(), 
                                        _client, takeProfit, entrySize, databasePath, _sessionId);

            // Apply trailing configuration from UI (for all modes; live mode uses exchange-side trailing now)
            if (_uiUseTrailing)
            {
                _orderManager.UpdateTrailingConfig(true, _uiTrailingActivationPercent, _uiTrailingCallbackPercent);
            }

            _reportSettings.StrategyName = selectedStrategies.First().ToString();
            _reportSettings.Leverage = (int)leverage;
            _reportSettings.TakeProfitMultiplier = takeProfit;
            _reportSettings.MarginPerTrade = entrySize;
            _reportSettings.StopLossRatio = stopLoss;
            _reportSettings.Interval = interval;                                        

            try
            {
                // Initialize StrategyRunner
                _logger.LogDebug("Initializing StrategyRunner...");
                var runner = new StrategyRunner(_client, apiKey, symbols, interval, _wallet, _orderManager, selectedStrategies);

                // Paper wallet already reset above when creating a fresh Wallet

                // Add timeout to the trading operation
                var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(1)); // 1 minute timeout
                var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cancellationTokenSource.Token, timeoutCts.Token);

                _logger.LogInformation($"Starting {operationMode} trading...");
                
                switch (operationMode)
                {
                    case OperationMode.Backtest:
                        await RunBacktest(_client, symbols, interval, _wallet, "", 
                                          _orderManager, runner, 
                                         startDate.Value, endDate.Value, //new DateTime(2025, 1, 5), new DateTime(2025, 1, 6), //
                                         _cancellationTokenSource.Token);
                        break;
                    case OperationMode.LiveRealTrading:
                        await RunLiveTrading(_client, symbols, interval, _wallet, "", 
                                        takeProfit, _orderManager, 
                                        runner, _cancellationTokenSource.Token, logger: _logger);
                        break;
                    default: // LivePaperTrading
                        await RunLivePaperTrading(_client, symbols, interval, _wallet, "", 
                                                takeProfit, _orderManager, 
                                                runner, _cancellationTokenSource.Token);
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Trading session failed");
                throw;
            }
            finally
            {
                lock (_startLock)
                {
                    _startInProgress = false;
                    _isRunning = false;
                }
                // Ensure session cleanup so UI can start again without requiring Stop
                try { _cancellationTokenSource?.Cancel(); } catch {}
                try { _cancellationTokenSource?.Dispose(); } catch {}
                _cancellationTokenSource = null;
            }

            _logger.LogInformation("Trading session completed");
            await GenerateReport();
        }

        public async Task<ExchangeInfo> GetExchangeInfoAsync()
        {
            var request = new RestRequest("/fapi/v1/exchangeInfo", Method.Get);
            
            try
            {
                // Double timeout protection
                using var cts = new CancellationTokenSource(_defaultTimeout);
                
                var response = await _client.ExecuteAsync(request, cts.Token);
                
                if (!response.IsSuccessful)
                {
                    Console.WriteLine($"API Error: {response.StatusCode} - {response.ErrorMessage}");
                    return null;
                }

                return JsonConvert.DeserializeObject<ExchangeInfo>(response.Content);
            }
            catch (TaskCanceledException)
            {
                Console.WriteLine("Request timed out (5s limit reached)");
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Unexpected error: {ex.Message}");
                return null;
            }
        }

        public void Dispose() => _client?.Dispose();

        public void SetPaperWalletViewModel(PaperWalletViewModel vm)
        {
            _paperWalletVm = vm;
        }

        // Set by MainWindow before StartTrading
        public void SetTrailingUiConfig(bool useTrailing, decimal activationPercent, decimal callbackPercent)
        {
            _uiUseTrailing = useTrailing;
            _uiTrailingActivationPercent = activationPercent;
            _uiTrailingCallbackPercent = callbackPercent;
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
            try
            {
                var healthChecker = new HealthChecker(_client, _logger);
                var result = await healthChecker.CheckAllEndpointsAsync();
                
                if (result.FullyOperational)
                {
                    _logger.LogInformation("All endpoints healthy - Exchange Info validated");
                    return true;
                }
                
                if (!result.PingHealthy && !result.ExchangeInfoHealthy)
                {
                    _logger.LogError("Both ping and exchange info checks failed - possible network issue");
                }
                else if (!result.ExchangeInfoHealthy)
                {
                    _logger.LogError("Exchange info endpoint failed - API may be degraded");
                }
                
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Health check crashed unexpectedly");
                return false;
            }
        }

        // When creating OrderManager:
        private OrderManager CreateOrderManager(Wallet wallet, decimal leverage, OperationMode operationMode,
                                                string interval, decimal takeProfit, decimal stopLoss,
                                                SelectedTradeDirection tradeDirection, SelectedTradingStrategy selectedStrategy,
                                                RestClient client, decimal tpIteration, decimal entrySize, string databasePath, string sessionId)
        {
            
            var orderManagerLogger = _loggerFactory.CreateLogger<OrderManager>();

            return new OrderManager(_wallet, leverage, operationMode, interval,
                                    takeProfit, stopLoss, tradeDirection, selectedStrategy,
                                    _client, takeProfit, entrySize, databasePath, 
                                    _sessionId, this, orderManagerLogger, // assuming you have this
                                    onTradeEntered: (symbol, isLong, strategy, price, timestamp) =>
                                    {
                                        // Create a simple trade object for display
                                        // We use minimal required fields since this is just for display
                                        var tradeEntry = new TradeEntry
                                        {
                                            Symbol = symbol,
                                            IsLong = isLong,
                                            Strategy = strategy,
                                            EntryPrice = price,
                                            Timestamp = timestamp
                                        };

                                        // Pass to the ViewModel
                                        _recentTradesVm?.AddTradeEntry(tradeEntry);           
                                        
                                        // Log for debugging
                                        _logger.LogDebug($"Recent Trade Added: {tradeEntry.DisplayText}");
                                    }
            
            );
        }

        public void StopTrading(bool closeAllTrades)
        {
            lock (_startLock)
            {
                if (_isStopping || _cancellationTokenSource == null)
                {
                    _logger.LogWarning("Stop operation already in progress or not running");
                    return;
                }
                _isStopping = true;
            }

            try
            {
                _logger.LogDebug("Cancelling running operations...");
                _cancellationTokenSource.Cancel();

                if (closeAllTrades)
                {
                    _logger.LogInformation("Closing all open trades...");
                    CloseAllTrades().Wait(); // Run synchronously
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during stop trading");
            }
            finally
            {
                lock (_startLock)
                {
                    _isStopping = false;
                    _startInProgress = false;
                    _isRunning = false;
                }
            }
        }
        private async Task OnTermination(object sender, ConsoleCancelEventArgs e)
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
            await GenerateReport();
            Environment.Exit(0);
        }

        private async Task CloseAllTrades()
        {
            _logger.LogInformation("Starting to close all trades...");
            
            try
            {
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

                var currentPrices = await FetchCurrentPrices(_client, symbols, 
                    GetApiKeys().apiKey, GetApiKeys().apiSecret);
                    
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
                throw; // Re-throw to ensure the continuation task knows about the failure
            }
            finally
            {
                _logger.LogDebug("CloseAllTrades operation completed");
            }
        }

        private async Task GenerateReport()
        {
            var settings = new ReportSettings
            {
                StrategyName = _orderManager.GetStrategy().ToString(),
                Leverage = (int)_orderManager.GetLeverage(),
                TakeProfitMultiplier = _orderManager.GetTakeProfit(),
                StopLossRatio = _orderManager.GetStopLoss(),
                MarginPerTrade = _orderManager.GetMarginPerTrade(),
                Interval = _orderManager.GetInterval()
            };

            // 2. Generate report paths
            string reportDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Reports");
            Directory.CreateDirectory(reportDir);
            
            // Traditional CSV report
            string csvReportPath = Path.Combine(reportDir, $"performance_report_{_sessionId}.csv");
            
            // Enhanced text report
            string enhancedReportPath = Path.Combine(reportDir, $"enhanced_report_{_sessionId}.txt");

                // HTML report (new)
            string htmlReportPath = Path.Combine(reportDir, $"interactive_report_{_sessionId}.html");

            // 3. Generate both reports
            var activeCoinPairs = _orderManager.DatabaseManager.GetClosestCoinPairList(DateTime.UtcNow);
            string coinPairsFormatted = string.Join(",", activeCoinPairs.Select(cp => $"\"{cp}\""));

            // Generate traditional CSV report
            var reportGenerator = new ReportGenerator(_tradeLogger);
            reportGenerator.GenerateSummaryReport(_sessionId, csvReportPath, coinPairsFormatted);

            // Generate enhanced text report
            using (var writer = new StreamWriter(enhancedReportPath))
            {
                var enhancedReporter = new EnhancedReportGenerator(_tradeLogger, writer);
                enhancedReporter.GenerateEnhancedReport(_sessionId, settings);
            }

            var htmlReporter = new HtmlReportGenerator(_tradeLogger, _marketAnalyzer!);
            string htmlContent = await htmlReporter.GenerateHtmlReport(_sessionId, settings);
            //string htmlContent = htmlReporter.GenerateHtmlReport(_sessionId, settings);
            File.WriteAllText(htmlReportPath, htmlContent);

            // 4. Auto-open if enabled
            try 
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(htmlReportPath) { 
                    UseShellExecute = true 
                });

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(enhancedReportPath) { 
                    UseShellExecute = true 
                });
            }
            catch { /* Silent fail if opening doesn't work */ }
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
                decimal leverage = 10; // Default leverage
                decimal entrySize = 10; // Default entry size
            if (operationMode == OperationMode.LivePaperTrading || operationMode == OperationMode.LiveRealTrading)
            {                
                Console.Write("Enter Entry Size (default 10 USDT): ");
                string entrySizeInput = Console.ReadLine();
                entrySize = decimal.TryParse(entrySizeInput, out var parsedEntrySize) ? parsedEntrySize : 10;

                Console.Write("Enter Leverage (1 to 25, default 10): ");
                string leverageInput = Console.ReadLine();
                leverage = decimal.TryParse(leverageInput, out var parsedLeverage) ? parsedLeverage : 10;
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
            // Backtesting branch
            if (targetDateTime.HasValue)
            {
                var closestCoinPairList = dbManager.GetClosestCoinPairList(targetDateTime.Value);
                if (closestCoinPairList.Any())
                {
                    return closestCoinPairList;
                }
                else
                {
                    // Changed to use biggest coins for fallback
                    var topBiggestCoins = dbManager.GetBiggestCoins(50);
                    if (topBiggestCoins.Any())
                    {
                        return topBiggestCoins;
                    }
                }
            }

            // Live trading branch
            var symbols = await FetchCoinPairsFromFuturesAPI(client);

            foreach (var symbolInfo in symbols)
            {
                dbManager.UpsertCoinPairData(symbolInfo.Symbol, symbolInfo.Price, symbolInfo.Volume);
            }

            // Now using biggest coins for live trading too
            var topSymbols = dbManager.GetBiggestCoins(50);
            dbManager.UpsertCoinPairList(topSymbols, DateTime.UtcNow);

            return topSymbols;
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
                                            Wallet wallet, string fileName, OrderManager orderManager, 
                                            StrategyRunner runner, DateTime startDate,
                                            DateTime endDate, CancellationToken cancellationToken)
        {
            var backtestTakeProfits = new[] { orderManager.GetTakeProfit() };
            var backtestStopLosses = orderManager.GetStopLoss();  
            var intervals = new[] { interval };
            var leverage = orderManager.GetLeverage();

            try
            {
                foreach (var tp in backtestTakeProfits)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    for (int i = 0; i < intervals.Length; i++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        
                        wallet = new Wallet(1000);
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
                                            decimal takeProfit, 
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
                    try
                    {
                        await runner.RunStrategiesAsync();
                    }
                    catch (StrategyExecutionException see)
                    {
                        _logger.LogError($"Strategies partially failed: {see.ErrorStats.Sum(kv => kv.Value)} errors");
                        foreach (var error in see.ErrorStats)
                        {
                            _logger.LogWarning($"{error.Value}x {error.Key}");
                        }
                    }

                    _logger.LogInformation($"---- Cycle {cycles + 1} Completed ----");
                    
                    if (currentPrices != null)
                    {
                        _logger.LogDebug("Checking and closing trades...");
                        await orderManager.CheckAndCloseTrades(currentPrices);
                        
                        _logger.LogDebug("Logging active trades...");
                        orderManager.PrintActiveTrades(currentPrices);
                        
                        _logger.LogDebug("Logging wallet balance...");
                        orderManager.PrintWalletBalance();

                        // Update Paper Wallet UI once per cycle
                        try
                        {
                            UpdatePaperWalletSnapshot(currentPrices);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to update paper wallet snapshot");
                        }
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

        private void UpdatePaperWalletSnapshot(Dictionary<string, decimal> currentPrices)
        {
            if (_paperWalletVm == null || _orderManager == null || currentPrices == null)
                return;

            var activeTrades = _orderManager.GetActiveTrades();
            decimal used = 0m;
            decimal unrealized = 0m;

            foreach (var t in activeTrades)
            {
                used += t.InitialMargin;
                if (currentPrices.TryGetValue(t.Symbol, out var price))
                {
                    unrealized += t.IsLong ? (price - t.EntryPrice) * t.Quantity
                                            : (t.EntryPrice - price) * t.Quantity;
                }
            }

            var walletBalance = _wallet.GetBalance();
            _paperWalletVm.UpdateSnapshot(walletBalance, used, unrealized, activeTrades.Count);
        }

        private static async Task RunLiveTrading(
            RestClient client, 
            List<string> symbols, 
            string interval,
            Wallet wallet, 
            string fileName, 
            decimal takeProfit, 
            OrderManager orderManager, 
            StrategyRunner runner,
            CancellationToken cancellationToken,
            ILogger logger)
        {
            var startTime = DateTime.Now;
            int cycles = 0;
            var onBinance = new BinanceActivities(client);
            var delay = TimeSpan.Zero; // Start immediately

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // 1. Execute trading logic FIRST
                    bool handledOrders = await onBinance.HandleOpenOrdersAndActiveTrades(symbols);
                    
                    if (handledOrders)
                    {
                        var currentPrices = await FetchCurrentPrices(client, symbols, GetApiKeys().apiKey, GetApiKeys().apiSecret);
                        try
                        {
                            await runner.RunStrategiesAsync();
                        }
                        catch (StrategyExecutionException see)
                        {
                            logger.LogError($"Strategies partially failed: {see.ErrorStats.Sum(kv => kv.Value)} errors");
                            foreach (var error in see.ErrorStats)
                            {
                                logger.LogWarning($"{error.Value}x {error.Key}");
                            }
                        }
                    }

                    // 2. Update symbols periodically
                    if (++cycles % 20 == 0)
                    {
                        symbols = await GetBestListOfSymbols(client, orderManager.DatabaseManager);
                    }

                    // 3. Delay AFTER work is done
                    delay = TimeTools.GetTimeSpanFromInterval(interval);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
                catch (Exception ex)  // Never swallow exceptions completely
                {
                    Console.WriteLine($"Cycle failed: {ex.Message}");
                    if (cancellationToken.IsCancellationRequested)
                        break;
                    
                    // Emergency throttle if errors persist
                    delay = TimeSpan.FromSeconds(5); 
                }

                // 4. Controlled delay with cancellation
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, cancellationToken);
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

        public class StrategyExecutionException : Exception
        {
            public Dictionary<string, int> ErrorStats { get; }
            
            public StrategyExecutionException(string message, AggregateException inner) 
                : base(message, inner)
            {
                ErrorStats = inner.InnerExceptions
                    .GroupBy(e => e.Message)
                    .ToDictionary(g => g.Key, g => g.Count());
            }
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
            request.AddParameter("limit", 1000);
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
        public void SetRecentTradesViewModel(RecentTradesViewModel recentTradesVm)
        {
            _recentTradesVm = recentTradesVm;    
            Console.WriteLine($"✅ SetRecentTradesViewModel called - ViewModel is {(recentTradesVm != null ? "NOT NULL" : "NULL")}");
        }
    
    }
}