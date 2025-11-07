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
        
    private static TradeLogger _tradeLogger = null!;
        private readonly ReportSettings _reportSettings;
    private static string _sessionId = string.Empty;
    private static OrderManager _orderManager = null!;
    private static RestClient _client = null!; // Lift client to class level
        
        private readonly TimeSpan _defaultTimeout = TimeSpan.FromSeconds(10);
    private static Wallet _wallet = null!;
    private CancellationTokenSource? _cancellationTokenSource;
        private volatile bool _isStopping = false;
        private volatile bool _startInProgress = false;
        
        public bool IsStopping => _isStopping;
        public bool StartInProgress => _startInProgress;
    private volatile bool _isRunning = false;
    private volatile bool _reportGenerated = false;
    private OperationMode _currentMode;
    private readonly object _reportLock = new();

    public bool IsRunning => _cancellationTokenSource != null && 
                !_cancellationTokenSource.IsCancellationRequested;

        private readonly ILogger<BinanceTradingService> _logger;
        
    private MarketContextAnalyzer? _marketAnalyzer;
        private readonly ILoggerFactory _loggerFactory;
        private readonly object _startLock = new();
        private int _symbolRefreshEveryNCycles = 20; // configurable via appsettings
    private enum SymbolSelectionMode { Volume, Volatility, Custom }
    private SymbolSelectionMode _symbolSelectionMode = SymbolSelectionMode.Volume;
    private int _symbolSelectionCount = 50;
        
        public BinanceTradingService(ILogger<BinanceTradingService> logger, ILoggerFactory loggerFactory)
        {
            _logger = logger;
            _loggerFactory = loggerFactory;
            
            // Initialize report settings and bind from appsettings (if present)
            _reportSettings = new ReportSettings();

            // Load configuration
            var config = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json")
                .Build();
            try
            {
                // Load optional report settings without relying on ConfigurationBinder (no NuGet dep)
                var reportSection = config.GetSection("ReportSettings");
                if (reportSection.Exists())
                {
                    var s = reportSection["OutputPath"]; if (!string.IsNullOrWhiteSpace(s)) _reportSettings.OutputPath = s;
                    s = reportSection["AutoOpen"]; if (bool.TryParse(s, out var b)) _reportSettings.AutoOpen = b;
                    s = reportSection["ShowTradeDetails"]; if (bool.TryParse(s, out b)) _reportSettings.ShowTradeDetails = b;
                    s = reportSection["TopPerformersCount"]; if (int.TryParse(s, out var i)) _reportSettings.TopPerformersCount = i;
                    s = reportSection["MaxCriticalTradesToShow"]; if (int.TryParse(s, out i)) _reportSettings.MaxCriticalTradesToShow = i;
                    s = reportSection["DateFormat"]; if (!string.IsNullOrWhiteSpace(s)) _reportSettings.DateFormat = s;
                    s = reportSection["NumberFormat"]; if (!string.IsNullOrWhiteSpace(s)) _reportSettings.NumberFormat = s;
                    s = reportSection["LiquidationWarningThreshold"]; if (decimal.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var d)) _reportSettings.LiquidationWarningThreshold = d;
                }
            }
            catch { /* fall back to defaults */ }
            // Load optional config for symbol refresh cadence (fallback to 20 if missing/invalid)
            if (int.TryParse(config["SymbolRefreshEveryNCycles"], out var n) && n > 0)
            {
                _symbolRefreshEveryNCycles = n;
            }
            // Load symbol selection mode and count
            var mode = config["SymbolRefreshMode"];
            if (!string.IsNullOrWhiteSpace(mode))
            {
                if (mode.Equals("Volatility", StringComparison.OrdinalIgnoreCase)) _symbolSelectionMode = SymbolSelectionMode.Volatility;
                else if (mode.Equals("Custom", StringComparison.OrdinalIgnoreCase)) _symbolSelectionMode = SymbolSelectionMode.Custom;
                else _symbolSelectionMode = SymbolSelectionMode.Volume;
            }
            if (int.TryParse(config["SymbolSelectionCount"], out var selCount) && selCount > 0)
            {
                _symbolSelectionCount = selCount;
            }
            

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
                    List<string>? customCoinSelection = null)
        {
            _currentMode = operationMode;
            _reportGenerated = false;
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
                // Interpret activation as ATR multiplier and show an example derived percent using BTCUSDT (best-effort)
                string samplePctText = "~N/A";
                try
                {
                    var samplePct = await TryComputeSampleDerivedPercent("BTCUSDT", interval, _uiTrailingActivationPercent);
                    if (samplePct.HasValue)
                        samplePctText = $"~{samplePct.Value:F2}% on BTCUSDT";
                }
                catch { /* ignore sample failure */ }

                _logger.LogDebug($"Params: Interval={interval}, Entry={entrySize}USDT, Leverage={leverage}x, Exit=Trailing (Act={_uiTrailingActivationPercent:F1}× ATR, {samplePctText}, Cb={_uiTrailingCallbackPercent:F1}%, RR=1:{stopLoss:F1})");
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
            if (!string.IsNullOrEmpty(dbDir) && !Directory.Exists(dbDir))
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
                _symbolSelectionMode = SymbolSelectionMode.Custom; // sticky custom for this session

                // Save this selection to database for persistence
                databaseManager.UpsertCoinPairList(symbols, DateTime.UtcNow);

                // Targeted refresh of CoinPairData for these selected symbols (fresh lastPrice + base volume)
                try
                {
                    await RefreshCoinPairDataForSymbols(_client, databaseManager, symbols);
                    _logger.LogInformation($"Refreshed CoinPairData for {symbols.Count} selected symbols");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to refresh CoinPairData for custom selection");
                }
            }
            else
            {
                // Use the default auto-selection logic
                _logger.LogInformation("Fetching symbols to trade using auto-selection...");
                if (_symbolSelectionMode == SymbolSelectionMode.Volatility)
                {
                    symbols = await GetMostVolatileSymbols(_client, databaseManager, _symbolSelectionCount);
                }
                else
                {
                    symbols = await GetBestListOfSymbols(_client, databaseManager);
                }
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
                        if (!startDate.HasValue || !endDate.HasValue)
                            throw new ArgumentException("Backtest requires startDate and endDate");
                        await RunBacktest(_client, symbols, interval, _wallet, "", 
                                          _orderManager, runner, 
                                          startDate.Value, endDate.Value,
                                          _cancellationTokenSource.Token);
                        break;
                    case OperationMode.LiveRealTrading:
                        await RunLiveTrading(_client, symbols, interval, _wallet, "", 
                                        takeProfit, _orderManager, 
                                        runner, _cancellationTokenSource.Token, _symbolRefreshEveryNCycles, _symbolSelectionMode, _symbolSelectionCount, logger: _logger);
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
            // Generate report asynchronously for Backtest or Paper only (skip Live Real)
            if (_currentMode != OperationMode.LiveRealTrading)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        _logger.LogInformation("Attempting background report generation...");
                        await TryGenerateReportAsync(forceOpen: false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Background report generation failed");
                    }
                });
            }
        }

        public async Task<ExchangeInfo> GetExchangeInfoAsync()
        {
            var request = new RestRequest("/fapi/v1/exchangeInfo", Method.Get);
            
            try
            {
                // Double timeout protection
                using var cts = new CancellationTokenSource(_defaultTimeout);
                
                var response = await _client.ExecuteAsync(request, cts.Token);
                
                if (!response.IsSuccessful || string.IsNullOrWhiteSpace(response.Content))
                {
                    Console.WriteLine($"API Error: {response.StatusCode} - {response.ErrorMessage}");
                    return new ExchangeInfo();
                }

                var parsed = JsonConvert.DeserializeObject<ExchangeInfo>(response.Content);
                return parsed ?? new ExchangeInfo();
            }
            catch (TaskCanceledException)
            {
                Console.WriteLine("Request timed out (5s limit reached)");
                return new ExchangeInfo();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Unexpected error: {ex.Message}");
                return new ExchangeInfo();
            }
        }

        public void Dispose() => _client?.Dispose();

        public void SetPaperWalletViewModel(PaperWalletViewModel vm)
        {
            _paperWalletVm = vm;
        }

        // Compute a sample derived activation percent using ATR(14) on the given symbol and interval
        private async Task<decimal?> TryComputeSampleDerivedPercent(string symbol, string interval, decimal atrMultiplier)
        {
            try
            {
                // Fetch klines (limit ~60 for a safe ATR window)
                var req = new RestRequest($"/fapi/v1/klines", Method.Get)
                    .AddParameter("symbol", symbol)
                    .AddParameter("interval", interval)
                    .AddParameter("limit", 60);
                var resp = await _client.ExecuteAsync(req);
                if (!resp.IsSuccessful || string.IsNullOrWhiteSpace(resp.Content)) return null;

                // Parse klines: [ openTime, open, high, low, close, volume, ... ]
                var arr = Newtonsoft.Json.Linq.JArray.Parse(resp.Content);
                if (arr.Count < 20) return null;

                var highs = new List<decimal>();
                var lows = new List<decimal>();
                var closes = new List<decimal>();
                foreach (var k in arr)
                {
                    highs.Add(Convert.ToDecimal((string)k[2], CultureInfo.InvariantCulture));
                    lows.Add(Convert.ToDecimal((string)k[3], CultureInfo.InvariantCulture));
                    closes.Add(Convert.ToDecimal((string)k[4], CultureInfo.InvariantCulture));
                }

                // Compute TR and ATR(14) approximated with Wilder's smoothing
                int period = 14;
                var trs = new List<decimal>();
                for (int i = 1; i < highs.Count; i++)
                {
                    var h = highs[i];
                    var l = lows[i];
                    var pc = closes[i - 1];
                    var tr = Math.Max((double)(h - l), Math.Max(Math.Abs((double)(h - pc)), Math.Abs((double)(l - pc))));
                    trs.Add((decimal)tr);
                }
                if (trs.Count < period) return null;

                // Initial ATR = average of first 'period' TRs
                decimal atr = trs.Take(period).Average();
                // Wilder smoothing for the rest
                for (int i = period; i < trs.Count; i++)
                {
                    atr = (atr * (period - 1) + trs[i]) / period;
                }

                var lastClose = closes.Last();
                if (lastClose <= 0) return null;
                var atrPercent = (atr / lastClose) * 100m;
                var derived = atrPercent * Math.Abs(atrMultiplier);
                return Math.Max(0.01m, derived);
            }
            catch
            {
                return null;
            }
        }

        // Set by MainWindow before StartTrading
        // Note: activationPercent parameter here represents an ATR multiplier when trailing is enabled.
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

        // New async stop to avoid blocking threads and the UI
        public async Task StopTradingAsync(bool closeAllTrades)
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
                    try
                    {
                        await CloseAllTrades().ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        // Do not block report generation on close failures
                        _logger.LogWarning(ex, "CloseAllTrades failed during stop; continuing to report generation");
                    }
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
                // For paper trading or backtest, generate the report immediately on stop and auto-open it
                if ((_currentMode == OperationMode.LivePaperTrading || _currentMode == OperationMode.Backtest))
                {
                    try
                    {
                        _logger.LogInformation($"Attempting report generation on stop ({_currentMode})...");
                        await TryGenerateReportAsync(forceOpen: true).ConfigureAwait(false);
                        _logger.LogInformation("Report generation on stop completed (or was already done)");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Report generation on stop failed");
                    }
                }
            }
        }

        private async Task TryGenerateReportAsync(bool forceOpen)
        {
            bool shouldRun = false;
            lock (_reportLock)
            {
                if (!_reportGenerated)
                {
                    _reportGenerated = true; // claim generation to prevent duplicates
                    shouldRun = true;
                }
            }

            if (!shouldRun)
            {
                _logger.LogDebug("Report already generated or in-progress; skipping generation");
                return;
            }

            await GenerateReport(forceOpen);
        }

        // Backwards-compatible synchronous wrapper for callers that expect a blocking StopTrading
        public void StopTrading(bool closeAllTrades)
        {
            // Call the async version and block the current thread. Prefer StopTradingAsync where possible.
            StopTradingAsync(closeAllTrades).GetAwaiter().GetResult();
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

            Dictionary<string, decimal>? currentPrices = null;
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

        private async Task GenerateReport(bool forceOpen = false)
        {
            // Compose run-specific settings using defaults from appsettings
            var settings = new ReportSettings
            {
                StrategyName = _orderManager.GetStrategy().ToString(),
                Leverage = (int)_orderManager.GetLeverage(),
                TakeProfitMultiplier = _orderManager.GetTakeProfit(),
                StopLossRatio = _orderManager.GetStopLoss(),
                MarginPerTrade = _orderManager.GetMarginPerTrade(),
                Interval = _orderManager.GetInterval(),
                OutputPath = _reportSettings.OutputPath,
                AutoOpen = _reportSettings.AutoOpen,
                ShowTradeDetails = _reportSettings.ShowTradeDetails,
                TopPerformersCount = _reportSettings.TopPerformersCount,
                MaxCriticalTradesToShow = _reportSettings.MaxCriticalTradesToShow,
                DateFormat = _reportSettings.DateFormat,
                NumberFormat = _reportSettings.NumberFormat,
                LiquidationWarningThreshold = _reportSettings.LiquidationWarningThreshold
            };

            // 2. Generate report paths
            string reportDir = Path.IsPathRooted(settings.OutputPath)
                ? settings.OutputPath
                : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, settings.OutputPath);
            Directory.CreateDirectory(reportDir);
            
            // Traditional CSV report
            string csvReportPath = Path.Combine(reportDir, $"performance_report_{_sessionId}.csv");
            
            // Enhanced text report
            string enhancedReportPath = Path.Combine(reportDir, $"enhanced_report_{_sessionId}.txt");

                // HTML report (new)
            string htmlReportPath = Path.Combine(reportDir, $"interactive_report_{_sessionId}.html");

            // 3. Generate reports (HTML first for faster availability)
            var activeCoinPairs = _orderManager.DatabaseManager.GetClosestCoinPairList(DateTime.UtcNow);
            string coinPairsFormatted = string.Join(",", activeCoinPairs.Select(cp => $"\"{cp}\""));

            var overallTimer = Stopwatch.StartNew();
            // HTML report
            var htmlTimer = Stopwatch.StartNew();
            var htmlReporter = new HtmlReportGenerator(_tradeLogger, _marketAnalyzer!);
            string htmlContent = await htmlReporter.GenerateHtmlReport(_sessionId, settings);
            File.WriteAllText(htmlReportPath, htmlContent);
            htmlTimer.Stop();
            _logger.LogInformation($"HTML report generated in {htmlTimer.ElapsedMilliseconds} ms");

            // Kick off CSV and Enhanced text in parallel (they are backups)
            var csvTask = Task.Run(() =>
            {
                var t = Stopwatch.StartNew();
                try
                {
                    var reportGenerator = new ReportGenerator(_tradeLogger);
                    reportGenerator.GenerateSummaryReport(_sessionId, csvReportPath, coinPairsFormatted);
                    _logger.LogInformation($"CSV report generated in {t.ElapsedMilliseconds} ms");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "CSV report generation failed");
                }
            });

            var txtTask = Task.Run(() =>
            {
                var t = Stopwatch.StartNew();
                try
                {
                    using var writer = new StreamWriter(enhancedReportPath);
                    var enhancedReporter = new EnhancedReportGenerator(_tradeLogger, writer);
                    enhancedReporter.GenerateEnhancedReport(_sessionId, settings);
                    _logger.LogInformation($"Enhanced text report generated in {t.ElapsedMilliseconds} ms");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Enhanced text report generation failed");
                }
            });

            await Task.WhenAll(csvTask, txtTask);
            overallTimer.Stop();
            _logger.LogInformation($"All reports completed in {overallTimer.ElapsedMilliseconds} ms");

            // 4. Auto-open if enabled or forced
            if (forceOpen || settings.AutoOpen)
            {
                try 
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(htmlReportPath) { 
                        UseShellExecute = true 
                    });
                }
                catch { /* ignore open failures */ }
            }
            _reportGenerated = true;
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
                string? intervalInput = Console.ReadLine();
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
                string? entrySizeInput = Console.ReadLine();
                entrySize = decimal.TryParse(entrySizeInput, out var parsedEntrySize) ? parsedEntrySize : 10;

                Console.Write("Enter Leverage (1 to 25, default 10): ");
                string? leverageInput = Console.ReadLine();
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
            string? dirInput = Console.ReadLine();
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
                
                string? tpInput = Console.ReadLine();
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

        private static async Task<List<string>> GetMostVolatileSymbols(RestClient client, DatabaseManager dbManager, int count = 50)
        {
            var request = new RestRequest("/fapi/v1/ticker/24hr", Method.Get);
            var result = new List<string>();

            try
            {
                var response = await client.ExecuteAsync(request);
                if (!response.IsSuccessful || string.IsNullOrWhiteSpace(response.Content))
                {
                    Console.WriteLine($"Failed to fetch 24h tickers for volatility: {response.StatusCode} - {response.ErrorMessage}");
                    return result;
                }

                var tickers = JsonConvert.DeserializeObject<List<Ticker24h>>(response.Content) ?? new List<Ticker24h>();

                var candidates = new List<(string symbol, decimal absChange, decimal price, decimal baseVol)>();
                foreach (var t in tickers)
                {
                    if (string.IsNullOrWhiteSpace(t.symbol) || !t.symbol.EndsWith("USDT", StringComparison.OrdinalIgnoreCase))
                        continue;

                    decimal changePct = 0m;
                    if (!string.IsNullOrWhiteSpace(t.priceChangePercent))
                        decimal.TryParse(t.priceChangePercent, NumberStyles.Any, CultureInfo.InvariantCulture, out changePct);

                    decimal price = 0m;
                    if (!string.IsNullOrWhiteSpace(t.lastPrice))
                        decimal.TryParse(t.lastPrice, NumberStyles.Any, CultureInfo.InvariantCulture, out price);

                    decimal baseVolume = 0m;
                    if (!string.IsNullOrWhiteSpace(t.volume))
                        decimal.TryParse(t.volume, NumberStyles.Any, CultureInfo.InvariantCulture, out baseVolume);

                    candidates.Add((t.symbol!, Math.Abs(changePct), price, baseVolume));
                }

                var top = candidates
                    .OrderByDescending(x => x.absChange)
                    .ThenByDescending(x => x.baseVol * x.price) // tie-break by dollar volume
                    .Take(Math.Max(1, count))
                    .ToList();

                foreach (var c in top)
                {
                    dbManager.UpsertCoinPairData(c.symbol, c.price, c.baseVol);
                    result.Add(c.symbol);
                }

                if (result.Count > 0)
                {
                    dbManager.UpsertCoinPairList(result, DateTime.UtcNow);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception computing volatility list: {ex.Message}");
            }

            return result;
        }

        
        private static async Task<List<CoinPairInfo>> FetchCoinPairsFromFuturesAPI(RestClient client)
        {
            // Use bulk 24h ticker to avoid per-symbol requests
            // Endpoint returns an array with fields including symbol, lastPrice, volume, quoteVolume
            var request = new RestRequest("/fapi/v1/ticker/24hr", Method.Get);

            var result = new List<CoinPairInfo>();

            try
            {
                var response = await client.ExecuteAsync(request);
                if (!response.IsSuccessful || string.IsNullOrWhiteSpace(response.Content))
                {
                    Console.WriteLine($"Failed to fetch 24h tickers: {response.StatusCode} - {response.ErrorMessage}");
                    return result;
                }

                var tickers = JsonConvert.DeserializeObject<List<Ticker24h>>(response.Content);
                if (tickers == null || tickers.Count == 0)
                    return result;

                foreach (var t in tickers)
                {
                    // Focus on USDT pairs; skip non-standard symbols
                    if (string.IsNullOrWhiteSpace(t.symbol) || !t.symbol.EndsWith("USDT", StringComparison.OrdinalIgnoreCase))
                        continue;

                    // Use BASE ASSET volume for DB (DB computes VolumeInUSDT as price * volume)
                    decimal baseVolume = 0m;
                    if (!string.IsNullOrWhiteSpace(t.volume))
                        decimal.TryParse(t.volume, NumberStyles.Any, CultureInfo.InvariantCulture, out baseVolume);

                    decimal price = 0m;
                    if (!string.IsNullOrWhiteSpace(t.lastPrice))
                        decimal.TryParse(t.lastPrice, NumberStyles.Any, CultureInfo.InvariantCulture, out price);

                    result.Add(new CoinPairInfo
                    {
                        Symbol = t.symbol,
                        Volume = baseVolume,
                        Price = price
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception reading 24h tickers: {ex.Message}");
            }

            return result;
        }

        private static async Task RefreshCoinPairDataForSymbols(RestClient client, DatabaseManager dbManager, IEnumerable<string> symbols)
        {
            if (symbols == null) return;
            var desired = new HashSet<string>(symbols, StringComparer.OrdinalIgnoreCase);
            if (desired.Count == 0) return;

            // Bulk 24h ticker (all symbols), then filter to selected
            var request = new RestRequest("/fapi/v1/ticker/24hr", Method.Get);
            var response = await client.ExecuteAsync(request);
            if (!response.IsSuccessful || string.IsNullOrWhiteSpace(response.Content))
            {
                Console.WriteLine($"RefreshCoinPairDataForSymbols: failed to fetch 24h tickers: {response.StatusCode} - {response.ErrorMessage}");
                return;
            }

            var tickers = JsonConvert.DeserializeObject<List<Ticker24h>>(response.Content) ?? new List<Ticker24h>();
            foreach (var t in tickers)
            {
                var sym = t?.symbol;
                if (string.IsNullOrWhiteSpace(sym)) continue;
                if (!desired.Contains(sym)) continue;

                decimal price = 0m;
                if (!string.IsNullOrWhiteSpace(t!.lastPrice))
                    decimal.TryParse(t.lastPrice, NumberStyles.Any, CultureInfo.InvariantCulture, out price);

                decimal baseVolume = 0m;
                if (!string.IsNullOrWhiteSpace(t!.volume))
                    decimal.TryParse(t.volume, NumberStyles.Any, CultureInfo.InvariantCulture, out baseVolume);

                dbManager.UpsertCoinPairData(sym, price, baseVolume);
            }
        }

        private static async Task<decimal> FetchSymbolVolume(RestClient client, string symbol)
        {
            // Example to fetch symbol's volume
            var request = new RestRequest("/fapi/v1/ticker/24hr", Method.Get);
            request.AddParameter("symbol", symbol);

            var response = await client.ExecuteAsync(request);
            if (response.IsSuccessful && !string.IsNullOrWhiteSpace(response.Content))
            {
                var symbolData = JsonConvert.DeserializeObject<SymbolDataResponse>(response.Content);
                return symbolData?.Volume ?? 0m;
            }

            return 0m;
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
            if (response.IsSuccessful && !string.IsNullOrWhiteSpace(response.Content))
            {
                var priceData = JsonConvert.DeserializeObject<PriceResponse>(response.Content);
                return priceData?.Price ?? 0m;
            }

            return 0m;
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
                if (_symbolRefreshEveryNCycles > 0 && cycles % _symbolRefreshEveryNCycles == 0)
                {
                    _logger.LogInformation($"Updating symbols based on {_symbolSelectionMode} (every {_symbolRefreshEveryNCycles} cycles)...");
                    Stopwatch timer = Stopwatch.StartNew();
                    switch (_symbolSelectionMode)
                    {
                        case SymbolSelectionMode.Custom:
                            await RefreshCoinPairDataForSymbols(client, orderManager.DatabaseManager, symbols);
                            break;
                        case SymbolSelectionMode.Volatility:
                            symbols = await GetMostVolatileSymbols(client, orderManager.DatabaseManager, _symbolSelectionCount);
                            break;
                        default:
                            symbols = await GetBestListOfSymbols(client, orderManager.DatabaseManager);
                            break;
                    }
                    timer.Stop();
                    _logger.LogInformation($"Updated symbols in {timer.ElapsedMilliseconds}ms");
                }

                var delay = TimeTools.GetTimeSpanFromInterval(interval);
                _logger.LogDebug($"Waiting {delay.TotalSeconds} seconds until next cycle");
                // IMPORTANT: pass cancellationToken so Stop() cancels the wait immediately
                await Task.Delay(delay, cancellationToken);
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
            int symbolRefreshEveryNCycles,
            SymbolSelectionMode selectionMode,
            int selectionCount,
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
                    if (symbolRefreshEveryNCycles > 0 && ++cycles % symbolRefreshEveryNCycles == 0)
                    {
                        switch (selectionMode)
                        {
                            case SymbolSelectionMode.Custom:
                                await RefreshCoinPairDataForSymbols(client, orderManager.DatabaseManager, symbols);
                                break;
                            case SymbolSelectionMode.Volatility:
                                symbols = await GetMostVolatileSymbols(client, orderManager.DatabaseManager, selectionCount);
                                break;
                            default:
                                symbols = await GetBestListOfSymbols(client, orderManager.DatabaseManager);
                                break;
                        }
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
            // Use bulk ticker to reduce N requests to 1; no auth required on this endpoint
            var prices = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var request = new RestRequest("/fapi/v1/ticker/price", Method.Get);
                var response = await client.ExecuteAsync(request);
                if (!response.IsSuccessful || string.IsNullOrWhiteSpace(response.Content))
                {
                    Console.WriteLine($"Failed to fetch prices: {response.StatusCode} - {response.ErrorMessage}");
                    return prices;
                }

                var all = JsonConvert.DeserializeObject<List<PriceResponse>>(response.Content) ?? new List<PriceResponse>();
                var set = new HashSet<string>(symbols, StringComparer.OrdinalIgnoreCase);
                foreach (var item in all)
                {
                    if (item == null || string.IsNullOrWhiteSpace(item.Symbol)) continue;
                    if (!set.Contains(item.Symbol)) continue;

                    prices[item.Symbol] = item.Price;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching bulk prices: {ex.Message}");
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
            public List<SymbolInfo>? Symbols { get; set; }
        }      

        public class ServerTimeResponse
        {
            [JsonProperty("serverTime")]
            public long ServerTime { get; set; }
        }

        // Create a class for the expected response structure
        public class PriceResponse
        {
            public string Symbol { get; set; } = string.Empty;
            public decimal Price { get; set; }
        }

        private class Ticker24h
        {
            // Binance returns strings for numeric fields in many endpoints
            public string? symbol { get; set; }
            public string? lastPrice { get; set; }
            public string? volume { get; set; }
            public string? quoteVolume { get; set; }
            public string? priceChangePercent { get; set; }
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
                if (klineData != null && klineData.Any())
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
                foreach (var kline in klineData ?? Enumerable.Empty<List<object>>())
                {
                    // Defensive parsing with null checks
                    long openTime = kline.Count > 0 && kline[0] != null ? Convert.ToInt64(kline[0], CultureInfo.InvariantCulture) : 0L;
                    string s1 = kline.Count > 1 ? kline[1]?.ToString() ?? "0" : "0";
                    string s2 = kline.Count > 2 ? kline[2]?.ToString() ?? "0" : "0";
                    string s3 = kline.Count > 3 ? kline[3]?.ToString() ?? "0" : "0";
                    string s4 = kline.Count > 4 ? kline[4]?.ToString() ?? "0" : "0";
                    string s5 = kline.Count > 5 ? kline[5]?.ToString() ?? "0" : "0";
                    long closeTime = kline.Count > 6 && kline[6] != null ? Convert.ToInt64(kline[6], CultureInfo.InvariantCulture) : 0L;
                    string tradesStr = kline.Count > 8 ? kline[8]?.ToString() ?? "0" : "0";

                    decimal open = decimal.TryParse(s1, NumberStyles.Any, CultureInfo.InvariantCulture, out var _open) ? _open : 0m;
                    decimal high = decimal.TryParse(s2, NumberStyles.Any, CultureInfo.InvariantCulture, out var _high) ? _high : 0m;
                    decimal low = decimal.TryParse(s3, NumberStyles.Any, CultureInfo.InvariantCulture, out var _low) ? _low : 0m;
                    decimal close = decimal.TryParse(s4, NumberStyles.Any, CultureInfo.InvariantCulture, out var _close) ? _close : 0m;
                    decimal vol = decimal.TryParse(s5, NumberStyles.Any, CultureInfo.InvariantCulture, out var _vol) ? _vol : 0m;
                    int trades = int.TryParse(tradesStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var _trades) ? _trades : 0;

                    historicalData.Add(new Kline
                    {
                        OpenTime = openTime,
                        Open = open,
                        High = high,
                        Low = low,
                        Close = close,
                        Volume = vol,
                        CloseTime = closeTime,
                        NumberOfTrades = trades
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
            public string Symbol { get; set; } = string.Empty;
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