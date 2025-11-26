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
                    // long-run volume baseline (200 bars) for context
                    decimal ma200Vol = 0m;
                    if (klines != null && klines.Any())
                    {
                        int maN = Math.Min(200, klines.Count);
                        ma200Vol = klines.Skip(Math.Max(0, klines.Count - maN)).Average(k => GetQuoteVolume(k));
                    }
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
                        sb.AppendLine($"Confidence: {localRegime.OverallConfidence}% (Trend {localRegime.TrendConfidence}% / Vol {localRegime.VolatilityConfidence}%)");
                        sb.AppendLine("-- Historical Context --");
                        sb.AppendLine($"Candles analyzed: {candlesCount}");
                        sb.AppendLine($"First close: {firstClose:F8}");
                        sb.AppendLine($"Last close: {lastClose:F8}");
                        sb.AppendLine($"Direction & Change: {localRegime.Type} {priceChangePct:F2}%");
                        sb.AppendLine($"Trend Strength Score: {localRegime.TrendConfidence}%");
                        sb.AppendLine($"Trend Quality (efficiency): {(efficiency * 100m):F1}%");
                        if (btcCorrelation.HasValue)
                        {
                            sb.AppendLine($"BTC Correlation: {btcCorrelation.Value:P1}");
                        }

                        sb.AppendLine("-- Right Now --");
                        sb.AppendLine($"Price: {(primary?.Price ?? 0m):F8}");
                        sb.AppendLine($"EMA50: {ema50:F8}  EMA200: {ema200:F8}");
                        var atrVal = (primary?.ATR ?? 0m);
                        var priceVal = (primary?.Price ?? 1m);
                        var atrPct = priceVal != 0m ? (atrVal / priceVal) * 100m : 0m;
                        sb.AppendLine($"ATR: {atrVal:F2} ({atrPct:F2}%)");
                        // compute expansion percent relative to EMA50/EMA200 reference for human-friendly context
                        var reference = ema50 != 0m ? ema50 : ema200;
                        var expansionPct = reference != 0m ? ((primary?.Price ?? 0m) - reference) / reference * 100m : 0m;
                        sb.AppendLine($"Expansion (ATRs): {expansionInATR:F2} ATRs (≈ {expansionPct:F2}%)");
                        sb.AppendLine($"Trend Stage: {trendStage} ({stagePct:P0})");
                        sb.AppendLine($"RSI(14): {(primary?.RSI ?? 0):F0} {(primary != null ? (primary.RSI > 70 ? "(Overbought)" : primary.RSI < 30 ? "(Oversold)" : "(Neutral)") : "")}");
                        // volume: report percent diff only (user preference)
                        var lastVol = lastQuoteVol;
                        var avgVolDisplay = avgVol; // already quote-volume
                        var volPctDiff = avgVolDisplay == 0 ? 0m : (lastVol - avgVolDisplay) / avgVolDisplay * 100m;
                            sb.AppendLine($"Volume (vs short-term avg): {volPctDiff:F1}%");
                            sb.AppendLine($"Volume (vs 200-bar MA): {(ma200Vol == 0m ? 0m : (lastVol - ma200Vol) / ma200Vol * 100m):F1}%");

                        // Volume warning summary (if any)
                        if (volWarningLevel > 0)
                        {
                            var warnText = volWarningLevel == 2 ? "CRITICAL: volume far below expected" : "LOW: volume below typical";
                            sb.AppendLine($"Volume Warning: {warnText} (change {volPctDiff:F1}%)");
                        }

                        // Choppy note
                        if (PreFlightUtils.IsChoppy(efficiency, candlesCount)) sb.AppendLine("Market flagged as CHOPPY (low efficiency)");

                        sb.AppendLine();
                        sb.AppendLine($"Recommendation: {recommendation}");
                        sb.AppendLine();
                        sb.AppendLine("-- Local Scores --");
                        sb.AppendLine($"Trend: {trendConfLocal:F0}%  Vol: {volConfLocal:F0}%  Overall: {Math.Round(overallLocal):F0}%");
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

                    // compute ui percent volume change early for banner and UI
                    var uiVolPctDiff = avgVol == 0 ? 0m : (lastQuoteVol - avgVol) / avgVol * 100m;
                    // show volume warning banner if needed
                    if (volWarningLevel > 0)
                    {
                        var warnText = volWarningLevel == 2
                            ? $"CRITICAL: Volume far below typical (change {uiVolPctDiff:F1}%) — low liquidity"
                            : $"LOW: volume below typical (change {uiVolPctDiff:F1}%)";
                        var warnBlock = new TextBlock { Text = warnText, Foreground = Brushes.White, Padding = new Thickness(6), Margin = new Thickness(0,6,0,6) };
                        warnBlock.Background = volWarningLevel == 2 ? Brushes.DarkRed : Brushes.Orange;
                        sp.Children.Insert(1, warnBlock); // just under header
                    }

                    sp.Children.Add(new TextBlock { Text = $"Regime: {localRegime.Type} ({localRegime.TrendStrength})" });
                    sp.Children.Add(new TextBlock { Text = $"Confidence: {localRegime.OverallConfidence}% (Trend {localRegime.TrendConfidence}% / Vol {localRegime.VolatilityConfidence}%)" });

                    sp.Children.Add(new TextBlock { Text = "-- Historical Context (last 1000) --", FontWeight = FontWeights.SemiBold });
                    sp.Children.Add(new TextBlock { Text = $"Trend Strength Score: {localRegime.TrendConfidence}%" });
                    sp.Children.Add(new TextBlock { Text = $"Direction & Change: {localRegime.Type} {priceChangePct:F2}%" });
                    sp.Children.Add(new TextBlock { Text = $"Trend Quality (efficiency): {(efficiency * 100m):F1}%" });
                    if (btcCorrelation.HasValue)
                    {
                        sp.Children.Add(new TextBlock { Text = $"BTC Correlation: {btcCorrelation.Value:P1}" });
                    }
                    sp.Children.Add(new TextBlock { Text = $"Candles analyzed: {candlesCount}" });

                    sp.Children.Add(new TextBlock { Text = "-- Right Now --", FontWeight = FontWeights.SemiBold });
                    sp.Children.Add(new TextBlock { Text = $"Trend Stage: {trendStage} ({stagePct:P0})" });
                    sp.Children.Add(new TextBlock { Text = $"RSI(14): {primary.RSI:F0} ({(primary.RSI > 70 ? "Overbought" : primary.RSI < 30 ? "Oversold" : "Neutral")})" });
                    var atrValUi = (primary?.ATR ?? 0m);
                    var atrPctUi = (primary?.Price ?? 1m) != 0m ? (atrValUi / (primary?.Price ?? 1m)) * 100m : 0m;
                    sp.Children.Add(new TextBlock { Text = $"ATR: {atrValUi:F2} ({atrPctUi:F2}%)" });
                    // Expansion with percent context
                    var referenceUi = ema50 != 0m ? ema50 : ema200;
                    var expansionPctUi = referenceUi != 0m ? ((primary?.Price ?? 0m) - referenceUi) / referenceUi * 100m : 0m;
                    sp.Children.Add(new TextBlock { Text = $"Expansion: {expansionInATR:F2} ATRs (≈ {expansionPctUi:F2}%)" });
                    sp.Children.Add(new TextBlock { Text = $"Volume (vs short-term avg): {uiVolPctDiff:F1}%" });
                    // show last vs 200-bar volume moving average for long-term liquidity context
                    var lastVs200PctUi = ma200Vol == 0m ? 0m : (lastQuoteVol - ma200Vol) / ma200Vol * 100m;
                    sp.Children.Add(new TextBlock { Text = $"Volume (vs 200-bar MA): {lastVs200PctUi:F1}%" });

                    // small visual gap before the recommendation to separate from 'Right Now' section
                    sp.Children.Add(new TextBlock { Text = "", Height = 6 });
                    sp.Children.Add(new TextBlock { Text = $"Recommendation: {recommendation}", FontWeight = FontWeights.Bold });
                    sp.Children.Add(new TextBlock { Text = "", Height = 4 });
                    sp.Children.Add(new TextBlock { Text = $"Local Scores — Trend: {trendConfLocal:F0}%  Vol: {volConfLocal:F0}%  Overall: {overallLocal:F0}%", FontStyle = FontStyles.Italic, Foreground = Brushes.DarkSlateGray });

                    // Color-code card based on recommendation - prefer theme brushes when available
                    try
                    {
                        var app = System.Windows.Application.Current;
                        if (app != null)
                        {
                            if (recommendation.StartsWith("✅") && app.Resources.Contains("SuccessColor"))
                            {
                                border.Background = app.Resources["SuccessColor"] as Brush ?? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0F9D58"));
                            }
                            else if (recommendation.StartsWith("⚠️") && app.Resources.Contains("WarningColor"))
                            {
                                border.Background = app.Resources["WarningColor"] as Brush ?? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"));
                            }
                            else if (app.Resources.Contains("DangerColor"))
                            {
                                border.Background = app.Resources["DangerColor"] as Brush ?? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#B91C1C"));
                            }
                            else
                            {
                                border.Background = recommendation.StartsWith("✅") ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0F9D58"))
                                    : recommendation.StartsWith("⚠️") ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"))
                                    : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#B91C1C"));
                            }
                        }
                        else
                        {
                            border.Background = recommendation.StartsWith("✅") ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0F9D58"))
                                : recommendation.StartsWith("⚠️") ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"))
                                : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#B91C1C"));
                        }
                    }
                    catch
                    {
                        border.Background = recommendation.StartsWith("✅") ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0F9D58"))
                            : recommendation.StartsWith("⚠️") ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"))
                            : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#B91C1C"));
                    }

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

        // Helper methods (timestamp/index correlation, parsing, etc.) were extracted to a
        // partial helper file to keep the codebehind focused and readable.

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

        
    }
}
