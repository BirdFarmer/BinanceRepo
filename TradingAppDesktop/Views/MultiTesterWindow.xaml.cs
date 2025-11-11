using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.Logging;
using RestSharp;
using BinanceTestnet.Tools;
using BinanceTestnet.Trading;
using BinanceTestnet.Enums;
using System.Globalization;
using TradingAppDesktop.Services;

namespace TradingAppDesktop.Views
{
    public partial class MultiTesterWindow : Window
    {
    // Controls are defined by XAML (x:FieldModifier="protected"); use the generated fields directly in code-behind.
        private readonly ObservableCollection<string> _progress = new();
        private CancellationTokenSource? _cts;
        private string _resultsCsvPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "results", "multi", "multi_results.csv");

        public MultiTesterWindow()
        {
            InitializeComponent();
            ProgressList.ItemsSource = _progress;
            // Sensible defaults
            ConfigPathTextBox.Text = FindSampleConfig();
            DatabasePathTextBox.Text = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TradingData.db");
            UpdateSummary();
            LoadJsonConfig();
        }
        
        // Load JSON config file into editor
        private void LoadJsonConfig()
        {
            try
            {
                // keep UI lookups local to each method
                var path = ConfigPathTextBox.Text.Trim();
                if (!File.Exists(path))
                {
                    JsonConfigEditor.Text = "File not found.";
                    JsonConfigStatus.Text = "Config file not found.";
                    return;
                }
                JsonConfigEditor.Text = File.ReadAllText(path);
                JsonConfigStatus.Text = "Loaded.";
            }
            catch (Exception ex)
            {
                JsonConfigEditor.Text = "";
                JsonConfigStatus.Text = $"Error loading: {ex.Message}";
            }
        }

        // Reload button handler
        private void ReloadJsonConfig_Click(object sender, RoutedEventArgs e)
        {
            LoadJsonConfig();
        }

        // Validate button handler
        private void ValidateJsonConfig_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var text = JsonConfigEditor.Text;
                Newtonsoft.Json.Linq.JToken.Parse(text);
                JsonConfigStatus.Text = "Valid JSON.";
            }
            catch (Exception ex)
            {
                JsonConfigStatus.Text = $"Invalid JSON: {ex.Message}";
            }
        }

        // Save button handler
        private void SaveJsonConfig_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var path = ConfigPathTextBox.Text.Trim();
                var text = JsonConfigEditor.Text;
                // Validate before saving
                Newtonsoft.Json.Linq.JToken.Parse(text);
                File.WriteAllText(path, text);
                JsonConfigStatus.Text = "Saved successfully.";
            }
            catch (Exception ex)
            {
                JsonConfigStatus.Text = $"Save failed: {ex.Message}";
            }
        }

        // Copy the summary text to the clipboard so users can paste/share insights
        private void CopySummary_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Prefer generated field if available, fallback to FindName
                var tb = this.FindName("SummaryTextBox") as TextBox;
                string text = tb?.Text ?? string.Empty;
                if (string.IsNullOrWhiteSpace(text))
                {
                    // Update status control if present, otherwise show a message box
                    var st = this.FindName("StatusText");
                    if (st is TextBlock stb) stb.Text = "No summary available to copy.";
                    else if (st is TextBox stbox) stbox.Text = "No summary available to copy.";
                    else
                        MessageBox.Show(this, "No summary available to copy.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // Clipboard operations must run on the UI thread (STA). Ensure we invoke on Dispatcher.
                Dispatcher.Invoke(() => Clipboard.SetText(text));

                var statusObj = this.FindName("StatusText");
                if (statusObj is TextBlock statusTb) statusTb.Text = "Summary copied to clipboard.";
                else if (statusObj is TextBox statusBox) statusBox.Text = "Summary copied to clipboard.";
                else
                    MessageBox.Show(this, "Summary copied to clipboard.", "Copied", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Failed to copy summary: {ex.Message}", "Copy Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private string FindSampleConfig()
        {
            // Robust upward directory scan to locate sample config anywhere in repo
            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var dir = new DirectoryInfo(baseDir);
                for (int depth = 0; depth < 6 && dir != null; depth++)
                {
                    var candidate = Path.Combine(dir.FullName, "multi_backtest.sample.json");
                    if (File.Exists(candidate)) return candidate;
                    // Known tools path
                    var toolsCandidate = Path.Combine(dir.FullName, "BinanceTestnet", "Tools", "multi_backtest.sample.json");
                    if (File.Exists(toolsCandidate)) return toolsCandidate;
                    dir = dir.Parent;
                }
            }
            catch { /* ignore */ }
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "multi_backtest.sample.json");
        }

        private void BrowseConfig_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*"
            };
            try
            {
                var probe = FindSampleConfig();
                var dir = File.Exists(probe) ? Path.GetDirectoryName(probe)! : AppDomain.CurrentDomain.BaseDirectory;
                if (Directory.Exists(dir)) dlg.InitialDirectory = dir;
            }
            catch { /* ignore */ }
            if (dlg.ShowDialog(this) == true)
            {
                ConfigPathTextBox.Text = dlg.FileName;
            }
        }

        private void BrowseDb_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "SQLite DB (*.db;*.sqlite)|*.db;*.sqlite|All files (*.*)|*.*"
            };
            if (dlg.ShowDialog(this) == true)
            {
                DatabasePathTextBox.Text = dlg.FileName;
            }
        }

        private async void RunButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var cfgPath = ConfigPathTextBox.Text.Trim();
                var dbPath = DatabasePathTextBox.Text.Trim();
                if (!File.Exists(cfgPath)) { MessageBox.Show(this, $"Config file not found at: {cfgPath}\nUse Browse to select or copy sample from BinanceTestnet/Tools.", "Error", MessageBoxButton.OK, MessageBoxImage.Error); return; }
                if (string.IsNullOrWhiteSpace(dbPath)) { MessageBox.Show(this, "Please set database path.", "Error", MessageBoxButton.OK, MessageBoxImage.Error); return; }

                RunButton.IsEnabled = false;
                CancelButton.IsEnabled = true;
                _progress.Clear();
                _cts = new CancellationTokenSource();

                await Task.Run(async () =>
                {
                    try
                    {
                        Append($"Loading config: {cfgPath}");
                        var cfg = MultiBacktestRunner.LoadConfig(cfgPath);
                        // Create a minimal logger; in app we might reuse DI later
                        using var loggerFactory = LoggerFactory.Create(builder => { });
                        var logger = loggerFactory.CreateLogger<OrderManager>();
                        var client = new RestClient("https://fapi.binance.com");
                        string apiKey = string.Empty; // testnet/historical needs public endpoints only
                        var svcLogger = loggerFactory.CreateLogger<BinanceTradingService>();
                        var exchangeInfoProvider = new BinanceTradingService(svcLogger, loggerFactory);
                        var runner = new MultiBacktestRunner(client, apiKey, dbPath, logger, exchangeInfoProvider);

                        Append("Starting runs...");
                        var configDir = Path.GetDirectoryName(cfgPath) ?? AppDomain.CurrentDomain.BaseDirectory;
                        await runner.RunAsync(cfg, _cts!.Token, baseDirectory: configDir);
                        Append("All runs completed.");
                    }
                    catch (Exception ex)
                    {
                        Append($"ERROR: {ex.Message}");
                    }
                }, _cts.Token);
            }
            finally
            {
                RunButton.IsEnabled = true;
                CancelButton.IsEnabled = false;
                UpdateSummary();
            }
        }

        private void Append(string msg)
        {
            Dispatcher.Invoke(() =>
            {
                _progress.Add($"{DateTime.Now:HH:mm:ss} {msg}");
            });
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            _cts?.Cancel();
            Append("Cancel requested.");
        }

        private void OpenResults_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Use the output directory from the config file location
                var cfgPath = ConfigPathTextBox.Text.Trim();
                var outputDir = "results/multi";
                if (File.Exists(cfgPath))
                {
                    var configDir = Path.GetDirectoryName(cfgPath) ?? AppDomain.CurrentDomain.BaseDirectory;
                    outputDir = Path.Combine(configDir, "results", "multi");
                }
                Directory.CreateDirectory(outputDir);
                Process.Start(new ProcessStartInfo
                {
                    FileName = outputDir,
                    UseShellExecute = true,
                    Verb = "open"
                });
                UpdateSummary();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Open Folder Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Reads the latest row from multi_results.csv and updates the summary section
        private void UpdateSummary()
        {
            try
            {
                // Use the output directory from the config file location
                var cfgPath = ConfigPathTextBox.Text.Trim();
                var outputDir = "results/multi";
                if (File.Exists(cfgPath))
                {
                    var configDir = Path.GetDirectoryName(cfgPath) ?? AppDomain.CurrentDomain.BaseDirectory;
                    outputDir = Path.Combine(configDir, "results", "multi");
                }
                var resultsCsvPath = Path.Combine(outputDir, "multi_results.csv");
                if (!File.Exists(resultsCsvPath))
                {
                    var tb0 = this.FindName("SummaryTextBlock") as TextBlock;
                    if (tb0 != null) tb0.Text = "No summary available.";
                    return;
                }
                var lines = File.ReadAllLines(resultsCsvPath);
                if (lines.Length < 2)
                {
                    var tb1 = this.FindName("SummaryTextBlock") as TextBlock;
                    if (tb1 != null) tb1.Text = "No summary available.";
                    return;
                }
                var header = lines[0].Split(',');
                // Define the columns we expect for a full row used by the summary
                var mandatoryCols = new[] { "timeframe", "symbolSet", "exitMode", "tpMult", "slMult", "trades", "winRate", "netPnl", "avgWin", "avgLoss", "avgDuration" };
                var topSymbolCol = header.Contains("topSymbol") ? "topSymbol" : (header.Contains("topSymbols") ? "topSymbols" : null);
                var bottomSymbolCol = header.Contains("bottomSymbol") ? "bottomSymbol" : (header.Contains("bottomSymbols") ? "bottomSymbols" : null);

                // Build header lookup
                var headerIndex = header.Select((h, i) => new { h, i }).ToDictionary(x => x.h, x => x.i);

                // Check all mandatory columns exist in header
                var missingMandatory = mandatoryCols.Where(c => !headerIndex.ContainsKey(c)).ToList();
                if (missingMandatory.Any())
                {
                    var tb2 = this.FindName("SummaryTextBlock") as TextBlock;
                    if (tb2 != null) tb2.Text = $"CSV missing required columns: {string.Join(", ", missingMandatory)}";
                    return;
                }
                // Only process rows that match the header column count
                var rows = lines.Skip(1)
                    .Select(l => l.Split(','))
                    .Where(r => r.Length == header.Length)
                    .ToList();
                // Only use rows with all columns present for summary
                if (rows.Count == 0)
                {
                    var tb3 = this.FindName("SummaryTextBlock") as TextBlock;
                    if (tb3 != null) tb3.Text = "No valid results found in CSV.";
                    return;
                }
                int GetIdx(string col) => Array.IndexOf(header, col);

                // Safe value accessor: returns empty string if column missing or row too short
                string GetVal(string[] row, string col)
                {
                    int idx = Array.IndexOf(header, col);
                    return (idx >= 0 && idx < row.Length) ? row[idx] : string.Empty;
                }

                // Group and analyze using safe accessor to avoid any index issues
                var timeframeStats = rows.GroupBy(r => GetVal(r, "timeframe"))
                    .Select(g => new {
                        Timeframe = g.Key,
                        AllPositive = g.All(r => decimal.TryParse(GetVal(r, "netPnl"), out var p) && p > 0),
                        AnyNegative = g.Any(r => decimal.TryParse(GetVal(r, "netPnl"), out var p) && p < 0)
                    })
                    .ToList();

                var symbolSetStats = rows.GroupBy(r => GetVal(r, "symbolSet"))
                    .Select(g => new {
                        SymbolSet = g.Key,
                        AllNegative = g.All(r => decimal.TryParse(GetVal(r, "netPnl"), out var p) && p < 0)
                    }).ToList();

                // Build insights
                var insights = new System.Text.StringBuilder();
                insights.AppendLine("Recommendations & Insights:");

                // Timeframes
                foreach (var tf in timeframeStats)
                {
                    if (tf.AllPositive)
                        insights.AppendLine($"✔ Timeframe '{tf.Timeframe}' has only positive PnL. Consider prioritizing.");
                    else if (tf.AnyNegative)
                        insights.AppendLine($"⚠ Timeframe '{tf.Timeframe}' includes negative PnL runs. Review before using.");
                }

                // Risk profiles (if present)
                var rpIdx = GetIdx("riskProfile");
                if (rpIdx >= 0)
                {
                    var riskProfileStats = rows.GroupBy(r => GetVal(r, "riskProfile"))
                        .Select(g => new {
                            RiskProfile = g.Key,
                            AnyNegWinRatePosPnl = g.Any(r => decimal.TryParse(GetVal(r, "winRate"), out var w) && w < 0 && decimal.TryParse(GetVal(r, "netPnl"), out var p) && p > 0)
                        }).ToList();

                    foreach (var rp in riskProfileStats)
                    {
                        if (rp.AnyNegWinRatePosPnl)
                            insights.AppendLine($"⚠ Risk profile '{rp.RiskProfile}' has negative win rate but positive PnL. Investigate for edge cases.");
                    }
                }

                // Symbol sets
                foreach (var ss in symbolSetStats)
                {
                    if (ss.AllNegative)
                        insights.AppendLine($"✗ Symbol set '{ss.SymbolSet}' has only negative PnL. Avoid trading these pairs.");
                }

                // Overall stats
                var totalRuns = rows.Count;
                var positiveRuns = rows.Count(r => decimal.TryParse(GetVal(r, "netPnl"), NumberStyles.Any, CultureInfo.InvariantCulture, out var p) && p > 0);
                decimal avgNet = 0m;
                try
                {
                    avgNet = rows.Average(r => {
                        decimal.TryParse(GetVal(r, "netPnl"), NumberStyles.Any, CultureInfo.InvariantCulture, out var v);
                        return v;
                    });
                }
                catch { avgNet = 0m; }
                insights.AppendLine($"Total runs: {totalRuns}, Positive runs: {positiveRuns} ({(totalRuns>0?((double)positiveRuns/totalRuns*100):0):F1}%), Avg netPnL: {avgNet:F2}");

                // Helper parsers for single-row analysis (use invariant culture)
                decimal ParseDec(string s) { return decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0m; }
                int ParseInt(string s) { return int.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0; }

                // Find best run by netPnl using safe accessor
                var best = rows.OrderByDescending(r => ParseDec(GetVal(r, "netPnl"))).FirstOrDefault();
                string GetBest(string col)
                {
                    return best != null ? GetVal(best, col) : string.Empty;
                }

                // Dynamically select correct symbol columns
                string topCol = header.Contains("topSymbols") ? "topSymbols" : (header.Contains("topSymbol") ? "topSymbol" : "");
                string bottomCol = header.Contains("bottomSymbols") ? "bottomSymbols" : (header.Contains("bottomSymbol") ? "bottomSymbol" : "");
                string GetBestSymbol(string col)
                {
                    if (string.IsNullOrEmpty(col)) return "(none)";
                    var val = GetBest(col);
                    if (string.IsNullOrWhiteSpace(val)) return "(none)";
                    var first = val.Split('|')[0].Trim();
                    var symbolOnly = first.Split(':')[0].Trim();
                    return symbolOnly;
                }
                string GetWorstSymbol(string col)
                {
                    if (string.IsNullOrEmpty(col)) return "(none)";
                    var val = GetBest(col);
                    if (string.IsNullOrWhiteSpace(val)) return "(none)";
                    var parts = val.Split('|').Select(s => s.Trim()).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
                    var last = parts.Count > 0 ? parts[^1] : "";
                    var symbolOnly = last.Split(':')[0].Trim();
                    return string.IsNullOrEmpty(symbolOnly) ? "(none)" : symbolOnly;
                }
                var bestTopSymbol = !string.IsNullOrEmpty(topCol) ? GetBestSymbol(topCol) : "(none)";
                var bestBottomSymbol = !string.IsNullOrEmpty(bottomCol) ? GetWorstSymbol(bottomCol) : "(none)";

                // Include strategy if present in CSV; otherwise try to read from the JSON config
                var strategyName = GetBest("strategy");
                if (string.IsNullOrWhiteSpace(strategyName)) strategyName = GetBest("Strategy");
                if (string.IsNullOrWhiteSpace(strategyName))
                {
                    // Try load from config file (if available)
                    try
                    {
                        var cfgPathLocal = cfgPath;
                        if (File.Exists(cfgPathLocal))
                        {
                            var j = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(cfgPathLocal));
                            var s = (string?)j["Strategy"] ?? (string?)j["strategy"];
                            if (!string.IsNullOrWhiteSpace(s)) strategyName = s;
                        }
                    }
                    catch { /* ignore */ }
                }
                if (string.IsNullOrWhiteSpace(strategyName)) strategyName = "(unknown)";

                // Try to include run start/end datetimes and number of candles tested when available in CSV
                string[] startCandidates = new[] { "startUtc", "startUtcIso", "startUtcIsoString", "startTime", "startDate", "start" };
                string[] endCandidates = new[] { "endUtc", "endUtcIso", "endUtcIsoString", "endTime", "endDate", "end", "finishUtc" };
                string[] candlesCandidates = new[] { "candles", "candlesTested", "testedCandles", "bars", "numCandles" };
                string foundStartCol = startCandidates.FirstOrDefault(c => header.Contains(c));
                string foundEndCol = endCandidates.FirstOrDefault(c => header.Contains(c));
                string foundCandlesCol = candlesCandidates.FirstOrDefault(c => header.Contains(c));

                string formattedStart = "(unknown)";
                string formattedEnd = "(unknown)";
                DateTime? parsedStart = null;
                DateTime? parsedEnd = null;

                if (!string.IsNullOrEmpty(foundStartCol))
                {
                    var raw = GetBest(foundStartCol);
                    if (!string.IsNullOrWhiteSpace(raw))
                    {
                        // Try parse as ISO or general DateTime, fallback to raw string
                        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt))
                        {
                            parsedStart = dt.ToUniversalTime();
                            formattedStart = parsedStart.Value.ToString("yyyy-MM-dd HH:mm:ss 'UTC'");
                        }
                        else
                        {
                            // Try parse as long ticks/epoch seconds
                            if (long.TryParse(raw, out var lv))
                            {
                                try
                                {
                                    // Interpret as unix seconds if plausible (> 1e9)
                                    if (lv > 1000000000)
                                    {
                                        var dt2 = DateTimeOffset.FromUnixTimeSeconds(lv).UtcDateTime;
                                        parsedStart = dt2;
                                        formattedStart = dt2.ToString("yyyy-MM-dd HH:mm:ss 'UTC'");
                                    }
                                    else
                                    {
                                        var dt3 = new DateTime(lv, DateTimeKind.Utc);
                                        parsedStart = dt3;
                                        formattedStart = dt3.ToString("yyyy-MM-dd HH:mm:ss 'UTC'");
                                    }
                                }
                                catch { formattedStart = raw; }
                            }
                            else
                            {
                                formattedStart = raw;
                            }
                        }
                    }
                }

                if (!string.IsNullOrEmpty(foundEndCol))
                {
                    var rawEnd = GetBest(foundEndCol);
                    if (!string.IsNullOrWhiteSpace(rawEnd))
                    {
                        if (DateTime.TryParse(rawEnd, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dtE))
                        {
                            parsedEnd = dtE.ToUniversalTime();
                            formattedEnd = parsedEnd.Value.ToString("yyyy-MM-dd HH:mm:ss 'UTC'");
                        }
                        else if (long.TryParse(rawEnd, out var lv2))
                        {
                            try
                            {
                                if (lv2 > 1000000000)
                                {
                                    var dt2 = DateTimeOffset.FromUnixTimeSeconds(lv2).UtcDateTime;
                                    parsedEnd = dt2;
                                    formattedEnd = dt2.ToString("yyyy-MM-dd HH:mm:ss 'UTC'");
                                }
                                else
                                {
                                    var dt3 = new DateTime(lv2, DateTimeKind.Utc);
                                    parsedEnd = dt3;
                                    formattedEnd = dt3.ToString("yyyy-MM-dd HH:mm:ss 'UTC'");
                                }
                            }
                            catch { formattedEnd = rawEnd; }
                        }
                        else formattedEnd = rawEnd;
                    }
                }

                string candlesTested = "(unknown)";
                // Prefer explicit candles column if present
                if (!string.IsNullOrEmpty(foundCandlesCol))
                {
                    var rawc = GetBest(foundCandlesCol);
                    if (!string.IsNullOrWhiteSpace(rawc))
                    {
                        if (int.TryParse(rawc, NumberStyles.Any, CultureInfo.InvariantCulture, out var ci)) candlesTested = ci.ToString();
                        else candlesTested = rawc;
                    }
                }

                // If explicit candles not present but we have both start and end plus timeframe, estimate candle count
                if ((string.IsNullOrEmpty(foundCandlesCol) || candlesTested == "(unknown)") && parsedStart.HasValue && parsedEnd.HasValue)
                {
                    int tfMinutes = 0;
                    try
                    {
                        tfMinutes = ParseTimeframeToMinutes(GetBest("timeframe"));
                    }
                    catch { tfMinutes = 0; }
                    if (tfMinutes > 0)
                    {
                        var span = parsedEnd.Value - parsedStart.Value;
                        if (span.TotalMinutes > 0)
                        {
                            var estimated = (int)Math.Round(span.TotalMinutes / tfMinutes);
                            if (estimated < 1) estimated = 1;
                            candlesTested = estimated.ToString();
                        }
                    }
                }

                // Helper to convert timeframe string like '5m' or '1h' to minutes
                int ParseTimeframeToMinutes(string tf)
                {
                    if (string.IsNullOrWhiteSpace(tf)) return 0;
                    tf = tf.Trim().ToLowerInvariant();
                    if (tf.EndsWith("m") && int.TryParse(tf.TrimEnd('m'), out var m)) return m;
                    if (tf.EndsWith("h") && int.TryParse(tf.TrimEnd('h'), out var h)) return h * 60;
                    if (tf.EndsWith("d") && int.TryParse(tf.TrimEnd('d'), out var d)) return d * 1440;
                    // fallback: try parse as minutes
                    if (int.TryParse(tf, out var v)) return v;
                    return 0;
                }

                // Try to surface entry size, leverage and sides (long/short) from CSV or config if available
                string entryDisplay = string.Empty;
                string leverageDisplay = string.Empty;
                string sidesDisplay = string.Empty;
                try
                {
                    // First try CSV best row values (common column names)
                    var candidateEntry = GetBest("entrySize");
                    if (string.IsNullOrWhiteSpace(candidateEntry)) candidateEntry = GetBest("entryAmount");
                    if (string.IsNullOrWhiteSpace(candidateEntry)) candidateEntry = GetBest("positionSize");
                    if (!string.IsNullOrWhiteSpace(candidateEntry)) entryDisplay = candidateEntry;

                    var candidateLev = GetBest("leverage");
                    if (string.IsNullOrWhiteSpace(candidateLev)) candidateLev = GetBest("leverageMultiplier");
                    if (!string.IsNullOrWhiteSpace(candidateLev)) leverageDisplay = candidateLev;

                    var candidateSides = GetBest("sides");
                    if (string.IsNullOrWhiteSpace(candidateSides)) candidateSides = GetBest("side");
                    if (!string.IsNullOrWhiteSpace(candidateSides)) sidesDisplay = candidateSides;

                    // Fallback to config file (if available)
                    var cfgPathLocal = cfgPath;
                    if ((string.IsNullOrWhiteSpace(entryDisplay) || string.IsNullOrWhiteSpace(leverageDisplay) || string.IsNullOrWhiteSpace(sidesDisplay)) && File.Exists(cfgPathLocal))
                    {
                        try
                        {
                            var j = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(cfgPathLocal));
                            if (string.IsNullOrWhiteSpace(entryDisplay))
                            {
                                var v = j.SelectToken("EntrySize") ?? j.SelectToken("entrySize") ?? j.SelectToken("PositionSize");
                                if (v != null) entryDisplay = v.ToString();
                            }
                            if (string.IsNullOrWhiteSpace(leverageDisplay))
                            {
                                var v = j.SelectToken("Leverage") ?? j.SelectToken("leverage");
                                if (v != null) leverageDisplay = v.ToString();
                            }
                            if (string.IsNullOrWhiteSpace(sidesDisplay))
                            {
                                // Accept boolean flags or explicit sides list
                                var allowShorts = j.SelectToken("AllowShorts") ?? j.SelectToken("allowShorts");
                                if (allowShorts != null && bool.TryParse(allowShorts.ToString(), out var allow) && allow)
                                {
                                    sidesDisplay = "longs and shorts";
                                }
                                else
                                {
                                    var v = j.SelectToken("Sides") ?? j.SelectToken("sides");
                                    if (v != null) sidesDisplay = v.ToString();
                                }
                            }
                        }
                        catch { /* ignore config parse errors */ }
                    }
                }
                catch { /* non-fatal */ }

                // Build the best run summary: show strategy first as requested; remove the Candles Tested line per request
                var bestSummary =
                    $"Strategy: {strategyName}\n" +
                    $"Best Run (by netPnl)\n" +
                    $"Start: {formattedStart}\n" +
                    $"End: {formattedEnd}\n" +
                    $"Candles Tested: {candlesTested}\n" +
                    $"Timeframe: {GetBest("timeframe")}\n" +
                    $"Symbols: {GetBest("symbolSet")}\n" +
                    $"TP Multiplier: {GetBest("tpMult")}\n" +
                    $"Risk Ratio: {GetBest("slMult")}\n" +
                    (string.IsNullOrWhiteSpace(entryDisplay) ? string.Empty : $"Entry Size: {entryDisplay}\n") +
                    (string.IsNullOrWhiteSpace(leverageDisplay) ? string.Empty : $"Leverage: {leverageDisplay}X\n") +
                    (string.IsNullOrWhiteSpace(sidesDisplay) ? string.Empty : $"Sides: {sidesDisplay}\n") +
                    $"Win Rate: {GetBest("winRate")}%\n" +
                    $"Net PNL: {GetBest("netPnl")}\n" +
                    $"Trades: {GetBest("trades")}\n" +
                    $"Avg Win: {GetBest("avgWin")}\n" +
                    $"Avg Loss: {GetBest("avgLoss")}\n" +
                    $"Avg Duration: {GetBest("avgDuration")}\n" +
                    $"Best Symbol: {bestTopSymbol}\n" +
                    $"Worst Symbol: {bestBottomSymbol}";

                // Analyze best run specifics for extra insights
                if (best != null)
                {
                    var bTrades = ParseInt(GetBest("trades"));
                    var bWinRate = ParseDec(GetBest("winRate"));
                    var bExpectancy = ParseDec(GetBest("expectancy"));
                    var bPayoff = ParseDec(GetBest("payoff"));
                    var bAvgWin = ParseDec(GetBest("avgWin"));
                    var bAvgLoss = ParseDec(GetBest("avgLoss"));

                    if (bTrades < 5) insights.AppendLine($"⚠ Best run has low sample size (trades={bTrades}). Treat results cautiously.");
                    if (bWinRate < 30) insights.AppendLine($"⚠ Low win rate ({bWinRate}%). Consider reviewing entry filters or sizing.");
                    if (bExpectancy > 0 && bWinRate < 40) insights.AppendLine($"ℹ Expectancy positive ({bExpectancy:F2}) despite low win rate — payoff or risk management may be favorable.");
                    if (bAvgLoss != 0 && bAvgWin / Math.Max(1, bAvgLoss) < 1m) insights.AppendLine($"⚠ Avg win ({bAvgWin}) is smaller than avg loss ({bAvgLoss}). Consider adjusting TP/SL.");
                    if (bPayoff > 0) insights.AppendLine($"ℹ Payoff: {bPayoff:F2} — higher is generally better.");
                }

                // Detect runs with zero trades (inactive runs) and add an insight if many runs are inactive
                try
                {
                    var zeroTradeCount = rows.Count(r => ParseInt(GetVal(r, "trades")) == 0);
                    if (zeroTradeCount > Math.Max(1, totalRuns / 2))
                    {
                        insights.AppendLine($"⚠ Many runs produced 0 trades: {zeroTradeCount}/{totalRuns}. Check entry filters, symbol sets, or timeframe — strategy appears inactive in these configurations.");
                    }
                }
                catch { /* non-fatal */ }

                // Show latest run metrics as well
                // Find the latest row with trades > 0 (safe)
                var latestWithTrades = rows.LastOrDefault(r => ParseInt(GetVal(r, "trades")) > 0);
                var rowToShow = latestWithTrades ?? rows.Last();
                string GetRow(string col) => GetVal(rowToShow, col);
                int trades = ParseInt(GetRow("trades"));
                string topSymbol, bottomSymbol;
                if (trades == 0 || string.IsNullOrEmpty(topCol) || string.IsNullOrEmpty(bottomCol))
                {
                    topSymbol = "(none)";
                    bottomSymbol = "(none)";
                }
                else
                {
                    topSymbol = GetBestSymbol(topCol);
                    bottomSymbol = GetWorstSymbol(bottomCol);
                }
                var latestStrategy = GetRow("strategy");
                if (string.IsNullOrWhiteSpace(latestStrategy)) latestStrategy = GetRow("Strategy");
                if (string.IsNullOrWhiteSpace(latestStrategy)) latestStrategy = "(unknown)";
                // We no longer show a separate 'Latest Run' below the insights; the best-run summary + insights is shown.
                var tb4 = this.FindName("SummaryTextBox") as TextBox;
                if (tb4 != null) tb4.Text = bestSummary + "\n\n" + insights.ToString();

                // Persist a short tooltip/insight for the strategy so the main UI can display it
                try
                {
                    var safeStrategyKey = strategyName;
                    if (!string.IsNullOrWhiteSpace(safeStrategyKey) && safeStrategyKey != "(unknown)")
                    {
                        // Build a concise parameter-summary based on the best run so the tooltip shows the setup that produced
                        // the top-performing result (timeframe, symbol set, TP/SL, win rate, net PnL, trades, start, candles).
                        var bestTf = GetBest("timeframe");
                        var bestSymbols = GetBest("symbolSet");
                        // Try to expand symbol set names (e.g. tier_50_54) into concrete symbol lists
                        try
                        {
                            var sampleCfg = FindSampleConfig();
                            if (File.Exists(sampleCfg))
                            {
                                var j = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(sampleCfg));
                                var sets = j["SymbolSets"] as Newtonsoft.Json.Linq.JObject;
                                if (sets != null && sets.TryGetValue(bestSymbols, out var arr))
                                {
                                    try
                                    {
                                        var list = arr.Values<string>().ToArray();
                                        if (list.Length > 0)
                                        {
                                            // Replace the short name with expanded representation
                                            bestSymbols = bestSymbols + " (" + string.Join(",", list) + ")";
                                            // Also update the UI summary text to show expanded symbols
                                            if (tb4 != null)
                                            {
                                                tb4.Text = tb4.Text.Replace($"Symbols: {GetBest("symbolSet")}", $"Symbols: {bestSymbols}");
                                            }
                                        }
                                    }
                                    catch { /* ignore */ }
                                }
                            }
                        }
                        catch { /* ignore */ }
                        var bestTp = GetBest("tpMult");
                        var bestSl = GetBest("slMult");
                        var bestWin = GetBest("winRate");
                        var bestNet = GetBest("netPnl");
                        var bestTrades = GetBest("trades");

                        // Use formattedStart and candlesTested if available (parsed earlier)
                        var startDisplay = formattedStart ?? "(unknown)";
                        var candlesDisplay = candlesTested ?? "(unknown)";

                        var shortInsight =
                            $"Best setup — TF:{bestTf}; Symbols:{bestSymbols}; TP:{bestTp}; SL:{bestSl}; Win:{bestWin}% ; Net:{bestNet}; Trades:{bestTrades}; Start:{startDisplay}; Candles:{candlesDisplay}";

                        var map = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        var insPath = Path.Combine(outputDir, "strategy_insights.json");
                        if (File.Exists(insPath))
                        {
                            try
                            {
                                var existing = Newtonsoft.Json.JsonConvert.DeserializeObject<System.Collections.Generic.Dictionary<string, string>>(File.ReadAllText(insPath));
                                if (existing != null)
                                {
                                    foreach (var kv in existing) map[kv.Key] = kv.Value;
                                }
                            }
                            catch { /* ignore malformed existing file */ }
                        }
                        map[safeStrategyKey] = shortInsight;
                        File.WriteAllText(insPath, Newtonsoft.Json.JsonConvert.SerializeObject(map, Newtonsoft.Json.Formatting.Indented));
                    }
                }
                catch { /* non-fatal */ }
            }
            catch (Exception ex)
            {
                var tbErr = this.FindName("SummaryTextBlock") as TextBlock;
                if (tbErr != null) tbErr.Text = $"Error reading summary: {ex.Message}";
            }
        }
    }
}
