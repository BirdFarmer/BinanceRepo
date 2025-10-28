using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using BinanceTestnet.Database;

namespace TradingAppDesktop.Views
{
    public partial class CoinSelectionWindow : Window
    {
        private DatabaseManager _databaseManager;
        private List<string> _currentSelectedCoins = new List<string>();
        
        // Event to notify main window when coins are updated
        public event Action<List<string>> OnCoinsUpdated;

        public CoinSelectionWindow(string databasePath)
        {
            InitializeComponent();
            
            // Initialize database manager with your existing path
            _databaseManager = new DatabaseManager(databasePath);
            
            InitializeUI();
            RefreshDatabaseInfo();
            LoadCurrentSelection();
        }

        private void InitializeUI()
        {
            // Set up initial UI state
            AutoSelectionMethod.SelectedIndex = 3; // Default to Composite Score
            UpdateSelectionSummary();
        }

        private void LoadCurrentSelection()
        {
            try
            {
                // Load the most recent coin pair list from database
                var currentCoins = _databaseManager.GetClosestCoinPairList(DateTime.UtcNow);
                if (currentCoins.Any())
                {
                    _currentSelectedCoins = currentCoins;
                    UpdateSelectedCoinsList();
                    SelectionMethodText.Text = "Method: Previously Saved";
                    StatusText.Text = $"Loaded {currentCoins.Count} previously selected coins";
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Error loading previous selection: {ex.Message}";
            }
        }

        private void RefreshDatabaseInfo()
        {
            try
            {
                // Get some basic database stats using your existing methods
                var topCoins = _databaseManager.GetTopCoinPairsByVolume(5);
                var totalSymbols = _databaseManager.GetBiggestCoins(1000).Count; // Get count of all symbols
                
                DatabaseInfoText.Text = $"Database: {totalSymbols} symbols tracked\nTop 5 by volume: {string.Join(", ", topCoins)}";
                StatusText.Text = "Database connected successfully";
            }
            catch (Exception ex)
            {
                DatabaseInfoText.Text = $"Database error: {ex.Message}";
                StatusText.Text = "Database connection failed";
            }
        }

        // Auto-selection methods using your DatabaseManager
        private void GenerateAutoButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!int.TryParse(LimitTextBox.Text, out int limit) || limit <= 0)
                {
                    MessageBox.Show("Please enter a valid limit number", "Invalid Input", 
                                  MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                List<string> selectedCoins = new List<string>();
                string method = "";

                switch (AutoSelectionMethod.SelectedIndex)
                {
                    case 0: // Top by Volume
                        selectedCoins = _databaseManager.GetTopCoinPairsByVolume(limit);
                        method = $"Top {limit} by Volume";
                        AutoSelectionInfo.Text = "Selects coins with highest trading volume (liquidity)";
                        break;
                    case 1: // Top by Price Change
                        selectedCoins = _databaseManager.GetTopCoinPairsByPriceChange(limit);
                        method = $"Top {limit} by Price Change";
                        AutoSelectionInfo.Text = "Selects coins with largest price movements (volatility)";
                        break;
                    case 2: // Top by Volume Change
                        selectedCoins = _databaseManager.GetTopCoinPairsByVolumeChange(limit);
                        method = $"Top {limit} by Volume Change";
                        AutoSelectionInfo.Text = "Selects coins with biggest volume spikes (momentum)";
                        break;
                    case 3: // Composite Score
                        selectedCoins = _databaseManager.GetTopCoinPairs(limit);
                        method = $"Top {limit} by Composite Score";
                        AutoSelectionInfo.Text = "Balanced selection based on volume + price change";
                        break;
                    case 4: // Biggest Coins
                        selectedCoins = _databaseManager.GetBiggestCoins(limit);
                        method = $"Top {limit} Biggest Coins";
                        AutoSelectionInfo.Text = "High market cap, established coins";
                        break;
                }

                _currentSelectedCoins = selectedCoins;
                UpdateSelectedCoinsList();
                SelectionMethodText.Text = $"Method: {method}";
                
                StatusText.Text = $"Auto-generated {selectedCoins.Count} coins using {method}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error generating auto-selection: {ex.Message}", "Error", 
                              MessageBoxButton.OK, MessageBoxImage.Error);
                StatusText.Text = "Error generating selection";
            }
        }

        // Manual selection
        private void ApplyManualButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var manualCoins = ManualCoinsTextBox.Text
                    .Split(new[] { ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(c => c.Trim().ToUpper())
                    .Where(c => !string.IsNullOrWhiteSpace(c) && c.EndsWith("USDT"))
                    .Distinct()
                    .ToList();

                if (!manualCoins.Any())
                {
                    MessageBox.Show("Please enter valid coin pairs ending with USDT", "Invalid Input", 
                                  MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                _currentSelectedCoins = manualCoins;
                UpdateSelectedCoinsList();
                SelectionMethodText.Text = "Method: Manual Selection";
                
                StatusText.Text = $"Manual selection applied: {manualCoins.Count} coins";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error processing manual selection: {ex.Message}", "Error", 
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Apply selection to trading system
        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_currentSelectedCoins.Any())
            {
                MessageBox.Show("No coins selected to apply", "Warning",
                              MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                // Save the current selection to database
                _databaseManager.UpsertCoinPairList(_currentSelectedCoins, DateTime.UtcNow);

                // NOTIFY MAIN WINDOW ABOUT THE UPDATE
                OnCoinsUpdated?.Invoke(_currentSelectedCoins);

                MessageBox.Show($"✅ Successfully applied {_currentSelectedCoins.Count} coins!\n\n" +
                              $"🔒 This selection will be used in your NEXT trading session\n\n" +
                              $"First 10 coins: {string.Join(", ", _currentSelectedCoins.Take(10))}" +
                              (_currentSelectedCoins.Count > 10 ? "..." : ""),
                              "Selection Saved", MessageBoxButton.OK, MessageBoxImage.Information);

                StatusText.Text = $"Applied {_currentSelectedCoins.Count} coins - will be used next session";

                this.DialogResult = true;
                this.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error applying selection: {ex.Message}", "Error",
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Save current list
        private void SaveListButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_currentSelectedCoins.Any())
            {
                MessageBox.Show("No coins to save", "Warning", 
                              MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Simple save - just use the existing database method
            try
            {
                _databaseManager.UpsertCoinPairList(_currentSelectedCoins, DateTime.UtcNow);
                MessageBox.Show($"Saved {_currentSelectedCoins.Count} coins to database", 
                              "Saved", MessageBoxButton.OK, MessageBoxImage.Information);
                StatusText.Text = "Selection saved to database";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving list: {ex.Message}", "Error", 
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Reset selection
        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            _currentSelectedCoins.Clear();
            UpdateSelectedCoinsList();
            SelectionMethodText.Text = "Method: None";
            ManualCoinsTextBox.Clear();
            StatusText.Text = "Selection cleared";
        }

        // Refresh data
        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            RefreshDatabaseInfo();
            StatusText.Text = "Data refreshed";
        }

        // View database statistics
        private void ViewDatabaseStatsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var topByVolume = _databaseManager.GetTopCoinPairsByVolume(5);
                var topByPriceChange = _databaseManager.GetTopCoinPairsByPriceChange(5);
                var biggestCoins = _databaseManager.GetBiggestCoins(5);
                var totalSymbols = _databaseManager.GetBiggestCoins(1000).Count;

                string stats = $"📊 Database Statistics\n\n" +
                             $"Total Symbols: {totalSymbols}\n\n" +
                             $"Top 5 by Volume:\n{string.Join("\n", topByVolume)}\n\n" +
                             $"Top 5 by Price Change:\n{string.Join("\n", topByPriceChange)}\n\n" +
                             $"Top 5 Biggest Coins:\n{string.Join("\n", biggestCoins)}";

                MessageBox.Show(stats, "Database Statistics", 
                              MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error getting database stats: {ex.Message}", "Error", 
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Helper methods
        private void UpdateSelectedCoinsList()
        {
            SelectedCoinsListBox.Items.Clear();
            foreach (var coin in _currentSelectedCoins)
            {
                SelectedCoinsListBox.Items.Add(coin);
            }
            UpdateSelectionSummary();
        }

        private void UpdateSelectionSummary()
        {
            SelectedCountText.Text = $"Selected Coins: {_currentSelectedCoins.Count}";
            TimestampText.Text = $"Last Updated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
        }

        // Placeholder methods for saved lists functionality
        private void SavedListsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e) { }
        private void LoadSavedListButton_Click(object sender, RoutedEventArgs e) { }
        private void DeleteSavedListButton_Click(object sender, RoutedEventArgs e) { }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }
    }
}