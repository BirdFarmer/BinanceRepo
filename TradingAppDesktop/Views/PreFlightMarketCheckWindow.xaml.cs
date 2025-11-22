using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using BinanceTestnet.MarketAnalysis;
using BinanceTestnet.Models;
using TradingAppDesktop.Services;
using Microsoft.Extensions.Logging;
using RestSharp;
using System.Windows.Media;
using System.Windows.Input;
using Newtonsoft.Json;
using System.Globalization;
using System.IO;

namespace TradingAppDesktop.Views
{
    public partial class PreFlightMarketCheckWindow : Window
    {
        public PreFlightMarketCheckWindow()
        {
            InitializeComponent();
        }

        private async void RunButton_Click(object sender, RoutedEventArgs e)
        {
            var coins = new List<string>();
            void addIf(string s) { if (!string.IsNullOrWhiteSpace(s)) coins.Add(s.Trim().ToUpperInvariant()); }
            addIf(Coin1TextBox.Text);
            addIf(Coin2TextBox.Text);
            addIf(Coin3TextBox.Text);
            addIf(Coin4TextBox.Text);

            if (coins.Count == 0)
            {
                MessageBox.Show(this, "Please enter at least one coin (e.g. BTCUSDT)", "No coins", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var tfItem = TimeFrameComboBox.SelectedItem as ComboBoxItem;
            var timeframe = tfItem?.Content?.ToString() ?? "5m";

            RunButton.IsEnabled = false;
            ResultsPanel.Children.Clear();
            ProgressText.Text = "Running...";

            try
            {
                // Create analyzer (simple logger to console)
                var loggerFactory = LoggerFactory.Create(builder => { });
                var logger = loggerFactory.CreateLogger<MarketContextAnalyzer>();
                var client = new RestClient("https://fapi.binance.com");
                var analyzer = new MarketContextAnalyzer(client, logger);

                // Compute start time for ~1000 candles
                int candleCount = 1000;
                var minutesPerCandle = GetMinutesForTimeframe(timeframe);
                var start = DateTime.UtcNow.AddMinutes(-minutesPerCandle * candleCount);

                // BTC correlation disabled — skip pre-warm fetch
                // (Removed to simplify Pre-Flight and avoid fragile cross-symbol fetches)

                // Local helper to analyze a single symbol with typed return
                async Task<(string symbol, MarketContext? ctx, BTCIndicatorSet? primary, List<Kline>? klines, Exception? error)> AnalyzeSymbolAsync(string symbol)
                {
                    try
                    {
                        List<Kline>? klines = null;
                        // Try recent-only lookbacks first, then expand
                        var tryCounts = new[] { 100, 250, 500, 1000 };
                        foreach (var count in tryCounts)
                        {
                            var tryStart = DateTime.UtcNow.AddMinutes(-minutesPerCandle * count);
                            klines = await BinanceTradingService.FetchHistoricalDataPublic(client, symbol, timeframe, tryStart, DateTime.UtcNow);
                            if (klines != null && klines.Any()) break;
                        }

                        // Final fallback: request latest candles without startTime (limit)
                        if (klines == null || !klines.Any())
                        {
                            try
                            {
                                var req = new RestRequest("/fapi/v1/klines", Method.Get);
                                req.AddParameter("symbol", symbol);
                                req.AddParameter("interval", timeframe);
                                req.AddParameter("limit", 1000);

                                var resp = await client.ExecuteAsync(req);
                                if (resp != null && resp.IsSuccessful && !string.IsNullOrWhiteSpace(resp.Content))
                                {
                                    klines = ParseKlinesFromContent(resp.Content);
                                }
                                else
                                {
                                    string diagMsg = resp == null
                                        ? "No response (null) from /fapi/v1/klines"
                                        : $"Status: {resp.StatusCode} Error: {resp.ErrorMessage ?? "(no message)"} ContentLength: {resp.Content?.Length ?? 0}";
                                    return (symbol, (MarketContext?)null, (BTCIndicatorSet?)null, (List<Kline>?)null, new Exception($"No kline data returned. Diagnostic: {diagMsg}"));
                                }
                            }
                            catch (Exception dex)
                            {
                                return (symbol, (MarketContext?)null, (BTCIndicatorSet?)null, (List<Kline>?)null, new Exception($"No kline data returned. Diagnostic exception: {dex.Message}"));
                            }
                        }

                        var closes = klines.Select(k => k.Close).ToList();
                        var latest = klines.Last();
                        var primary = new BTCIndicatorSet
                        {
                            Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(latest.CloseTime).UtcDateTime,
                            Timeframe = timeframe,
                            Price = latest.Close,
                            EMA50 = BTCTrendCalculator.CalculateEMA(closes, 50),
                            EMA100 = BTCTrendCalculator.CalculateEMA(closes, 100),
                            EMA200 = BTCTrendCalculator.CalculateEMA(closes, 200),
                            RSI = BTCTrendCalculator.CalculateRSI(closes),
                            ATR = BTCTrendCalculator.CalculateATR(klines),
                            Volume = klines.Average(k => k.Volume)
                        };

                        var analysis = new BTCTrendAnalysis { Primary1H = primary };
                        var regime = analyzer.AnalyzeCurrentRegime(analysis, klines);

                        var ctx = new MarketContext();
                        ctx.TradingAlignedRegime = regime;
                        ctx.TradingTrendAnalysis = analysis;
                        return (symbol, ctx, primary, klines, (Exception?)null);
                    }
                    catch (Exception ex)
                    {
                        return (symbol, (MarketContext?)null, null, null, ex);
                    }
                }

                var tasks = coins.Select(symbol => AnalyzeSymbolAsync(symbol)).ToArray();

                List<Kline> btcKlines = null;
                if (coins.Any(c => c != "BTCUSDT"))
                {
                    try
                    {
                        var btcTask = AnalyzeSymbolAsync("BTCUSDT");
                        var btcResult = await btcTask;
                        btcKlines = btcResult.klines;
                    }
                    catch (Exception btcEx)
                    {
                        // Log but don't fail the whole analysis
                        Console.WriteLine($"BTC correlation data fetch failed: {btcEx.Message}");
                    }
                }

                var results = await Task.WhenAll(tasks);

                foreach (var (symbol, ctx, primary, klines, error) in results)
                {
                    var border = new Border { Margin = new Thickness(6), Padding = new Thickness(8), CornerRadius = new CornerRadius(6), Background = System.Windows.Media.Brushes.White };
                    var sp = new StackPanel();
                    border.Child = sp;

                    var headerRow = new StackPanel { Orientation = Orientation.Horizontal };
                    var title = new TextBlock { Text = symbol, FontWeight = FontWeights.Bold, FontSize = 14 };
                    headerRow.Children.Add(title);
                    // copy button will be appended after metrics are computed so it can capture local variables
                    sp.Children.Add(headerRow);

                    if (error != null || ctx == null)
                    {
                        sp.Children.Add(new TextBlock { Text = $"Error: {error?.Message ?? "analysis failed"}", Foreground = System.Windows.Media.Brushes.DarkRed });
                        ResultsPanel.Children.Add(border);
                        continue;
                    }

                    var regime = ctx.TradingAlignedRegime;
                    var trend = regime?.Type.ToString() ?? "Unknown";
                    var trendStrength = regime?.TrendStrength.ToString() ?? "N/A";
                    var priceVs200 = regime?.PriceVs200EMA ?? 0m;
                    var rsi = regime?.RSI ?? 0m;
                    var vol = regime?.Volatility.ToString() ?? "N/A";
                    var conf = regime?.OverallConfidence ?? 0;

                    // Historical context
                    var firstClose = klines != null && klines.Any() ? klines.First().Close : 0m;
                    var lastClose = klines != null && klines.Any() ? klines.Last().Close : 0m;
                    var priceChangePct = firstClose == 0 ? 0m : (lastClose - firstClose) / firstClose * 100m;
                    // Efficiency ratio: net change over sum of absolute moves (0-1)
                    decimal efficiency = 0m;
                    if (klines != null && klines.Count > 1)
                    {
                        var sumAbs = klines.Zip(klines.Skip(1), (a, b) => Math.Abs((double)(b.Close - a.Close))).Sum();
                        var netMove = Math.Abs((double)(lastClose - firstClose));
                        efficiency = sumAbs == 0 ? 0m : (decimal)(netMove / sumAbs);
                    }

                    // BTC correlation 
                    decimal? btcCorrelation = null;
                    if (symbol != "BTCUSDT" && btcKlines != null && klines != null)
                    {
                        btcCorrelation = ComputeSimpleBTCCorrelation(klines, btcKlines);
                    }

                    // Right now metrics
                    var ema50 = primary?.EMA50 ?? 0m;
                    var ema200 = primary?.EMA200 ?? 0m;
                    var atr = (primary?.ATR ?? 0m) == 0 ? 1m : (primary?.ATR ?? 1m);
                    var expansionInATR = (primary?.Price ?? 0m) - ema200;
                    expansionInATR = atr != 0 ? expansionInATR / atr : 0m;
                    var avgVol = klines != null && klines.Any() ? klines.Average(k => k.Volume) : 0m;
                    var volRatio = avgVol == 0 ? 0m : ((klines != null && klines.Any()) ? klines.Last().Volume : 0m) / avgVol;

                    // Candles count
                    var candlesCount = klines?.Count ?? 0;

                    // Trend stage (simple heuristic using price vs EMA200 and EMA50)
                    string trendStage = "N/A";
                    var stagePct = 0m;
                    if (primary != null && primary.Price > ema50)
                    {
                        stagePct = Math.Min(1m, (primary.Price - ema50) / (Math.Abs(ema200 - ema50) + 0.0001m));
                        trendStage = stagePct < 0.33m ? "Early" : stagePct < 0.66m ? "Mid" : "Late";
                    }
                    else if (primary != null && primary.Price < ema50)
                    {
                        stagePct = Math.Min(1m, (ema50 - primary.Price) / (Math.Abs(ema200 - ema50) + 0.0001m));
                        trendStage = stagePct < 0.33m ? "Early" : stagePct < 0.66m ? "Mid" : "Late";
                    }

                    // Recommendation mapping
                    string recommendation;
                    if (conf >= 75 && regime != null && regime.Type == BinanceTestnet.MarketAnalysis.MarketRegimeType.BullishTrend)
                        recommendation = "✅ GO";
                    else if (conf >= 75 && regime != null && regime.Type == BinanceTestnet.MarketAnalysis.MarketRegimeType.BearishTrend)
                        recommendation = "✅ GO";
                    else if (conf >= 50) recommendation = "⚠️ CAUTION";
                    else recommendation = "❌ AVOID";

                    // Add copy-card button now that all local variables exist
                    var copyCardBtn2 = new Button { Content = "Copy Card", Width = 80, Margin = new Thickness(8, 0, 0, 0) };
                    copyCardBtn2.Click += (s, ev) =>
                    {
                        try
                        {
                            var sb = new System.Text.StringBuilder();
                            sb.AppendLine(symbol);
                            sb.AppendLine($"Regime: {trend} ({trendStrength})");
                            sb.AppendLine($"Confidence: {conf}% (Trend {regime?.TrendConfidence ?? 0} / Vol {regime?.VolatilityConfidence ?? 0})");
                            // BTC Correlation removed from card
                            sb.AppendLine($"Candles analyzed: {candlesCount}");
                            sb.AppendLine($"Trend Stage: {trendStage} ({stagePct:P0})");
                            sb.AppendLine($"RSI(14): {(primary?.RSI ?? 0):F0}");
                            sb.AppendLine($"Expansion (ATRs): {expansionInATR:F2}");
                            sb.AppendLine($"Volume ratio: {volRatio:F2}x");
                            sb.AppendLine($"Recommendation: {recommendation}");
                            Clipboard.SetText(sb.ToString());
                        }
                        catch { }
                    };
                    headerRow.Children.Add(copyCardBtn2);

                    sp.Children.Add(new TextBlock { Text = $"Regime: {trend} ({trendStrength})" });
                    sp.Children.Add(new TextBlock { Text = $"Confidence: {conf}% (Trend {regime?.TrendConfidence ?? 0} / Vol {regime?.VolatilityConfidence ?? 0})" });

                    sp.Children.Add(new TextBlock { Text = "-- Historical Context (last 1000) --", FontWeight = FontWeights.SemiBold });
                    sp.Children.Add(new TextBlock { Text = $"Trend Strength Score: {regime.TrendConfidence / 100.0m:F2} (0-1)" });
                    sp.Children.Add(new TextBlock { Text = $"Direction & Change: {trend} {priceChangePct:F2}%" });
                    sp.Children.Add(new TextBlock { Text = $"Trend Quality (efficiency): {efficiency:F2}" });
                    if (btcCorrelation.HasValue)
                    {
                        sp.Children.Add(new TextBlock { Text = $"BTC Correlation: {btcCorrelation.Value:F2}" });
                    }
                    sp.Children.Add(new TextBlock { Text = $"Candles analyzed: {candlesCount}" });

                    sp.Children.Add(new TextBlock { Text = "-- Right Now --", FontWeight = FontWeights.SemiBold });
                    sp.Children.Add(new TextBlock { Text = $"Trend Stage: {trendStage} ({stagePct:P0})" });
                    sp.Children.Add(new TextBlock { Text = $"RSI(14): {primary.RSI:F0} ({(primary.RSI > 70 ? "Overbought" : primary.RSI < 30 ? "Oversold" : "Neutral")})" });
                    sp.Children.Add(new TextBlock { Text = $"Expansion: {expansionInATR:F2} ATRs" });
                    sp.Children.Add(new TextBlock { Text = $"Volume (last vs avg): {volRatio:F2}x" });

                    sp.Children.Add(new TextBlock { Text = $"Recommendation: {recommendation}", FontWeight = FontWeights.Bold });

                    // Color-code card based on recommendation
                    border.Background = recommendation.StartsWith("✅") ? Brushes.LightGreen
                        : recommendation.StartsWith("⚠️") ? Brushes.LightYellow
                        : Brushes.LightCoral;

                    ResultsPanel.Children.Add(border);
                }

                ProgressText.Text = $"Done ({results.Length} coins)";
            }
            catch (Exception ex)
            {
                ProgressText.Text = "Failed";
                MessageBox.Show(this, ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                RunButton.IsEnabled = true;
            }
        }

        private int GetMinutesForTimeframe(string tf)
        {
            return tf?.ToLower() switch
            {
                "1m" => 1,
                "5m" => 5,
                "15m" => 15,
                "30m" => 30,
                "1h" => 60,
                "4h" => 240,
                _ => 5
            };
        }

        // Compute timestamp-aligned Pearson correlation on log-returns with tolerant matching.
        // We match timestamps allowing nearest-key matches within one interval to tolerate small alignment gaps.
        private static (double? corr, int matched) ComputeTimestampAlignedCorrelation(List<Kline> a, List<Kline> b, int minMatches = 50)
        {
            if (a == null || b == null) return (null, 0);

            // Build returns keyed by CloseTime (log-returns)
            var returnsA = new Dictionary<long, double>();
            for (int i = 1; i < a.Count; i++)
            {
                var prev = a[i - 1];
                var cur = a[i];
                if (prev.Close <= 0 || cur.Close <= 0) continue;
                returnsA[cur.CloseTime] = Math.Log((double)cur.Close / (double)prev.Close);
            }

            var returnsB = new Dictionary<long, double>();
            for (int i = 1; i < b.Count; i++)
            {
                var prev = b[i - 1];
                var cur = b[i];
                if (prev.Close <= 0 || cur.Close <= 0) continue;
                returnsB[cur.CloseTime] = Math.Log((double)cur.Close / (double)prev.Close);
            }

            if (returnsA.Count == 0 || returnsB.Count == 0) return (null, 0);

            // Prepare sorted keys for B for nearest lookup
            var keysB = returnsB.Keys.OrderBy(k => k).ToArray();

            // Estimate typical interval (use median diff from B if available else A)
            long intervalMs = EstimateMedianInterval(keysB);
            if (intervalMs <= 0)
            {
                var keysA = returnsA.Keys.OrderBy(k => k).ToArray();
                intervalMs = EstimateMedianInterval(keysA);
            }
            if (intervalMs <= 0) intervalMs = 60 * 1000; // fallback 1m

            // Relax tolerance to two intervals to improve match rate when series are nearly aligned
            long tolerance = intervalMs * 2; // allow nearest within two candle intervals
            Console.WriteLine($"ComputeTimestampAlignedCorrelation: returnsA={returnsA.Count}, returnsB={returnsB.Count}, intervalMs={intervalMs}, toleranceMs={tolerance}");

            var matchedPairs = new List<(double a, double b)>();

            var sortedKeysA = returnsA.Keys.OrderBy(k => k).ToArray();
            foreach (var kA in sortedKeysA)
            {
                // try exact match first
                if (returnsB.TryGetValue(kA, out var rb))
                {
                    matchedPairs.Add((returnsA[kA], rb));
                    continue;
                }

                // binary search nearest in keysB
                int idx = Array.BinarySearch(keysB, kA);
                if (idx < 0) idx = ~idx;
                long nearestKey = -1;
                long bestDiff = long.MaxValue;
                if (idx < keysB.Length)
                {
                    var diff = Math.Abs(keysB[idx] - kA);
                    if (diff < bestDiff) { bestDiff = diff; nearestKey = keysB[idx]; }
                }
                if (idx - 1 >= 0)
                {
                    var diff = Math.Abs(keysB[idx - 1] - kA);
                    if (diff < bestDiff) { bestDiff = diff; nearestKey = keysB[idx - 1]; }
                }

                if (nearestKey >= 0 && bestDiff <= tolerance)
                {
                    matchedPairs.Add((returnsA[kA], returnsB[nearestKey]));
                }
            }

            int n = matchedPairs.Count;
            if (n < minMatches) return (null, n);

            // compute Pearson on matched pairs
            double sumA = matchedPairs.Sum(p => p.a);
            double sumB = matchedPairs.Sum(p => p.b);
            double meanA = sumA / n;
            double meanB = sumB / n;
            double cov = 0, varA = 0, varB = 0;
            foreach (var p in matchedPairs)
            {
                var da = p.a - meanA;
                var db = p.b - meanB;
                cov += da * db;
                varA += da * da;
                varB += db * db;
            }
            var denom = Math.Sqrt(varA * varB);
            if (denom <= double.Epsilon) return (null, n);
            var corr = cov / denom;
            return (corr, n);
        }

        private static long EstimateMedianInterval(long[] keys)
        {
            if (keys == null || keys.Length < 2) return 0;
            var diffs = new List<long>();
            for (int i = 1; i < keys.Length; i++) diffs.Add(keys[i] - keys[i - 1]);
            diffs.Sort();
            return diffs[diffs.Count / 2];
        }

        private static string UnixMsToUtc(long ms)
        {
            try
            {
                return DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime.ToString("yyyy-MM-dd HH:mm");
            }
            catch
            {
                return ms.ToString();
            }
        }

        // Index-aligned correlation fallback: align last N bars by index and compute Pearson on log-returns.
        private static (double? corr, int matched) ComputeIndexAlignedCorrelation(List<Kline> a, List<Kline> b, int targetMatches = 950)
        {
            if (a == null || b == null) return (null, 0);

            // Determine how many bars to use so that returns length is targetMatches if possible
            int maxBars = Math.Min(a.Count, b.Count);
            int desiredBars = Math.Min(maxBars, targetMatches + 1); // need bars = matches+1
            if (desiredBars < 2) return (null, 0);

            // take last desiredBars from each
            var sliceA = a.Skip(a.Count - desiredBars).ToList();
            var sliceB = b.Skip(b.Count - desiredBars).ToList();

            var returnsA = new List<double>();
            for (int i = 1; i < sliceA.Count; i++)
            {
                var prev = sliceA[i - 1];
                var cur = sliceA[i];
                if (prev.Close <= 0 || cur.Close <= 0) continue;
                returnsA.Add(Math.Log((double)cur.Close / (double)prev.Close));
            }

            var returnsB = new List<double>();
            for (int i = 1; i < sliceB.Count; i++)
            {
                var prev = sliceB[i - 1];
                var cur = sliceB[i];
                if (prev.Close <= 0 || cur.Close <= 0) continue;
                returnsB.Add(Math.Log((double)cur.Close / (double)prev.Close));
            }

            int n = Math.Min(returnsA.Count, returnsB.Count);
            if (n < 2) return (null, n);

            // align by last n
            var ra = returnsA.Skip(returnsA.Count - n).ToArray();
            var rb = returnsB.Skip(returnsB.Count - n).ToArray();

            double meanA = ra.Average();
            double meanB = rb.Average();
            double cov = 0, varA = 0, varB = 0;
            for (int i = 0; i < n; i++)
            {
                var da = ra[i] - meanA;
                var db = rb[i] - meanB;
                cov += da * db;
                varA += da * da;
                varB += db * db;
            }
            var denom = Math.Sqrt(varA * varB);
            if (denom <= double.Epsilon) return (null, n);
            return (cov / denom, n);
        }

        private void InfoButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var help = new PreFlightHelpWindow { Owner = this };
                help.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Unable to show help: " + ex.Message, "Help error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static decimal? ComputeSimpleBTCCorrelation(List<Kline> currentCoinKlines, List<Kline> btcKlines)
        {
            if (currentCoinKlines == null || btcKlines == null || currentCoinKlines.Count < 50 || btcKlines.Count < 50)
                return null;

            try
            {
                // Simple approach: Use the last 200 candles and align by close time
                var coinSlice = currentCoinKlines.TakeLast(200).ToList();
                var btcSlice = btcKlines.TakeLast(200).ToList();
                
                // Build dictionaries for quick lookup by close time
                var btcByTime = btcSlice.ToDictionary(k => k.CloseTime, k => k.Close);
                
                var coinReturns = new List<decimal>();
                var btcReturns = new List<decimal>();
                
                // Calculate returns for matching timestamps
                for (int i = 1; i < coinSlice.Count; i++)
                {
                    var currentCandle = coinSlice[i];
                    var prevCandle = coinSlice[i - 1];
                    
                    if (btcByTime.TryGetValue(currentCandle.CloseTime, out decimal btcClose) &&
                        btcByTime.TryGetValue(prevCandle.CloseTime, out decimal btcPrevClose))
                    {
                        // Calculate returns (percentage change)
                        decimal coinReturn = (currentCandle.Close - prevCandle.Close) / prevCandle.Close;
                        decimal btcReturn = (btcClose - btcPrevClose) / btcPrevClose;
                        
                        coinReturns.Add(coinReturn);
                        btcReturns.Add(btcReturn);
                    }
                }
                
                if (coinReturns.Count < 30) return null; // Need minimum data
                
                // Calculate correlation
                return CalculateCorrelation(coinReturns, btcReturns);
            }
            catch
            {
                return null;
            }
        }

        private static decimal CalculateCorrelation(List<decimal> x, List<decimal> y)
        {
            if (x.Count != y.Count) return 0;
            
            decimal meanX = x.Average();
            decimal meanY = y.Average();
            
            decimal numerator = 0;
            decimal denomX = 0;
            decimal denomY = 0;
            
            for (int i = 0; i < x.Count; i++)
            {
                decimal diffX = x[i] - meanX;
                decimal diffY = y[i] - meanY;
                
                numerator += diffX * diffY;
                denomX += diffX * diffX;
                denomY += diffY * diffY;
            }
            
            if (denomX == 0 || denomY == 0) return 0;
            
            return numerator / (decimal)(Math.Sqrt((double)(denomX * denomY)));
        }

        private static List<Kline> ParseKlinesFromContent(string content)
        {
            var result = new List<Kline>();
            try
            {
                var klineData = JsonConvert.DeserializeObject<List<List<object>>>(content);
                if (klineData == null) return result;

                foreach (var kline in klineData)
                {
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

                    result.Add(new Kline
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
            catch
            {
                // swallow parsing errors - caller will handle empty list
            }
            return result;
        }
    }
}
