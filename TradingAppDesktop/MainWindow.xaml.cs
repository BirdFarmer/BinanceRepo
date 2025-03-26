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

namespace TradingAppDesktop
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            
            // Redirect console output to UI
            Console.SetOut(new TextBoxWriter(DebugOutput));
            
            Console.WriteLine($"Window created at {DateTime.Now:T}");

            // Bind enums properly
            OperationModeComboBox.ItemsSource = Enum.GetValues(typeof(OperationMode));
            TradeDirectionComboBox.ItemsSource = Enum.GetValues(typeof(SelectedTradeDirection));
            TradingStrategyComboBox.ItemsSource = Enum.GetValues(typeof(SelectedTradingStrategy));
            
            // Set default selections
            OperationModeComboBox.SelectedIndex = 0;
            TradeDirectionComboBox.SelectedIndex = 0;
            TradingStrategyComboBox.SelectedIndex = 0;

            StartButton.Click += StartButton_Click;
            StopButton.Click += StopButton_Click;
        }

        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            Log("Attempting to start trading...");
            try 
            {
                // Get user inputs from the UI
                var operationMode = (OperationMode)OperationModeComboBox.SelectedIndex;
                var tradeDirection = (SelectedTradeDirection)TradeDirectionComboBox.SelectedIndex;
                var selectedStrategy = (SelectedTradingStrategy)TradingStrategyComboBox.SelectedIndex;

                // Pass the enums to BinanceTradingService
                var tradingService = ((App)Application.Current).TradingService;
                tradingService.StartTrading(
                    operationMode: operationMode,
                    tradeDirection: tradeDirection,
                    selectedStrategy: selectedStrategy,
                    interval: "5m",
                    entrySize: 20m,
                    leverage: 15m,
                    takeProfit: 5m
                );
                StartButton.IsEnabled = false;
                StopButton.IsEnabled = true;
                Log("Trading started successfully");
            }
            catch (Exception ex)
            {
                Log($"Start failed: {ex.Message}");
            }
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            Log("Stopping trading...");
            try
            {
                bool closeAll = OperationModeComboBox.SelectedItem is not OperationMode.LiveRealTrading;
                ((App)Application.Current).TradingService.StopTrading(closeAll);
                StopButton.IsEnabled = false;
                StartButton.IsEnabled = true;
                Log(closeAll ? "Closed all trades" : "Stopped new trades (positions remain open)");
            }
            catch (Exception ex)
            {
                Log($"Stop failed: {ex.Message}");
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

            // Custom writer for UI output
        private class TextBoxWriter : TextWriter
        {
            private readonly TextBlock _output;
            public TextBoxWriter(TextBlock output) => _output = output;
            public override Encoding Encoding => Encoding.UTF8;

            public override void WriteLine(string value)
            {
                _output.Dispatcher.Invoke(() => 
                {
                    _output.Text += value + Environment.NewLine;
                    // Auto-scroll
                    var scrollViewer = FindVisualParent<ScrollViewer>(_output);
                    scrollViewer?.ScrollToEnd();
                });
            }
        }

        public void Log(string message)
        {
            // Ensure this runs on the UI thread
            Dispatcher.Invoke(() =>
            {
                // Append to log text block
                LogText.Text += $"{DateTime.Now:T} - {message}\n";
                StatusText.Text = message;
                
                // Auto-scroll to bottom
                var scrollViewer = FindVisualParent<ScrollViewer>(LogText);
                scrollViewer?.ScrollToEnd();
            });
        }

        // Helper to find parent controls
        private static T FindVisualParent<T>(DependencyObject child) where T : DependencyObject
        {
            while (child != null && child is not T)
                child = VisualTreeHelper.GetParent(child);
            return child as T;
        }
    }
}