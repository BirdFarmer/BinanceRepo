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
        private BinanceTradingService _tradingService;
        private bool _isStarting = false; // Add this class field
        private readonly object _startLock = new(); // Add this for thread safety
        private List<string> _customCoinSelection = null;
        private RecentTradesViewModel _recentTradesVm;
    


        public MainWindow()
        {
            InitializeComponent();
        
            // Initialize Recent Trades ViewModel
            _recentTradesVm = new RecentTradesViewModel();
            RecentTradesList.DataContext = _recentTradesVm;        

            // One-time initialization that doesn't need loaded controls
            Console.SetOut(new TextBoxWriter(LogText));
            StartButton.Click += StartButton_Click;
            StopButton.Click += StopButton_Click;

            // UI-dependent initialization
            this.Loaded += (s, e) =>
            {
                InitializeComboBoxes();
                AtrMultiplierText.Text = $"{AtrMultiplierSlider.Value:F1} (TP: +{AtrMultiplierSlider.Value:F1}ATR)";
                RiskRewardText.Text = $"1:{RiskRewardSlider.Value:F1}";
            };
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
                //new StrategyItem(SelectedTradingStrategy.SMAExpansion, "SMA Expansion", "3 SMAs expanding, trade reversal"), 
                new StrategyItem(SelectedTradingStrategy.BollingerSqueeze, "Bollinger Squeeze", "Breaking out of Bollinger Bands squeeze"),
                new StrategyItem(SelectedTradingStrategy.SupportResistance, "Support Resistance Break", "Breaking out and retesting pivots")
            });

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

        private void UpdateSelection()
        {
        }

        private void OperationMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (OperationModeComboBox.SelectedItem is OperationMode mode)
            {
                BacktestPanel.Visibility = mode == OperationMode.Backtest
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
            //ValidateInputs();
        }

        private void AtrMultiplier_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!IsLoaded || AtrMultiplierText == null) return;
            AtrMultiplierText.Text = $"{AtrMultiplierSlider.Value:F1} (TP: +{AtrMultiplierSlider.Value:F1}ATR)";
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
                Log("Attempting to start trading...");
                // Clear previous trades when starting new session
                _recentTradesVm.Clear();
        

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

        private async void StopButton_Click(object sender, RoutedEventArgs e)
        {
            Log("Stopping trading...");
            try
            {
                bool closeAll = OperationModeComboBox.SelectedItem is not OperationMode.LiveRealTrading;
                StopButton.IsEnabled = false;

                await Task.Run(() => _tradingService.StopTrading(closeAll));

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
                AtrMultiplierText.Text = $"{takeProfit:F1} (TP: +{takeProfit:F1}ATR)";
            }


            Log("Application initialized with default parameters");
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

            public override void WriteLine(string value)
            {
                _output.Dispatcher.Invoke(() =>
                {
                    _output.AppendText(value + Environment.NewLine);
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
            if (DateTime.TryParse(StartDateTextBox.Text, out DateTime date))
            {
                StartDateTextBox.Text = date.ToString("yyyy-MM-dd HH:mm");
            }
        }

        private void EndDateTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (DateTime.TryParse(EndDateTextBox.Text, out DateTime date))
            {
                EndDateTextBox.Text = date.ToString("yyyy-MM-dd HH:mm");
            }
        }

        private void OnStrategySelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // This will now work with the control's built-in UpdateSelection
            UpdateSelection();

            // Additional logic if needed:
            StatusText.Text = $"{StrategySelector.SelectedCount} strategies selected";
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
        public string Display { get; set; }
        public string Value { get; set; }
    }
}