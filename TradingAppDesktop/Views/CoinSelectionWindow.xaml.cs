using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Text;
using System.Text.RegularExpressions;
using BinanceTestnet.Database;
using System.Threading;
using System.Threading.Tasks;
using RestSharp;
using Newtonsoft.Json;

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
            if (AutoSelectionMethod != null)
                AutoSelectionMethod.SelectedIndex = 3; // Default to Composite Score
            UpdateSelectionSummary();
            RefreshSavedLists();
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
                var topCoins = _databaseManager.GetTopCoinPairsByVolume(5);
                var totalSymbols = _databaseManager.GetBiggestCoins(1000).Count;

                if (DatabaseInfoText != null)
                DatabaseInfoText.Text = $"Database: {totalSymbols} symbols\n" +
                                       $"Last refresh: {DateTime.Now:HH:mm:ss}\n" +
                                       $"Top 5 by volume:\n{string.Join(", ", topCoins)}";
                if (StatusText != null)
                    StatusText.Text = "Database connected - Ready for refresh";
            }
            catch (Exception ex)
            {
                if (DatabaseInfoText != null)
                    DatabaseInfoText.Text = $"Database error: {ex.Message}";
                if (StatusText != null)
                    StatusText.Text = "Database connection failed";
            }
        }

        private class SavedListItem
        {
            public int Id { get; set; }
            public string? Name { get; set; }
            public DateTime Start { get; set; }
            public DateTime? End { get; set; }
            public int Count { get; set; }
            public override string ToString()
            {
                var label = End == null ? "(active)" : "";
                if (!string.IsNullOrWhiteSpace(Name))
                {
                    return $"{Name}  —  {Start:yyyy-MM-dd HH:mm} {label}  —  {Count} coins";
                }
                return $"{Start:yyyy-MM-dd HH:mm} {label}  —  {Count} coins";
            }
        }

        private void RefreshSavedLists()
        {
            try
            {
                var lists = _databaseManager.GetAllCoinPairLists();
                SavedListsListBox.Items.Clear();
                foreach (var (id, name, start, end, coins) in lists)
                {
                    SavedListsListBox.Items.Add(new SavedListItem
                    {
                        Id = id,
                        Name = name,
                        Start = start,
                        End = end,
                        Count = coins.Count
                    });
                }
            }
            catch
            {
                // ignore list load errors to avoid blocking UI
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
                var listName = ListNameTextBox?.Text?.Trim();
                if (string.IsNullOrWhiteSpace(listName)) listName = null;
                _databaseManager.UpsertCoinPairList(_currentSelectedCoins, DateTime.UtcNow, listName);

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
                var listName = ListNameTextBox?.Text?.Trim();
                if (string.IsNullOrWhiteSpace(listName)) listName = null;
                _databaseManager.UpsertCoinPairList(_currentSelectedCoins, DateTime.UtcNow, listName);
                MessageBox.Show($"Saved {_currentSelectedCoins.Count} coins to database" +
                              (string.IsNullOrWhiteSpace(listName) ? "" : $" as '{listName}'"),
                              "Saved", MessageBoxButton.OK, MessageBoxImage.Information);
                StatusText.Text = "Selection saved to database";
                if (ListNameTextBox != null) ListNameTextBox.Text = string.Empty;
                RefreshSavedLists();
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

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                RefreshButton.IsEnabled = false;

                // Show refresh type selection dialog
                var result = MessageBox.Show(
                    "Choose refresh type:\n\n" +
                    "YES - Full Refresh (recommended)\n• Updates ALL coins (2-3 seconds)\n• Most accurate rankings\n• Best for trading decisions\n\n" +
                    "NO - Quick Refresh\n• Updates top 200 coins only (1 second)\n• Faster but less comprehensive\n• Good for quick checks",
                    "Refresh Market Data",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question
                );

                switch (result)
                {
                    case MessageBoxResult.Yes:
                        await ExecuteFullRefresh();
                        break;
                    case MessageBoxResult.No:
                        await ExecuteQuickRefresh();
                        break;
                    default:
                        StatusText.Text = "Refresh canceled";
                        return; // User canceled
                }

                RefreshDatabaseInfo();
                StatusText.Text = $"Market data refreshed at {DateTime.Now:HH:mm:ss}";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Refresh failed: {ex.Message}";
                MessageBox.Show($"Could not refresh market data: {ex.Message}", "Refresh Error",
                              MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                RefreshButton.IsEnabled = true;
            }
        }


        private async Task ExecuteFullRefresh()
        {
            RefreshButton.Content = "🔄 Full Refresh...";
            StatusText.Text = "Refreshing ALL market data...";

            await RefreshAllMarketDataBulk();

            MessageBox.Show("✅ Full refresh complete!\n\nAll trading pairs updated with latest market data.\nYour rankings and selections are now fully accurate.",
                          "Full Refresh Complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async Task RefreshAllMarketDataBulk()
        {
            var client = new RestClient("https://fapi.binance.com");

            // Single API call gets ALL symbol data
            var request = new RestRequest("/fapi/v1/ticker/24hr", Method.Get);

            var response = await client.ExecuteAsync<List<BulkSymbolData>>(request);

            if (response.IsSuccessful && response.Data != null)
            {
                int usdtPairsCount = 0;

                foreach (var symbolData in response.Data)
                {
                    // Only process USDT pairs
                    if (symbolData.Symbol.EndsWith("USDT") && symbolData.LastPrice > 0)
                    {
                        _databaseManager.UpsertCoinPairData(
                            symbolData.Symbol,
                            symbolData.LastPrice,
                            symbolData.Volume
                        );
                        usdtPairsCount++;
                    }
                }

                StatusText.Text = $"Full refresh: {usdtPairsCount} USDT pairs updated";
            }
            else
            {
                throw new Exception($"API request failed: {response.ErrorMessage}");
            }
        }


        private async Task RefreshTopMarketData(int topCount)
        {
            var client = new RestClient("https://fapi.binance.com");

            // Get only the top coins by volume to refresh
            var topSymbols = _databaseManager.GetTopCoinPairsByVolume(topCount);

            int updatedCount = 0;
            int errorCount = 0;

            // Use parallel processing for speed
            var tasks = topSymbols.Select(async symbol =>
            {
                try
                {
                    var price = await FetchSymbolPrice(client, symbol);
                    var volume = await FetchSymbolVolume(client, symbol);

                    if (price > 0 && volume > 0)
                    {
                        _databaseManager.UpsertCoinPairData(symbol, price, volume);
                        Interlocked.Increment(ref updatedCount);
                    }
                }
                catch
                {
                    Interlocked.Increment(ref errorCount);
                }
            });

            await Task.WhenAll(tasks);

            StatusText.Text = $"Quick refresh: {updatedCount} top coins updated, {errorCount} failed";
        }


        private async Task<decimal> FetchSymbolPrice(RestClient client, string symbol)
        {
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            var request = new RestRequest("/fapi/v1/ticker/price", Method.Get);
            request.AddParameter("symbol", symbol);

            var response = await client.ExecuteAsync<PriceResponse>(request, cts.Token);

            return response.IsSuccessful && response.Data != null ? response.Data.Price : 0;
        }


        private async Task<decimal> FetchSymbolVolume(RestClient client, string symbol)
        {
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            var request = new RestRequest("/fapi/v1/ticker/24hr", Method.Get);
            request.AddParameter("symbol", symbol);

            var response = await client.ExecuteAsync<SymbolDataResponse>(request, cts.Token);

            return response.IsSuccessful && response.Data != null ? response.Data.Volume : 0;
        }

        private async Task ExecuteQuickRefresh()
        {
            RefreshButton.Content = "🔄 Quick Refresh...";
            StatusText.Text = "Refreshing top 200 coins...";

            await RefreshTopMarketData(200);

            MessageBox.Show("✅ Quick refresh complete!\n\nTop 200 coins updated.\nNote: Lower-ranked coins still use older data.",
                          "Quick Refresh Complete", MessageBoxButton.OK, MessageBoxImage.Information);
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
        private void SavedListsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // no-op; we load explicitly on button click
        }

        // Allow Delete key to remove selected coins from the current selection
        private void SelectedCoinsListBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Delete to remove selected items
            if (e.Key == Key.Delete)
            {
                RemoveSelectedCoins();
                e.Handled = true;
                return;
            }

            // Ctrl+C to copy selected items
            if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control && e.Key == Key.C)
            {
                CopySelectedCoins_Click(sender, new RoutedEventArgs());
                e.Handled = true;
                return;
            }
        }

        private void SavedListsListBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Delete)
            {
                DeleteSavedListButton_Click(sender, new RoutedEventArgs());
                e.Handled = true;
            }
        }

        private void CopySelectedCoins_Click(object sender, RoutedEventArgs e)
        {
            var items = SelectedCoinsListBox.SelectedItems.Cast<string>().ToList();
            if (!items.Any()) return;
            var txt = string.Join(", ", items);
            Clipboard.SetText(txt);
            StatusText.Text = $"Copied {items.Count} coins to clipboard";
        }

        private void CopySelectedToManual_Click(object sender, RoutedEventArgs e)
        {
            var items = SelectedCoinsListBox.SelectedItems.Cast<string>().ToList();
            if (!items.Any()) return;
            var txt = string.Join(", ", items);
            // Append to manual coins text box and ensure newline separation
            if (ManualCoinsTextBox.Text.Length > 0 && !ManualCoinsTextBox.Text.EndsWith("\n"))
                ManualCoinsTextBox.AppendText("\n");
            ManualCoinsTextBox.AppendText(txt);
            StatusText.Text = $"Pasted {items.Count} coins to Manual list";
        }

        private void RemoveSelectedCoins_Click(object sender, RoutedEventArgs e)
        {
            RemoveSelectedCoins();
        }

        private void RemoveSelectedCoins()
        {
            var selected = SelectedCoinsListBox.SelectedItems.Cast<string>().ToList();
            if (!selected.Any()) return;
            foreach (var s in selected)
            {
                _currentSelectedCoins.Remove(s);
            }
            UpdateSelectedCoinsList();
            StatusText.Text = $"Removed {selected.Count} coins";
        }

        private void CopySelectedSavedList_Click(object sender, RoutedEventArgs e)
        {
            var items = SavedListsListBox.SelectedItems.Cast<object>().ToList();
            if (!items.Any()) return;
            var sb = new StringBuilder();
            foreach (var it in items)
            {
                sb.AppendLine(it.ToString());
            }
            Clipboard.SetText(sb.ToString());
            StatusText.Text = $"Copied {items.Count} saved-list lines to clipboard";
        }

        // Open the normalize menu on left-click
        private void NormalizeButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button b && b.ContextMenu != null)
            {
                b.ContextMenu.PlacementTarget = b;
                b.ContextMenu.IsOpen = true;
            }
        }

        private void Normalize_OnePerLine_Click(object sender, RoutedEventArgs e)
        {
            NormalizeManualText(onePerLine: true);
        }

        private void Normalize_CommaSeparated_Click(object sender, RoutedEventArgs e)
        {
            NormalizeManualText(onePerLine: false);
        }

        // Normalize manual textbox content into either one-per-line or comma-separated formats.
        private void NormalizeManualText(bool onePerLine)
        {
            var raw = ManualCoinsTextBox.Text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(raw)) return;

            // Split on common separators: comma, semicolon, newline, whitespace
            var tokens = Regex.Split(raw, @"[\s,;]+")
                              .Select(t => t.Trim().ToUpper())
                              .Where(t => !string.IsNullOrWhiteSpace(t))
                              .Distinct()
                              .ToList();

            if (!tokens.Any()) return;

            var formatted = onePerLine
                ? string.Join(Environment.NewLine, tokens)
                : string.Join(", ", tokens);

            ManualCoinsTextBox.Text = formatted + Environment.NewLine;
            ManualCoinsTextBox.CaretIndex = ManualCoinsTextBox.Text.Length;
            ManualCoinsTextBox.Focus();
            StatusText.Text = $"Normalized manual list ({tokens.Count} items)";
        }

        private void LoadSavedListButton_Click(object sender, RoutedEventArgs e)
        {
            if (SavedListsListBox.SelectedItem is not SavedListItem item)
            {
                MessageBox.Show("Please select a saved list first.");
                return;
            }
            try
            {
                var coins = _databaseManager.GetCoinPairListById(item.Id);
                if (coins.Any())
                {
                    _currentSelectedCoins = coins;
                    UpdateSelectedCoinsList();
                    if (SelectionMethodText != null)
                        SelectionMethodText.Text = string.IsNullOrWhiteSpace(item.Name) ?
                            "Method: Saved List" : $"Method: Saved List ('{item.Name}')";
                    if (ListNameTextBox != null)
                        ListNameTextBox.Text = item.Name ?? string.Empty;
                    if (StatusText != null)
                        StatusText.Text = $"Loaded saved list from {item.Start:yyyy-MM-dd HH:mm} ({coins.Count} coins)";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load saved list: {ex.Message}");
            }
        }

        private void DeleteSavedListButton_Click(object sender, RoutedEventArgs e)
        {
            if (SavedListsListBox.SelectedItem is not SavedListItem item)
            {
                MessageBox.Show("Please select a saved list to delete.");
                return;
            }
            var confirm = MessageBox.Show("Delete this saved list? This action cannot be undone.",
                "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes) return;

            try
            {
                _databaseManager.DeleteCoinPairList(item.Id);
                RefreshSavedLists();
                if (StatusText != null)
                    StatusText.Text = "Saved list deleted";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to delete saved list: {ex.Message}");
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }
    }

    public class BulkSymbolData
    {
        [JsonProperty("symbol")]
        public string Symbol { get; set; }

        [JsonProperty("lastPrice")]
        public decimal LastPrice { get; set; }

        [JsonProperty("volume")]
        public decimal Volume { get; set; }
    }


    public class PriceResponse
    {
        public string Symbol { get; set; }
        public decimal Price { get; set; }
    }


    public class SymbolDataResponse
    {
        [JsonProperty("volume")]
        public decimal Volume { get; set; }
    }

}