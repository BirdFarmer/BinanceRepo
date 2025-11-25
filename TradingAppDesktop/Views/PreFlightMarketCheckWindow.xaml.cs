using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using BinanceTestnet.MarketAnalysis;
using BinanceTestnet.Models;
using BinanceTestnet.Utilities;
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
                // Client for public kline fetches
                var client = new RestClient("https://fapi.binance.com");

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
                        var ctx = new MarketContext();
                        // Do not call shared MarketContextAnalyzer here — build regime locally in the UI processing step
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

                    // We'll build a local MarketRegime from the timeframe-local indicators and scores below.

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
                    // Compute quote-volume (USDT) for UI comparisons: prefer quote volume if available, otherwise baseVolume * closePrice
                    decimal GetQuoteVolume(Kline k) => (k == null) ? 0m : (k.Volume * k.Close);
                    var avgVol = klines != null && klines.Any() ? klines.Average(k => GetQuoteVolume(k)) : 0m; // avg quote-volume (USDT)
                    // Use the previous closed candle for "last" volume to avoid including an in-progress bar
                    Kline lastClosedKline = null;
                    if (klines != null && klines.Count > 1) lastClosedKline = klines[klines.Count - 2];
                    else if (klines != null && klines.Count == 1) lastClosedKline = klines[0];
                    var lastQuoteVol = lastClosedKline != null ? GetQuoteVolume(lastClosedKline) : 0m;
                    var volRatio = avgVol == 0 ? 0m : lastQuoteVol / avgVol;

                    // Candles count
                    var candlesCount = klines?.Count ?? 0;

                    // Trend stage (ATR-based mapping: Early <1 ATR, Mid 1-2 ATRs, Extended >2 ATRs)
                    string trendStage = "N/A";
                    var stagePct = 0m;
                    if (primary != null)
                    {
                        // prefer EMA50 as the reference; fallback to EMA200
                        var reference = ema50 != 0m ? ema50 : ema200;
                        trendStage = PreFlightUtils.GetStageLabel(primary.Price, reference, atr);
                        stagePct = PreFlightUtils.GetStagePct(primary.Price, reference, atr) / 100m; // convert to 0-1 for display where needed
                    }

                    // Local PreFlight confidence scoring (timeframe-specific and symmetric for bull/bear)
                    decimal ComputeTrendConfidenceLocal(List<Kline> klns, BTCIndicatorSet p, decimal eff)
                    {
                        if (klns == null || p == null) return 50m;
                        int score = 0;

                        // 1) EMA alignment (0-30)
                        bool ema50Gt100 = p.EMA50 > p.EMA100;
                        bool ema100Gt200 = p.EMA100 > p.EMA200;
                        bool ema50Lt100 = p.EMA50 < p.EMA100;
                        bool ema100Lt200 = p.EMA100 < p.EMA200;
                        if ((ema50Gt100 && ema100Gt200) || (ema50Lt100 && ema100Lt200)) score += 30;
                        else if (ema50Gt100 || ema100Gt200 || ema50Lt100 || ema100Lt200) score += 15;

                        // 2) EMA spread strength (0-20)
                        var denom = Math.Max(1m, Math.Abs(p.EMA200));
                        var emaSpreadPct = (p.EMA50 - p.EMA200) / denom;
                        var absEmaSpreadPct = Math.Abs(emaSpreadPct);
                        if (absEmaSpreadPct >= 0.03m) score += 20;
                        else if (absEmaSpreadPct >= 0.01m) score += 10;

                        // 3) RSI momentum (0-20)
                        if (p.RSI >= 70m || p.RSI <= 30m) score += 20;
                        else if (p.RSI >= 60m || p.RSI <= 40m) score += 15;

                        // 4) Efficiency (0-15)
                        if (eff >= 0.4m) score += 15;
                        else if (eff >= 0.2m) score += 8;

                        // 5) Recent bar confirmation (0-15) - last 5 closes monotonic or majority
                        int monotonicCount = 0;
                        if (klns.Count >= 5)
                        {
                            var last5 = klns.Skip(klns.Count - 5).Select(k => k.Close).ToList();
                            bool inc = true; bool dec = true;
                            for (int i = 1; i < last5.Count; i++)
                            {
                                if (last5[i] <= last5[i - 1]) inc = false;
                                if (last5[i] >= last5[i - 1]) dec = false;
                            }
                            if (inc || dec) monotonicCount = 5;
                            else
                            {
                                int incC = 0, decC = 0;
                                for (int i = 1; i < last5.Count; i++)
                                {
                                    if (last5[i] > last5[i - 1]) incC++; else if (last5[i] < last5[i - 1]) decC++;
                                }
                                monotonicCount = Math.Max(incC, decC);
                            }
                        }
                        if (monotonicCount >= 4) score += 15;
                        else if (monotonicCount >= 3) score += 8;

                        return Math.Min(100, Math.Max(0, score));
                    }

                    decimal ComputeVolatilityConfidenceLocal(List<Kline> klns, decimal currentAtr, decimal avgQuoteVol, decimal lastQVol)
                    {
                        if (klns == null) return 50m;
                        int score = 0;

                        // 1) ATR ratio vs ATR moving average (0-40)
                        decimal atrMA = 0m;
                        try
                        {
                            var atrVals = BTCTrendCalculator.CalculateATRValues(klns, 14, 50);
                            if (atrVals != null && atrVals.Any()) atrMA = atrVals.Average();
                        }
                        catch { }

                        decimal atrRatio = 1.0m;
                        if (atrMA > 0) atrRatio = currentAtr / atrMA;
                        if (atrMA > 0 && atrRatio >= 0.7m && atrRatio <= 1.3m) score += 40;
                        else if (atrMA > 0 && ((atrRatio >= 0.5m && atrRatio < 0.7m) || (atrRatio > 1.3m && atrRatio <= 1.7m))) score += 20;

                        // 2) ATR absolute relative to price (0-20)
                        var latestPrice = klns.LastOrDefault()?.Close ?? 1m;
                        var atrPct = latestPrice > 0 ? currentAtr / latestPrice : 0m;
                        if (atrPct > 0 && atrPct < 0.02m) score += 20;
                        else if (atrPct >= 0.02m && atrPct < 0.05m) score += 10;

                        // 3) Volume sanity (0-20)
                        if (avgQuoteVol > 0)
                        {
                            var ratio = lastQVol / avgQuoteVol;
                            if (ratio >= 0.5m && ratio <= 2.0m) score += 20;
                            else if ((ratio >= 0.3m && ratio < 0.5m) || (ratio > 2.0m && ratio <= 3.0m)) score += 10;
                        }

                        // 4) Volatility stability (std dev of returns) (0-20)
                        try
                        {
                            var returns = new List<decimal>();
                            for (int i = 1; i < klns.Count; i++)
                            {
                                if (klns[i - 1].Close > 0) returns.Add((klns[i].Close - klns[i - 1].Close) / klns[i - 1].Close);
                            }
                            if (returns.Any())
                            {
                                var mean = returns.Average();
                                var variance = returns.Sum(r => (double)(r - mean) * (double)(r - mean)) / returns.Count;
                                var stddev = (decimal)Math.Sqrt(variance);
                                if (stddev < 0.002m) score += 20;
                                else if (stddev < 0.01m) score += 10;
                            }
                        }
                        catch { }

                        return Math.Min(100, Math.Max(0, score));
                    }

                    // Compute local scores
                    var trendConfLocal = ComputeTrendConfidenceLocal(klines, primary, efficiency);
                    var volConfLocal = ComputeVolatilityConfidenceLocal(klines, atr, avgVol, lastQuoteVol);
                    var overallLocal = (trendConfLocal + volConfLocal) / 2m;

                    // Volume warning level
                    var volWarningLevel = PreFlightUtils.GetVolumeWarningLevel(volRatio); // 0 none, 1 low, 2 critical

                    // Build a local MarketRegime using the local scores and indicators
                    var localRegime = new BinanceTestnet.MarketAnalysis.MarketRegime
                    {
                        AnalysisTime = DateTime.UtcNow,
                        PeriodStart = klines != null && klines.Any() ? DateTimeOffset.FromUnixTimeMilliseconds(klines.First().CloseTime).UtcDateTime : DateTime.UtcNow.AddHours(-24),
                        PeriodEnd = klines != null && klines.Any() ? DateTimeOffset.FromUnixTimeMilliseconds(klines.Last().CloseTime).UtcDateTime : DateTime.UtcNow,
                        PriceVs200EMA = primary?.PriceVs200EMA ?? 0m,
                        RSI = primary?.RSI ?? 0m,
                        VolumeRatio = volRatio,
                        TrendConfidence = (int)Math.Max(0, Math.Min(100, trendConfLocal)),
                        VolatilityConfidence = (int)Math.Max(0, Math.Min(100, volConfLocal)),
                        OverallConfidence = (int)Math.Max(0, Math.Min(100, Math.Round(overallLocal))),
                        DominantTimeframe = timeframe
                    };

                    // ATR ratio (compare current ATR to ATR moving average)
                    try
                    {
                        var atrVals = BTCTrendCalculator.CalculateATRValues(klines, 14, 20);
                        if (atrVals != null && atrVals.Any())
                        {
                            var atrMA = atrVals.Average();
                            localRegime.ATRRatio = atrMA > 0 ? (atr / atrMA) : 1.0m;
                        }
                        else
                        {
                            localRegime.ATRRatio = 1.0m;
                        }
                    }
                    catch { localRegime.ATRRatio = 1.0m; }

                    // Volatility level mapping (match analyzer mapping)
                    localRegime.Volatility = localRegime.ATRRatio switch
                    {
                        > 2.0m => BinanceTestnet.MarketAnalysis.VolatilityLevel.VeryHigh,
                        > 1.5m => BinanceTestnet.MarketAnalysis.VolatilityLevel.High,
                        > 1.2m => BinanceTestnet.MarketAnalysis.VolatilityLevel.Elevated,
                        > 0.8m => BinanceTestnet.MarketAnalysis.VolatilityLevel.Normal,
                        _ => BinanceTestnet.MarketAnalysis.VolatilityLevel.Low
                    };

                    // Trend classification
                    if (localRegime.ATRRatio > 2.0m)
                    {
                        localRegime.Type = BinanceTestnet.MarketAnalysis.MarketRegimeType.HighVolatility;
                    }
                    else if (primary != null && primary.IsAlignedBullish && localRegime.PriceVs200EMA > 0 && localRegime.RSI > 40m)
                    {
                        localRegime.Type = BinanceTestnet.MarketAnalysis.MarketRegimeType.BullishTrend;
                    }
                    else if (primary != null && primary.IsAlignedBearish && localRegime.PriceVs200EMA < 0 && localRegime.RSI < 60m)
                    {
                        localRegime.Type = BinanceTestnet.MarketAnalysis.MarketRegimeType.BearishTrend;
                    }
                    else
                    {
                        localRegime.Type = BinanceTestnet.MarketAnalysis.MarketRegimeType.RangingMarket;
                    }

                    // Fix 1: Choppy override — if efficiency is low and we have enough candles, force Ranging/Choppy
                    if (PreFlightUtils.IsChoppy(efficiency, candlesCount))
                    {
                        localRegime.Type = BinanceTestnet.MarketAnalysis.MarketRegimeType.RangingMarket;
                        trendStage = "Choppy";
                    }

                    // Trend strength mapping from local trend confidence
                    if (trendConfLocal >= 80m) localRegime.TrendStrength = BinanceTestnet.MarketAnalysis.TrendStrength.VeryStrong;
                    else if (trendConfLocal >= 60m) localRegime.TrendStrength = BinanceTestnet.MarketAnalysis.TrendStrength.Strong;
                    else if (trendConfLocal >= 40m) localRegime.TrendStrength = BinanceTestnet.MarketAnalysis.TrendStrength.Moderate;
                    else if (trendConfLocal >= 25m) localRegime.TrendStrength = BinanceTestnet.MarketAnalysis.TrendStrength.Weak;
                    else localRegime.TrendStrength = BinanceTestnet.MarketAnalysis.TrendStrength.Neutral;

                    // Persist local regime into context for downstream uses if needed
                    ctx.TradingAlignedRegime = localRegime;

                    // Recommendation mapping (symmetric for both Bullish and Bearish)
                    string recommendation;
                    if (overallLocal >= 70m && (localRegime.Type == BinanceTestnet.MarketAnalysis.MarketRegimeType.BullishTrend || localRegime.Type == BinanceTestnet.MarketAnalysis.MarketRegimeType.BearishTrend))
                        recommendation = "✅ GO";
                    else if (overallLocal >= 50m) recommendation = "⚠️ CAUTION";
                    else recommendation = "❌ AVOID";

                    // Build the card payload (used by individual copy and Copy All)
                    Func<string> BuildCardPayload = () =>
                    {
                        var sb = new System.Text.StringBuilder();
                        sb.AppendLine("==== Pre-Flight Card ====");
                        sb.AppendLine(symbol + "    " + timeframe + "    " + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm 'UTC'"));
                        sb.AppendLine($"Regime: {localRegime.Type} ({localRegime.TrendStrength})");
                        sb.AppendLine($"Confidence: {localRegime.OverallConfidence}% (Trend {localRegime.TrendConfidence} / Vol {localRegime.VolatilityConfidence})");
                        sb.AppendLine("-- Historical Context --");
                        sb.AppendLine($"Candles analyzed: {candlesCount}");
                        sb.AppendLine($"First close: {firstClose:F8}");
                        sb.AppendLine($"Last close: {lastClose:F8}");
                        sb.AppendLine($"Direction & Change: {localRegime.Type} {priceChangePct:F2}%");
                        sb.AppendLine($"Trend Strength Score: {(localRegime.TrendConfidence / 100.0m):F2} (0-1)");
                        sb.AppendLine($"Trend Quality (efficiency): {efficiency:F2}");
                        if (btcCorrelation.HasValue)
                        {
                            sb.AppendLine($"BTC Correlation: {btcCorrelation.Value:P2}");
                        }

                        sb.AppendLine("-- Right Now --");
                        sb.AppendLine($"Price: {(primary?.Price ?? 0m):F8}");
                        sb.AppendLine($"EMA50: {ema50:F8}  EMA200: {ema200:F8}");
                        sb.AppendLine($"ATR: {(primary?.ATR ?? 0m):F8}");
                        sb.AppendLine($"Expansion (ATRs): {expansionInATR:F2}");
                        sb.AppendLine($"Trend Stage: {trendStage} ({stagePct:P0})");
                        sb.AppendLine($"RSI(14): {(primary?.RSI ?? 0):F0} {(primary != null ? (primary.RSI > 70 ? "(Overbought)" : primary.RSI < 30 ? "(Oversold)" : "(Neutral)") : "")}");
                        // volume: report percent diff only (user preference)
                        var lastVol = lastQuoteVol;
                        var avgVolDisplay = avgVol; // already quote-volume
                        var volPctDiff = avgVolDisplay == 0 ? 0m : (lastVol - avgVolDisplay) / avgVolDisplay * 100m;
                        sb.AppendLine($"Volume change: {volPctDiff:F1}%");

                        // Volume warning summary (if any)
                        if (volWarningLevel > 0)
                        {
                            var warnText = volWarningLevel == 2 ? "CRITICAL: volume far below expected" : "LOW: volume below typical";
                            sb.AppendLine($"Volume Warning: {warnText} (change {volPctDiff:F1}%)");
                        }

                        // Choppy note
                        if (PreFlightUtils.IsChoppy(efficiency, candlesCount)) sb.AppendLine("Market flagged as CHOPPY (low efficiency)");

                        sb.AppendLine($"Recommendation: {recommendation}");
                        sb.AppendLine("=========================");
                        return sb.ToString();
                    };

                    var copyCardBtn2 = new Button { Content = "Copy Card", Width = 100, Margin = new Thickness(8, 0, 0, 0) };
                    copyCardBtn2.Click += (s, ev) =>
                    {
                        try
                        {
                            var payload = BuildCardPayload();
                            try { border.Tag = payload; } catch { }
                            Clipboard.SetText(payload);
                        }
                        catch { }
                    };
                    headerRow.Children.Add(copyCardBtn2);

                    // store the initial payload on the border so Copy All can aggregate without individual clicks
                    try { border.Tag = BuildCardPayload(); } catch { }

                    // show volume warning banner if needed
                    if (volWarningLevel > 0)
                    {
                        var warnText = volWarningLevel == 2 ? "CRITICAL: Volume far below typical — low liquidity" : "Low volume vs recent average — exercise caution";
                        var warnBlock = new TextBlock { Text = warnText, Foreground = Brushes.White, Padding = new Thickness(6), Margin = new Thickness(0,6,0,6) };
                        warnBlock.Background = volWarningLevel == 2 ? Brushes.DarkRed : Brushes.Orange;
                        sp.Children.Insert(1, warnBlock); // just under header
                    }

                    sp.Children.Add(new TextBlock { Text = $"Regime: {localRegime.Type} ({localRegime.TrendStrength})" });
                    sp.Children.Add(new TextBlock { Text = $"Confidence: {localRegime.OverallConfidence}% (Trend {localRegime.TrendConfidence} / Vol {localRegime.VolatilityConfidence})" });

                    sp.Children.Add(new TextBlock { Text = "-- Historical Context (last 1000) --", FontWeight = FontWeights.SemiBold });
                    sp.Children.Add(new TextBlock { Text = $"Trend Strength Score: {localRegime.TrendConfidence / 100.0m:F2} (0-1)" });
                    sp.Children.Add(new TextBlock { Text = $"Direction & Change: {localRegime.Type} {priceChangePct:F2}%" });
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
                    // show only percent diff for quick readability (user preference)
                    var uiVolPctDiff = avgVol == 0 ? 0m : (lastQuoteVol - avgVol) / avgVol * 100m;
                    sp.Children.Add(new TextBlock { Text = $"Volume change: {uiVolPctDiff:F1}%" });

                    sp.Children.Add(new TextBlock { Text = $"Recommendation: {recommendation}", FontWeight = FontWeights.Bold });
                    sp.Children.Add(new TextBlock { Text = $"Local Scores — Trend: {trendConfLocal}  Vol: {volConfLocal}  Overall: {overallLocal:F0}", FontStyle = FontStyles.Italic, Foreground = Brushes.DarkSlateGray });

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

        private void CopyAllButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var sb = new System.Text.StringBuilder();
                foreach (var child in ResultsPanel.Children)
                {
                    if (child is Border b && b.Tag is string payload)
                    {
                        sb.AppendLine(payload);
                        sb.AppendLine();
                    }
                }

                var text = sb.ToString();
                if (string.IsNullOrWhiteSpace(text))
                {
                    MessageBox.Show(this, "No card data to copy. Run an analysis first.", "Copy All", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                try { Clipboard.SetText(text); } catch { }
                MessageBox.Show(this, "Copied all cards to clipboard.", "Copy All", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Copy All failed: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
