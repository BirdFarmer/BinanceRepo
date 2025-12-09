using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BinanceTestnet.Enums; 
using TradingAppDesktop.Services;
using System.ComponentModel;
using System.IO;
using System.Diagnostics;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using TradingAppDesktop.Controls;
using TradingAppDesktop.Views;


namespace TradingAppDesktop
{
    public partial class MainWindow : Window
    {
        private BinanceTradingService _tradingService = null!; // set by App on startup via SetTradingService
        private System.IO.FileSystemWatcher? _insWatcher;
        private bool _isStarting = false; // Add this class field
        private readonly object _startLock = new(); // Add this for thread safety
        private List<string>? _customCoinSelection = null;
        private RecentTradesViewModel _recentTradesVm;
        private PaperWalletViewModel _paperWalletVm;
        
        // Trailing UI state
        private bool _useTrailing = false;
        private decimal _trailingActivationPercent = 2.8m; // reuse ATR slider default
        private decimal _trailingCallbackPercent = 1.0m;
        // Exit PnL% UI state (runtime only)
        private decimal? _exitPnLPct = null;
        
        // Persisted settings
        private UserSettings? _userSettings = null;
        // UI: Closed candles toggle state (persisted)
        private bool _useClosedCandles = false;
    


        public MainWindow()
        {
            InitializeComponent();
        
            // Initialize Recent Trades ViewModel
            _recentTradesVm = new RecentTradesViewModel();
            RecentTradesList.DataContext = _recentTradesVm;        

            // Initialize Paper Wallet ViewModel (for paper mode)
            _paperWalletVm = new PaperWalletViewModel();
            if (PaperWalletPanel != null)
                PaperWalletPanel.DataContext = _paperWalletVm;

            // One-time initialization that doesn't need loaded controls
            Console.SetOut(new TextBoxWriter(LogText));
            StartButton.Click += StartButton_Click;
            StopButton.Click += StopButton_Click;

            // Ensure StrategySelector selection changes update the UI (SelectedStrategies is an ObservableCollection)
            try
            {
                if (StrategySelector != null && StrategySelector.SelectedStrategies != null)
                {
                    StrategySelector.SelectedStrategies.CollectionChanged += (s, ev) =>
                    {
                        // call the existing handler to update closed-candle UI
                        OnStrategySelectionChanged(this, null);
                    };
                    // initialize state based on any pre-selected strategies
                    OnStrategySelectionChanged(this, null);
                }
            }
            catch { }

            // UI-dependent initialization
            this.Loaded += (s, e) =>
            {
                InitializeComboBoxes();
                AtrMultiplierText.Text = $"{AtrMultiplierSlider.Value:F1} * ATR";
                RiskRewardText.Text = $"1:{RiskRewardSlider.Value:F1}";
                CallbackSlider.Value = (double)_trailingCallbackPercent;

                // Load persisted settings once controls are ready
                _userSettings = UserSettings.Load();
                ApplyUserSettingsToUi();

                // Initialize closed-candle checkbox from persisted settings
                if (UseClosedCandlesCheckBox != null)
                {
                    UseClosedCandlesCheckBox.IsChecked = _userSettings?.UseClosedCandles ?? false;
                    _useClosedCandles = UseClosedCandlesCheckBox.IsChecked == true;
                    // Propagate to runtime config for strategies
                    BinanceTestnet.Strategies.Helpers.StrategyRuntimeConfig.UseClosedCandles = _useClosedCandles;
                }

                // Apply persisted theme if present
                try
                {
                    var themeKey = _userSettings?.Theme;
                    if (!string.IsNullOrEmpty(themeKey))
                        ThemeManager.ApplyTheme(themeKey);
                }
                catch { }
            };
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var win = new Views.SettingsWindow { Owner = this };
                win.ShowDialog();
            }
            catch (System.Exception ex)
            {
                Log($"Failed to open Settings: {ex.Message}");
            }
        }

        private void InitializeComboBoxes()
        {
            // Operation Mode
            OperationModeComboBox.ItemsSource = Enum.GetValues(typeof(OperationMode));
            OperationModeComboBox.SelectedIndex = 0;

            StrategySelector.SetAvailableStrategies(new List<StrategyItem>
            {
                new StrategyItem(SelectedTradingStrategy.EmaStochRsi, "EMA + Stoch RSI", "Combines EMA crossover with Stochastic RSI"),
                new StrategyItem(SelectedTradingStrategy.EnhancedMACD, "Enhanced MACD", "Modified MACD with additional filters"),
                new StrategyItem(SelectedTradingStrategy.FVG, "Fair Value Gap", "Price action based fair value gaps"),
                new StrategyItem(SelectedTradingStrategy.IchimokuCloud, "Ichimoku Cloud", "Complete Ichimoku Kinko Hyo system"),
                new StrategyItem(SelectedTradingStrategy.CandleDistributionReversal, "Candle Distribution", "Reversal patterns based on candle distribution"),
                new StrategyItem(SelectedTradingStrategy.RSIMomentum, "RSI Momentum", "Pure RSI momentum strategy"),
                new StrategyItem(SelectedTradingStrategy.MACDStandard, "Standard MACD", "Classic MACD crossover"),
                new StrategyItem(SelectedTradingStrategy.RsiDivergence, "RSI Divergence", "Divergence detection with RSI"),
                new StrategyItem(SelectedTradingStrategy.FibonacciRetracement, "Fibonacci", "Fibonacci retracement levels"),
                new StrategyItem(SelectedTradingStrategy.Aroon, "Aroon", "Aroon oscillator strategy"),
                new StrategyItem(SelectedTradingStrategy.HullSMA, "Hull SMA", "Hull moving average system"),
                new StrategyItem(SelectedTradingStrategy.SMAExpansion, "SMA Expansion", "Multi-SMA expansion / 200 turn"), 
                new StrategyItem(SelectedTradingStrategy.SimpleSMA375, "Simple SMA 375", "Regime shift via long SMA crossover"),
                new StrategyItem(SelectedTradingStrategy.CDVReversalWithEMA, "CDV Reversal + EMA50", "CDV divergence reversal validated by EMA50 crossover"),
                new StrategyItem(SelectedTradingStrategy.BollingerNoSqueeze, "Bollinger No Squeeze", "Breaking out of Bollinger Bands without squeeze condition"),
                new StrategyItem(SelectedTradingStrategy.SupportResistance, "Support Resistance Break", "Breaking out and retesting pivots"),
                new StrategyItem(SelectedTradingStrategy.EmaCrossoverVolume, "EMA25/50 + Volume", "EMA 25/50 crossover confirmed by 20-period volume SMA"),
                new StrategyItem(SelectedTradingStrategy.DEMASuperTrend, "DEMA Supertrend", "DEMA Supertrend strategy"),
                new StrategyItem(SelectedTradingStrategy.HarmonicPattern, "Harmonic Pattern", "Detects harmonic patterns like Gartley, Butterfly, Bat")           
            });


            // Load and watch per-strategy insights (generated by MultiTesterWindow) and apply as tooltips
            LoadAndApplyStrategyInsights();
            StartStrategyInsightsWatcher();

            // Apply initial enable/disable state for Candle Distribution based on current operation mode
            if (OperationModeComboBox.SelectedItem is OperationMode currentMode)
            {
                bool isLive = currentMode == OperationMode.LiveRealTrading;
                StrategySelector.SetStrategyEnabled(
                    SelectedTradingStrategy.CandleDistributionReversal,
                    isLive,
                    isLive ? null : "Real-only strategy (uses order book data). Switch to Live Real to enable."
                );

                // SMA Expansion re-enabled per user request; no tooltip restriction.
            }

            TradeDirectionComboBox.ItemsSource = Enum.GetValues(typeof(SelectedTradeDirection));
            TradeDirectionComboBox.SelectedIndex = 0; // Default to first option

            // TimeFrame
            var timeFrames = new List<TimeFrameItem>
            {
                new TimeFrameItem { Display = "1 Minute", Value = "1m" },
                new TimeFrameItem { Display = "5 Minutes", Value = "5m" },
                new TimeFrameItem { Display = "15 Minutes", Value = "15m" },
                new TimeFrameItem { Display = "30 Minutes", Value = "30m" },
                new TimeFrameItem { Display = "1 Hour", Value = "1h" },
                new TimeFrameItem { Display = "4 Hours", Value = "4h" }
            };
            TimeFrameComboBox.ItemsSource = timeFrames;
            TimeFrameComboBox.SelectedIndex = 1; // Default to 5m
        }

        private void LoadAndApplyStrategyInsights()
        {
            try
            {
                // Prefer the app-local results folder, but also search the repository (test runner may write in other projects)
                var appInsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "results", "multi", "strategy_insights.json");
                string? insPath = null;
                if (File.Exists(appInsPath)) insPath = appInsPath;
                else
                {
                    // Try to locate repo root and find any strategy_insights.json under results/multi
                    try
                    {
                        var cur = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
                        string? repoRoot = null;
                        for (int i = 0; i < 8 && cur != null; i++)
                        {
                            var sln = Path.Combine(cur.FullName, "BinanceAPI.sln");
                            if (File.Exists(sln)) { repoRoot = cur.FullName; break; }
                            cur = cur.Parent;
                        }
                        if (!string.IsNullOrEmpty(repoRoot))
                        {
                            // Prefer files under results/multi to avoid other noise
                            var candidates = Directory.GetFiles(repoRoot, "strategy_insights.json", SearchOption.AllDirectories)
                                .Where(p => p.Replace('/', '\\').Contains("\\results\\multi\\") || p.Replace('/', '\\').Contains("\\Tools\\results\\multi\\"))
                                .ToList();
                            if (candidates.Count == 0)
                            {
                                // fallback to any strategy_insights.json
                                candidates = Directory.GetFiles(repoRoot, "strategy_insights.json", SearchOption.AllDirectories).ToList();
                            }
                            if (candidates.Count > 0) insPath = candidates[0];
                        }
                    }
                    catch { /* ignore search failures */ }
                }

                if (string.IsNullOrEmpty(insPath) || !File.Exists(insPath)) return;

                var map = Newtonsoft.Json.JsonConvert.DeserializeObject<System.Collections.Generic.Dictionary<string, string>>(File.ReadAllText(insPath));
                if (map == null) return;
                foreach (var kv in map)
                {
                    if (Enum.TryParse<SelectedTradingStrategy>(kv.Key, true, out var parsed))
                    {
                        StrategySelector.SetStrategyInsight(parsed, kv.Value);
                    }
                    else
                    {
                        StrategySelector.SetStrategyInsightByName(kv.Key, kv.Value);
                    }
                }
            }
            catch { /* non-fatal */ }
        }

        private void StartStrategyInsightsWatcher()
        {
            try
            {
                // Try to place watcher at repository root (so we catch results written by test tools in other projects)
                string? repoRoot = null;
                try
                {
                    var cur = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
                    for (int i = 0; i < 6 && cur != null; i++)
                    {
                        var sln = Path.Combine(cur.FullName, "BinanceAPI.sln");
                        if (File.Exists(sln)) { repoRoot = cur.FullName; break; }
                        cur = cur.Parent;
                    }
                }
                catch { }

                var watchDir = repoRoot ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory);
                Directory.CreateDirectory(watchDir);
                _insWatcher?.Dispose();
                _insWatcher = new FileSystemWatcher(watchDir, "strategy_insights.json")
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime
                };
                _insWatcher.Changed += OnInsightsChanged;
                _insWatcher.Created += OnInsightsChanged;
                _insWatcher.Renamed += OnInsightsChanged;
                _insWatcher.EnableRaisingEvents = true;
            }
            catch { /* non-fatal */ }
        }

        private void OnInsightsChanged(object sender, FileSystemEventArgs e)
        {
            // small delay to allow file write to complete and avoid file-lock races
            Task.Delay(150).ContinueWith(_ =>
            {
                try
                {
                    Application.Current?.Dispatcher?.Invoke(() => LoadAndApplyStrategyInsights());
                }
                catch { }
            });
        }

        private void UpdateSelection()
        {
        }

        // Apply a best-setup insight produced by MultiTesterWindow.
        // The insight is a compact string produced by the multi-tester, e.g.
        // "Best setup — TF:1h; Symbols:tier_50_54; TP:2.5; SL:2.0; Win:45% ; Net:37; Trades:12; Start:...; Candles:..."
        public void ApplyBestSetup(string strategyDisplayName, string insight)
        {
            if (string.IsNullOrWhiteSpace(insight))
            {
                Log("No insight available to apply.");
                return;
            }

            try
            {
                // Parse key:value; pairs split by ';'
                var parts = insight.Split(';').Select(p => p.Trim()).Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
                string tf = string.Empty;
                string symbolsRaw = string.Empty;
                string tpRaw = string.Empty;
                string slRaw = string.Empty;

                foreach (var p in parts)
                {
                    var kv = p.Split(':', 2);
                    if (kv.Length < 2) continue;
                    var key = kv[0].Trim();
                    var val = kv[1].Trim();
                    if (key.StartsWith("TF", StringComparison.OrdinalIgnoreCase)) tf = val;
                    else if (key.StartsWith("Symbols", StringComparison.OrdinalIgnoreCase)) symbolsRaw = val;
                    else if (key.Equals("TP", StringComparison.OrdinalIgnoreCase) || key.StartsWith("TP Mult", StringComparison.OrdinalIgnoreCase)) tpRaw = val;
                    else if (key.Equals("SL", StringComparison.OrdinalIgnoreCase) || key.StartsWith("Risk Ratio", StringComparison.OrdinalIgnoreCase)) slRaw = val;
                }

                // Apply timeframe
                if (!string.IsNullOrWhiteSpace(tf) && TimeFrameComboBox != null)
                {
                    // TimeFrameComboBox items are TimeFrameItem with Value
                    foreach (var item in TimeFrameComboBox.Items)
                    {
                        if (item is TimeFrameItem tfi && string.Equals(tfi.Value, tf, StringComparison.OrdinalIgnoreCase))
                        {
                            TimeFrameComboBox.SelectedItem = tfi;
                            break;
                        }
                    }
                }

                // Apply TP/SL to sliders where possible
                if (!string.IsNullOrWhiteSpace(tpRaw) && double.TryParse(tpRaw, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var tpVal))
                {
                    if (AtrMultiplierSlider != null)
                    {
                        AtrMultiplierSlider.Value = tpVal;
                        // refresh label text
                        AtrMultiplier_ValueChanged(AtrMultiplierSlider, new RoutedPropertyChangedEventArgs<double>(AtrMultiplierSlider.Value, AtrMultiplierSlider.Value));
                    }
                }
                if (!string.IsNullOrWhiteSpace(slRaw) && double.TryParse(slRaw, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var slVal))
                {
                    if (RiskRewardSlider != null)
                    {
                        // RiskRewardSlider shows 1:X, we'll set it to the SL value (best effort)
                        RiskRewardSlider.Value = slVal;
                        RiskReward_ValueChanged(RiskRewardSlider, new RoutedPropertyChangedEventArgs<double>(RiskRewardSlider.Value, RiskRewardSlider.Value));
                    }
                }

                // Symbols: could be a tier name optionally followed by expanded list in parentheses
                if (!string.IsNullOrWhiteSpace(symbolsRaw))
                {
                    // If it contains parentheses, extract inside; otherwise try comma-separated or single token
                    string raw = symbolsRaw;
                    if (raw.Contains('(') && raw.Contains(')'))
                    {
                        var start = raw.IndexOf('(');
                        var end = raw.LastIndexOf(')');
                        if (end > start) raw = raw.Substring(start + 1, end - start - 1);
                    }

                    var coins = raw.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
                    if (coins.Count > 0)
                    {
                        _customCoinSelection = coins;
                        Log($"Applied best setup coins ({coins.Count}) from '{strategyDisplayName}'.");
                    }
                }

                Log($"Applied best setup for {strategyDisplayName} (TF={tf}, TP={tpRaw}, SL={slRaw}).");
            }
            catch (Exception ex)
            {
                Log($"Failed to apply best setup: {ex.Message}");
            }
        }

        private void OperationMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (OperationModeComboBox.SelectedItem is OperationMode mode)
            {
                BacktestPanel.Visibility = mode == OperationMode.Backtest
                    ? Visibility.Visible
                    : Visibility.Collapsed;

                // If user switches to Backtest and no start date is set, default to one week ago at 00:00 UTC
                if (mode == OperationMode.Backtest && string.IsNullOrWhiteSpace(StartDateTextBox.Text))
                {
                    var defaultStart = DateTime.UtcNow.Date.AddDays(-7);
                    StartDateTextBox.Text = defaultStart.ToString("yyyy-MM-dd HH:mm");
                }

                // Grey out and disable Candle Distribution when not Live
                bool isLive = mode == OperationMode.LiveRealTrading;
                if (StrategySelector != null)
                {
                    StrategySelector.SetStrategyEnabled(
                        SelectedTradingStrategy.CandleDistributionReversal,
                        isLive,
                        isLive ? null : "Real-only strategy (uses order book data). Switch to Live Real to enable."
                    );
                }
            }
            //ValidateInputs();
        }

        private void AtrMultiplier_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!IsLoaded || AtrMultiplierText == null) return;
            // If PnL% mode is selected, repurpose this slider as Exit PnL% input
            bool isPnLMode = ExitModeComboBox != null && ExitModeComboBox.SelectedItem is ComboBoxItem ei &&
                             string.Equals(ei.Content?.ToString(), "PnL% Take Profit", StringComparison.OrdinalIgnoreCase);

            if (isPnLMode)
            {
                AtrMultiplierText.Text = $"{AtrMultiplierSlider.Value:F2}%";
                _exitPnLPct = (decimal)AtrMultiplierSlider.Value;
                // Do not persist PnL% to user settings (session/runtime only)
            }
            else
            {
                // Always display as ATR multiplier; in trailing mode this is Activation ATR Multiplier
                AtrMultiplierText.Text = $"{AtrMultiplierSlider.Value:F1} * ATR";

                if (_useTrailing)
                {
                    _trailingActivationPercent = (decimal)AtrMultiplierSlider.Value; // semantics: ATR multiplier
                    if (_userSettings != null)
                    {
                        _userSettings.TrailingActivationAtrMultiplier = _trailingActivationPercent;
                        _userSettings.Save();
                    }
                }
                else
                {
                    if (_userSettings != null)
                    {
                        _userSettings.TpAtrMultiplier = (decimal)AtrMultiplierSlider.Value;
                        _userSettings.Save();
                    }
                }
            }
        }

        private void RiskReward_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!IsLoaded || RiskRewardText == null) return;
            RiskRewardText.Text = $"1:{RiskRewardSlider.Value:F1}";
        }

        private void ValidateInputs(object sender, TextChangedEventArgs e)
        {
            if (!IsLoaded)
                return;

            PerformValidation();
        }

        private void ValidateInputs(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded || StrategySelector == null)
                return;

            PerformValidation();
        }

        private void PerformValidation()
        {
            bool isValid = true;

            // Validate entry size
            if (!decimal.TryParse(EntrySizeTextBox.Text, out decimal entrySize) || entrySize <= 0)
            {
                EntrySizeTextBox.BorderBrush = Brushes.Red;
                isValid = false;
            }
            else
            {
                EntrySizeTextBox.BorderBrush = new SolidColorBrush(Color.FromRgb(64, 64, 64));
            }

            // Validate at least one strategy is selected - NEW WAY
            if (StrategySelector.SelectedCount == 0) // Using SelectedCount instead of StrategiesListView
            {
                StrategySelector.BorderBrush = Brushes.Red;
                isValid = false;
            }
            else
            {
                StrategySelector.BorderBrush = new SolidColorBrush(Color.FromRgb(64, 64, 64));
            }

            if ((OperationMode)OperationModeComboBox.SelectedItem == OperationMode.Backtest)
            {
                if (!string.IsNullOrEmpty(StartDateTextBox.Text) &&
                    !DateTime.TryParse(StartDateTextBox.Text, out _))
                {
                    StartDateTextBox.BorderBrush = Brushes.Red;
                    isValid = false;
                }
                if (!string.IsNullOrEmpty(EndDateTextBox.Text) &&
                    !DateTime.TryParse(EndDateTextBox.Text, out _))
                {
                    EndDateTextBox.BorderBrush = Brushes.Red;
                    isValid = false;
                }

            }

            StartButton.IsEnabled = isValid;
        }

        private async void StartButton_Click(object sender, RoutedEventArgs e)
        {
            lock (_startLock)
            {
                if (_isStarting || _tradingService.IsRunning)
                {
                    Log("Start operation already in progress");
                    return;
                }
                _isStarting = true;
            }

            try
            {
                StartButton.IsEnabled = false;
                StopButton.IsEnabled = true;
                // Clear the log for a fresh session
                LogText.Clear();
                StatusText.Text = string.Empty;
                Log("Attempting to start trading...");
                // Clear previous trades when starting new session
                _recentTradesVm.Clear();
                // Clear the main trading log textbox so the new session starts fresh
                LogText.Clear();
        

                var selectedStrategies = StrategySelector.SelectedStrategies.ToList();

                // Get other parameters
                var operationMode = (OperationMode)OperationModeComboBox.SelectedItem;
                var tradeDirection = (SelectedTradeDirection)TradeDirectionComboBox.SelectedItem;
                var timeFrame = ((TimeFrameItem)TimeFrameComboBox.SelectedItem).Value;
                decimal entrySize = decimal.Parse(EntrySizeTextBox.Text);
                decimal leverage = decimal.Parse(LeverageTextBox.Text);
                decimal atrMultiplier = (decimal)AtrMultiplierSlider.Value;
                decimal riskReward = (decimal)RiskRewardSlider.Value;

                DateTime? startDate = null, endDate = null;
                if (operationMode == OperationMode.Backtest)
                {
                    var dates = GetBacktestDates();
                    startDate = dates.startDate;
                    endDate = dates.endDate;

                    Log($"Backtest period: {startDate:yyyy-MM-dd} to {endDate:yyyy-MM-dd}");
                }

                _tradingService.SetRecentTradesViewModel(_recentTradesVm);        
                // Pass the Paper Wallet VM to the service (paper mode only used)
                _tradingService.SetPaperWalletViewModel(_paperWalletVm);
                // Pass trailing config from UI
                _tradingService.SetTrailingUiConfig(_useTrailing, _trailingActivationPercent, _trailingCallbackPercent);
                // Pass exit mode config (runtime only)
                string exitModeName;
                decimal? exitPctToPass = null;
                if (ExitModeComboBox.SelectedItem is ComboBoxItem exitItem)
                {
                    var exitContent = exitItem.Content?.ToString() ?? "Take Profit";
                    if (string.Equals(exitContent, "PnL% Take Profit", StringComparison.OrdinalIgnoreCase))
                    {
                        exitModeName = "PnLPct";
                        // Use runtime percent from slider
                        exitPctToPass = _exitPnLPct ?? (decimal?)AtrMultiplierSlider.Value;
                    }
                    else if (string.Equals(exitContent, "Trailing Stop", StringComparison.OrdinalIgnoreCase))
                    {
                        exitModeName = "TrailingStop";
                    }
                    else
                    {
                        exitModeName = "TakeProfit";
                    }
                }
                else
                {
                    exitModeName = "TakeProfit";
                }
                _tradingService.SetExitModeConfig(exitModeName, exitPctToPass);
                
                // PASS THE CUSTOM COIN SELECTION
                Log(_customCoinSelection != null 
                    ? $"Using custom coin selection: {_customCoinSelection.Count} coins"
                    : "Using auto coin selection");

                // Start trading with custom coin selection
                await _tradingService.StartTrading(
                    (OperationMode)OperationModeComboBox.SelectedItem,
                    (SelectedTradeDirection)TradeDirectionComboBox.SelectedItem,
                    selectedStrategies,
                    timeFrame,
                    entrySize,
                    leverage, 
                    atrMultiplier,
                    riskReward,
                    startDate,
                    endDate,
                    _customCoinSelection  
                );

                // Keep custom selection for subsequent runs unless the user changes it
                // _customCoinSelection = null; // removed so selection persists across runs



                Log("Trading started successfully");
            }
            catch (Exception ex)
            {
                Log($"Start failed: {ex.Message}");
            }
            finally
            {
                lock (_startLock)
                {
                    _isStarting = false;
                }

                StartButton.IsEnabled = !_tradingService.IsRunning;
                StopButton.IsEnabled = _tradingService.IsRunning;
            }

            Log($"Service state: Running={_tradingService.IsRunning}");

        }

        private void ExitModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded) return;
            if (ExitModeComboBox.SelectedItem is ComboBoxItem item)
            {
                var mode = item.Content?.ToString();
                _useTrailing = string.Equals(mode, "Trailing Stop", StringComparison.OrdinalIgnoreCase);
                // Show PnL% UI: repurpose ATR slider label/display instead of separate textbox
                var isPnLMode = string.Equals(mode, "PnL% Take Profit", StringComparison.OrdinalIgnoreCase);
                if (ExitPnLPctTextBox != null)
                {
                    ExitPnLPctTextBox.Visibility = Visibility.Collapsed; // we don't use the textbox when repurposing the slider
                }
                // Update the ATR label to reflect current mode immediately
                ExitParamLabel.Content = isPnLMode ? "Exit PnL%:" : (_useTrailing ? "Activation ATR Multiplier:" : "ATR Multiplier:");
                // If switching into PnL mode, set _exitPnLPct from current slider value
                if (isPnLMode)
                {
                    _exitPnLPct = (decimal)AtrMultiplierSlider.Value;
                    AtrMultiplierText.Text = $"{AtrMultiplierSlider.Value:F2}%";
                    // Optionally clamp slider range for percent; leave slider min/max as-is unless you want changes
                }
                // Restore full label text for clarity
                var vis = _useTrailing ? Visibility.Visible : Visibility.Collapsed;
                if (CallbackLabel != null) CallbackLabel.Visibility = vis;
                if (CallbackSlider != null) CallbackSlider.Visibility = vis;
                if (CallbackText != null) CallbackText.Visibility = vis;
                // Refresh label text to reflect current mode
                AtrMultiplier_ValueChanged(AtrMultiplierSlider, new RoutedPropertyChangedEventArgs<double>(AtrMultiplierSlider.Value, AtrMultiplierSlider.Value));

                // Persist exit mode selection (only store TrailingStop vs TakeProfit for legacy settings)
                if (_userSettings != null)
                {
                    _userSettings.ExitMode = _useTrailing ? "TrailingStop" : "TakeProfit";
                    _userSettings.Save();
                }
            }
        }

        private void CallbackSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!IsLoaded || CallbackText == null) return;
            _trailingCallbackPercent = (decimal)CallbackSlider.Value;
            CallbackText.Text = $"{CallbackSlider.Value:F1}%";
            if (_userSettings != null)
            {
                _userSettings.TrailingCallbackPercent = _trailingCallbackPercent;
                _userSettings.Save();
            }
        }

        private void ExitPnLPctTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!IsLoaded) return;
            if (ExitPnLPctTextBox == null) return;
            if (decimal.TryParse(ExitPnLPctTextBox.Text, out var pct) && pct > 0)
            {
                _exitPnLPct = pct;
            }
            else
            {
                _exitPnLPct = null;
            }
        }

        private async void StopButton_Click(object sender, RoutedEventArgs e)
        {
            Log("Stopping trading...");
            try
            {
                bool closeAll = OperationModeComboBox.SelectedItem is not OperationMode.LiveRealTrading;
                StopButton.IsEnabled = false;

                // Use the async stop to avoid blocking the UI thread
                await _tradingService.StopTradingAsync(closeAll);

                Log(closeAll ? "Closed all trades" : "Stopped new trades (positions remain open)");
            }
            catch (Exception ex)
            {
                Log($"Stop failed: {ex.Message}");
            }
            finally
            {
                StartButton.IsEnabled = true;
                StopButton.IsEnabled = _tradingService.IsRunning;
            }
        }

        public void SetTradingService(BinanceTradingService service)
        {
            _tradingService = service;
        }

        private async void TestConnectionButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                TestConnectionButton.Content = "Testing...";
                TestConnectionButton.IsEnabled = false;

                SetTradingService(_tradingService);

                var success = await _tradingService.TestConnection();
                TestConnectionButton.Content = success ? "✓ Connected" : "✗ Failed";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"CRASH PREVENTED: {ex}");
                TestConnectionButton.Content = "Error (see debug)";
            }
            finally
            {
                TestConnectionButton.IsEnabled = true;
            }
        }

        private void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            if (((App)Application.Current).TradingService.IsRunning)
            {
                var result = MessageBox.Show(
                    "Trading is currently active. Do you want to close all positions and exit?",
                    "Confirm Exit",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    ((App)Application.Current).TradingService.StopTrading(true);
                    Log("Closed all trades - application exiting");
                }
                else
                {
                    e.Cancel = true;
                    Log("Exit canceled by user");
                }
            }
        }

        private void LogText_TextChanged(object sender, TextChangedEventArgs e)
        {
            LogText.ScrollToEnd();
        }

        public void InitializeTradingParameters(
            OperationMode operationMode,
            SelectedTradeDirection tradeDirection,
            string interval,
            decimal entrySize,
            decimal leverage,
            decimal takeProfit)
        {

            if (!IsLoaded) return;
            // Set UI controls
            OperationModeComboBox.SelectedItem = operationMode;
            TradeDirectionComboBox.SelectedItem = tradeDirection;
            EntrySizeTextBox.Text = entrySize.ToString();
            LeverageTextBox.Text = leverage.ToString();

            if (AtrMultiplierText != null && AtrMultiplierSlider != null)
            {
                AtrMultiplierSlider.Value = (double)takeProfit;
                AtrMultiplierText.Text = $"{takeProfit:F1} * ATR";
            }


            Log("Application initialized with default parameters");
        }

        private void ApplyUserSettingsToUi()
        {
            if (_userSettings == null) return;

            // Exit mode
            if (ExitModeComboBox != null && ExitModeComboBox.Items.Count >= 2)
            {
                int index = string.Equals(_userSettings.ExitMode, "TrailingStop", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
                ExitModeComboBox.SelectedIndex = index;
                _useTrailing = index == 1;
            }

            // Sliders
            if (_useTrailing)
            {
                _trailingActivationPercent = _userSettings.TrailingActivationAtrMultiplier;
                if (AtrMultiplierSlider != null)
                    AtrMultiplierSlider.Value = (double)_trailingActivationPercent;
            }
            else
            {
                if (AtrMultiplierSlider != null)
                    AtrMultiplierSlider.Value = (double)_userSettings.TpAtrMultiplier;
            }

            if (CallbackSlider != null)
                CallbackSlider.Value = (double)_userSettings.TrailingCallbackPercent;
            // Closed candles checkbox will be set in Loaded once visual tree exists
        }

        // private List<StrategyItem> GetDefaultStrategies()
        // {
        //     return new List<StrategyItem>
        //     {
        //         new StrategyItem(SelectedTradingStrategy.EmaStochRsi, "EMA + Stoch RSI", "Combines EMA crossover with Stochastic RSI"),
        //         // ... all other strategies ...
        //     };
        // }

        protected override void OnClosed(EventArgs e)
        {
            // Force-stop any running trading operations
            var tradingService = ((App)Application.Current).TradingService;
            tradingService?.StopTrading(closeAllTrades: true);

            // Ensure full application shutdown
            Application.Current.Shutdown();
            base.OnClosed(e);
        }

        // Custom writer for UI output (updated for TextBox)
        private class TextBoxWriter : TextWriter
        {
            private readonly TextBox _output;
            public TextBoxWriter(TextBox output) => _output = output;
            public override Encoding Encoding => Encoding.UTF8;

            public override void WriteLine(string? value)
            {
                _output.Dispatcher.Invoke(() =>
                {
                    _output.AppendText((value ?? string.Empty) + Environment.NewLine);
                    _output.ScrollToEnd();
                });
            }
        }

        public void Log(string message)
        {
            if (message == null) return;
            // Ensure this runs on the UI thread
            Dispatcher.Invoke(() =>
            {
                // Append to log text block
                LogText.Text += $"{DateTime.Now:T} - {message}\n";
                LogText.ScrollToEnd();
                StatusText.Text = message;

                // Auto-scroll to bottom
                // var scrollViewer = FindVisualParent<ScrollViewer>(LogText);
                // scrollViewer?.ScrollToEnd();
            });
        }

        private (DateTime startDate, DateTime endDate) GetBacktestDates()
        {
            DateTime startDate;
            DateTime endDate;

            // Parse or default start date
            if (DateTime.TryParse(StartDateTextBox.Text, out startDate))
            {
                startDate = startDate.ToUniversalTime();
            }
            else
            {
                startDate = DateTime.UtcNow.AddDays(-7); // Default: 1 week ago
            }

            var timeFrame = ((TimeFrameItem)TimeFrameComboBox.SelectedItem).Value;

            // Parse or calculate end date based on candles
            if (DateTime.TryParse(EndDateTextBox.Text, out endDate))
            {
                endDate = endDate.ToUniversalTime();
            }
            else
            {
                int candleCount = 900; // Your specified default
                endDate = CalculateEndDate(startDate, timeFrame, candleCount);
            }

            // Ensure we don't exceed exchange limits (1000 candles)
            if ((endDate - startDate).TotalDays > GetMaxDaysForTimeFrame(timeFrame))
            {
                endDate = startDate.AddDays(GetMaxDaysForTimeFrame(timeFrame));
                Log($"Adjusted end date to stay within 1000 candle limit");
            }

            return (startDate, endDate);
        }

        private DateTime CalculateEndDate(DateTime startDate, string timeFrame, int candleCount)
        {
            TimeSpan interval = timeFrame switch
            {
                "1m" => TimeSpan.FromMinutes(1),
                "5m" => TimeSpan.FromMinutes(5),
                "15m" => TimeSpan.FromMinutes(15),
                "30m" => TimeSpan.FromMinutes(30),
                "1h" => TimeSpan.FromHours(1),
                "4h" => TimeSpan.FromHours(4),
                _ => TimeSpan.FromHours(1)
            };

            return startDate.Add(interval * candleCount);
        }

        private double GetMaxDaysForTimeFrame(string timeFrame)
        {
            // Binance typically limits to 1000 candles
            return timeFrame switch
            {
                "1m" => 1000.0 / (1440),    // 1000 minutes
                "5m" => 1000.0 / (288),     // 1000*5 minutes
                "15m" => 1000.0 / (96),     // 1000*15 minutes
                "30m" => 1000.0 / (48),     // 1000*30 minutes
                "1h" => 1000.0 / 24,        // 1000 hours
                "4h" => 1000.0 / 6,         // 1000*4 hours
                _ => 30                     // Fallback (days)
            };
        }

        private void StartDateTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            var raw = StartDateTextBox.Text?.Trim();
            if (string.IsNullOrEmpty(raw)) return;

            if (DateTime.TryParse(raw, out DateTime date))
            {
                // If user did not provide time (no ':'), normalize to 00:00
                if (!raw.Contains(":")) date = date.Date;
                StartDateTextBox.Text = date.ToString("yyyy-MM-dd HH:mm");
            }
        }

        private void EndDateTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            var raw = EndDateTextBox.Text?.Trim();
            if (string.IsNullOrEmpty(raw)) return;

            if (DateTime.TryParse(raw, out DateTime date))
            {
                if (!raw.Contains(":")) date = date.Date;
                EndDateTextBox.Text = date.ToString("yyyy-MM-dd HH:mm");
            }
        }

        private void OnStrategySelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // This will now work with the control's built-in UpdateSelection
            UpdateSelection();

            // Additional logic if needed:
            StatusText.Text = $"{StrategySelector.SelectedCount} strategies selected";

            // If FVG strategy is selected we force closed-candle mode off and disable the checkbox
            try
            {
                bool fvgSelected = StrategySelector.SelectedStrategies?.Contains(SelectedTradingStrategy.FVG) == true;
                if (UseClosedCandlesCheckBox != null)
                {
                    if (fvgSelected)
                    {
                        UseClosedCandlesCheckBox.IsChecked = false;
                        UseClosedCandlesCheckBox.IsEnabled = false;
                        _useClosedCandles = false;
                        BinanceTestnet.Strategies.Helpers.StrategyRuntimeConfig.UseClosedCandles = false;
                        if (_userSettings != null)
                        {
                            _userSettings.UseClosedCandles = false;
                            _userSettings.Save();
                        }
                    }
                    else
                    {
                        UseClosedCandlesCheckBox.IsEnabled = true;
                        // Restore to persisted or current runtime value
                        UseClosedCandlesCheckBox.IsChecked = _userSettings?.UseClosedCandles ?? _useClosedCandles;
                    }
                }
            }
            catch
            {
                // non-fatal UI update; ignore any failures
            }
        }

        // UI handler: toggle closed candle mode
        private void UseClosedCandlesCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            _useClosedCandles = UseClosedCandlesCheckBox.IsChecked == true;
            // Persist setting
            if (_userSettings != null)
            {
                _userSettings.UseClosedCandles = _useClosedCandles;
                _userSettings.Save();
            }
            // Apply to runtime config read by strategies
            BinanceTestnet.Strategies.Helpers.StrategyRuntimeConfig.UseClosedCandles = _useClosedCandles;
            Log($"Candle policy: {(_useClosedCandles ? "Closed-only" : "Forming allowed")}");
        }

        private void PreFlightButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var win = new PreFlightMarketCheckWindow();
                win.Owner = this;
                win.Show();
            }
            catch (Exception ex)
            {
                Log($"Failed to open Pre-Flight window: {ex.Message}");
            }
        }

        

        // Helper to find parent controls
        public static T FindVisualParent<T>(DependencyObject child) where T : DependencyObject
        {
            while (child != null && child is not T)
                child = VisualTreeHelper.GetParent(child);
            return child as T;
        }

        // Add this method to your MainWindow class
        private void CoinSelectionButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string databasePath = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "TradingData.db"
                );

                var coinSelectionWindow = new CoinSelectionWindow(databasePath);
                coinSelectionWindow.Owner = this;
                coinSelectionWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;

                // SUBSCRIBE TO THE COINS UPDATED EVENT
                coinSelectionWindow.OnCoinsUpdated += (selectedCoins) =>
                {
                    _customCoinSelection = selectedCoins;
                    Log($"Coin selection updated: {selectedCoins.Count} coins saved for next session");
                };

                coinSelectionWindow.ShowDialog();

                Log("Coin selection updated - new symbols will be used in next trading cycle");
            }
            catch (Exception ex)
            {
                Log($"Error opening coin selection: {ex.Message}");
                MessageBox.Show($"Could not open coin selection: {ex.Message}", "Error",
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void MultiTesterButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var win = new MultiTesterWindow();
                win.Owner = this;
                win.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                win.Show();
                Log("Opened Multi Tester window");
            }
            catch (Exception ex)
            {
                Log($"Error opening multi tester: {ex.Message}");
                MessageBox.Show($"Could not open multi tester: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
    
    public class StrategyComboBoxItem
    {
        public SelectedTradingStrategy Strategy { get; set; }
        public string DisplayName { get; set; }

        public StrategyComboBoxItem(SelectedTradingStrategy strategy, string displayName)
        {
            Strategy = strategy;
            DisplayName = displayName;
        }
    }

    public class TimeFrameItem
    {
        public string Display { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }
}