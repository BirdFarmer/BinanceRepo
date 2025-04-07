using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using BinanceTestnet.Enums; 
using BinanceTestnet.Database;
using BinanceTestnet.Trading;
using BinanceLive.Strategies;
using BinanceTestnet.Models;
using TradingAppDesktop;
using TradingAppDesktop.Services;
using System.ComponentModel;
using System.IO;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;


namespace TradingAppDesktop
{
    public partial class MainWindow : Window
    {
        private BinanceTradingService _tradingService;
        private bool _isStarting = false; // Add this class field
        private readonly object _startLock = new(); // Add this for thread safety

        public MainWindow()
        {
            InitializeComponent();
            
            // UI initialization only
            Console.SetOut(new TextBoxWriter(LogText));
            InitializeComboBoxes();
            
            StartButton.Click += StartButton_Click;
            StopButton.Click += StopButton_Click;
        }

        private void InitializeComboBoxes()
        {
            // Simple version - works with 0-based enum
            OperationModeComboBox.ItemsSource = Enum.GetValues(typeof(OperationMode));
            TradeDirectionComboBox.ItemsSource = Enum.GetValues(typeof(SelectedTradeDirection));
            TradingStrategyComboBox.ItemsSource = Enum.GetValues(typeof(SelectedTradingStrategy));
            
            // Set default selections
            OperationModeComboBox.SelectedIndex = 0; // Paper Trading
            TradeDirectionComboBox.SelectedIndex = 0;
            TradingStrategyComboBox.SelectedIndex = 0;
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

                // Get selections
                var operationMode = (OperationMode)OperationModeComboBox.SelectedItem;
                var tradeDirection = (SelectedTradeDirection)TradeDirectionComboBox.SelectedItem;
                var selectedStrategy = (SelectedTradingStrategy)TradingStrategyComboBox.SelectedItem;

                // Parse inputs
                decimal entrySize = decimal.Parse(EntrySizeTextBox.Text);
                decimal leverage = decimal.Parse(LeverageTextBox.Text);
                decimal takeProfit = decimal.Parse(TakeProfitTextBox.Text);

                // Make the service call
                await _tradingService.StartTrading(
                    operationMode,
                    tradeDirection,
                    selectedStrategy,
                    "5m", 
                    entrySize, 
                    leverage, 
                    takeProfit
                );
                
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
            SelectedTradingStrategy strategy,
            string interval,
            decimal entrySize,
            decimal leverage,
            decimal takeProfit)
        {
            // Set UI controls
            OperationModeComboBox.SelectedItem = operationMode;
            TradeDirectionComboBox.SelectedItem = tradeDirection;
            TradingStrategyComboBox.SelectedItem = strategy;
            EntrySizeTextBox.Text = entrySize.ToString();
            LeverageTextBox.Text = leverage.ToString();
            TakeProfitTextBox.Text = takeProfit.ToString();
            
            Log("Application initialized with default parameters");
        }

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

        // Helper to find parent controls
        public static T FindVisualParent<T>(DependencyObject child) where T : DependencyObject
        {
            while (child != null && child is not T)
                child = VisualTreeHelper.GetParent(child);
            return child as T;
        }
    }
}