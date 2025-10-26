using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using BinanceTestnet.MarketAnalysis;
using BinanceTestnet.Services.Reporting;

namespace BinanceTestnet.Database
{
    public class HtmlReportGenerator
    {
        private readonly TradeLogger _tradeLogger;
        private readonly MarketContextAnalyzer _marketAnalyzer;

        List<MarketRegimeSegment> savedRegimeSegments = new List<MarketRegimeSegment>();

        public HtmlReportGenerator(TradeLogger tradeLogger, MarketContextAnalyzer marketAnalyzer)
        {
            _tradeLogger = tradeLogger;
            _marketAnalyzer = marketAnalyzer;
        }

        private decimal CalculateWinRate(List<Trade> trades)
        {
            if (trades.Count == 0) return 0;
            return (decimal)trades.Count(t => t.Profit > 0) / trades.Count * 100;
        }

        public async Task<string> GenerateHtmlReport(string sessionId, ReportSettings settings)
        {
            try
            {
                var allTrades = _tradeLogger.GetTrades(sessionId);
                if (allTrades == null || !allTrades.Any())
                {
                    return $$"""
                        <!DOCTYPE html>
                        <html>
                        <head><title>No Trade Data</title></head>
                        <body>
                            <h1>📊 No Trade Data Available</h1>
                            <p>Session {{sessionId}} contains no trades to analyze.</p>
                            <p>Please check if trading was active during this session.</p>
                        </body>
                        </html>
                    """;
                }

                // Generate all sections with error isolation
                var sections = new List<ReportSectionResult>
                {
                    GenerateExecutiveSummarySafe(sessionId, settings, allTrades),
                    GenerateStrategyParametersSafe(sessionId, settings),
                    GeneratePerformanceSnapshotSafe(sessionId, settings, allTrades),
                    GenerateStrategyComparisonSafe(sessionId, settings, allTrades),
                    GenerateTradeSimulationSafe(sessionId, settings, allTrades),
                    GeneratePerformanceOverTimeSafe(sessionId, settings, allTrades),
                    GenerateTradeDurationSafe(sessionId, settings, allTrades),
                    GenerateMarketSessionAnalysisSafe(sessionId, settings, allTrades),
                    GenerateRiskAnalysisSafe(sessionId, settings, allTrades),
                    GenerateCoinPerformanceSafe(sessionId, settings, allTrades),
                    GenerateCriticalTradesSafe(sessionId, settings, allTrades),
                    await GenerateBtcMarketContextSafe(sessionId, settings, allTrades),
                    await GenerateRegimeTimelineSafe(sessionId, settings, allTrades),
                    await GenerateStrategyHeatmapSafe(sessionId, settings, allTrades),
                    GenerateActionableInsightsSafe(sessionId, settings, allTrades),
                    GenerateDayOfWeekAnalysisSafe(sessionId, settings, allTrades),
                    GenerateExtendedHoursAnalysisSafe(sessionId, settings, allTrades),
                    GenerateWeekendWeekdayAnalysisSafe(sessionId, settings, allTrades)
                };

                return BuildFinalReport(sessionId, settings, allTrades, sections);
            }
            catch (Exception ex)
            {
                return $$"""
                    <!DOCTYPE html>
                    <html>
                    <head><title>Report Error</title></head>
                    <body>
                        <h1 style="color: #e74c3c;">⚠️ Report Generation Failed</h1>
                        <p>{{ex.Message}}</p>
                        <p>Please check the logs for more details.</p>
                    </body>
                    </html>
                """;
            }
        }

        private string BuildFinalReport(string sessionId, ReportSettings settings, List<Trade> allTrades, List<ReportSectionResult> sections)
        {
            var html = new StringBuilder();

            // HTML Head with improved CSS
            html.AppendLine($$"""
                <!DOCTYPE html>
                <html>
                <head>
                    <title>Trading Report - {{sessionId}}</title>
                        <style>
                            :root {
                                --color-positive: #27ae60;
                                --color-negative: #e74c3c;
                                --color-warning: #ffc107;
                                --color-suggestion: #28a745;
                                --color-primary: #3498db;
                                --color-dark: #2c3e50;
                                --color-light: #f8f9fa;
                            }
                            body { 
                                font-family: 'Segoe UI', Arial, sans-serif; 
                                margin: 20px;
                                color: #333;
                                line-height: 1.6;
                            }
                            .header { 
                                background: var(--color-dark);
                                color: white;
                                padding: 20px;
                                border-radius: 5px;
                                margin-bottom: 20px;
                            }
                            .section { 
                                background: var(--color-light);
                                border-radius: 5px;
                                padding: 15px;
                                margin-bottom: 20px;
                                box-shadow: 0 2px 5px rgba(0,0,0,0.1);
                            }
                            .positive { color: var(--color-positive); }
                            .negative { color: var(--color-negative); }
                            table { 
                                width: 100%; 
                                border-collapse: collapse;
                                margin: 10px 0;
                            }
                            th, td { 
                                padding: 12px;
                                text-align: left;
                                border-bottom: 1px solid #ddd;
                            }
                            th { background-color: var(--color-primary); color: white; }
                            tr:nth-child(even) { background-color: #f2f2f2; }
                            .warning { 
                                background: #fff3cd;
                                padding: 10px;
                                border-left: 4px solid var(--color-warning);
                                margin: 10px 0;
                            }
                            .suggestion { 
                                background: #d4edda;
                                padding: 10px;
                                border-left: 4px solid var(--color-suggestion);
                                margin: 5px 0;
                            }
                            .critical-trade { background-color: #f8d7da !important; }
                            .metric-grid { 
                                display: grid;
                                grid-template-columns: repeat(3, 1fr);
                                gap: 15px;
                            }
                            .metric-card {
                                background: white;
                                padding: 15px;
                                border-radius: 5px;
                                box-shadow: 0 2px 4px rgba(0,0,0,0.1);
                            }
                            .chart-container {
                                width: 100%;
                                height: 300px;
                                margin: 20px 0;
                            }
                            @media print {
                                body { margin: 0; padding: 0; }
                                .section { break-inside: avoid; }
                            }
                            .executive-kpi {
                                background: rgba(255,255,255,0.1) !important;
                                border: 1px solid rgba(255,255,255,0.2) !important;
                                backdrop-filter: blur(10px);
                            }
                            .executive-kpi h3 {
                                color: white !important;
                                font-size: 0.9em;
                                margin-bottom: 8px;
                            }
                            .executive-kpi .positive {
                                color: #a8e6cf !important;
                                font-weight: bold;
                            }
                            .executive-kpi .negative {
                                color: #ff8b94 !important;
                                font-weight: bold;
                            }
                            .executive-summary {
                                background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
                                color: white;
                                border-radius: 8px;
                                box-shadow: 0 4px 15px rgba(0,0,0,0.2);
                            }

                            .executive-kpi {
                                background: rgba(255,255,255,0.1);
                                border: 1px solid rgba(255,255,255,0.2);
                                border-radius: 6px;
                                padding: 15px;
                                backdrop-filter: blur(10px);
                            }

                            .insight-list {
                                background: rgba(255,255,255,0.1);
                                border-radius: 6px;
                                padding: 15px;
                                margin: 15px 0;
                            }

                            .insight-list li {
                                margin-bottom: 10px;
                                line-height: 1.4;
                            }
                            .heatmap-table td {
                                text-align: center;
                                vertical-align: middle;
                                padding: 8px 4px;
                            }
                        </style>
                </head>
                <body>
                    <div class="header">
                        <h1>📊 Trading Report - {{sessionId}}</h1>
                        <p>Generated: {{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}}</p>
                        <p>First Trade: {{allTrades.Min(t => t.EntryTime).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}} UTC</p>
                        <p>Last Trade: {{allTrades.Max(t => t.EntryTime).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}} UTC</p>
                        <p>Duration: {{(allTrades.Max(t => t.EntryTime) - allTrades.Min(t => t.EntryTime)).TotalDays.ToString("F1", CultureInfo.InvariantCulture)}} days</p>
                    </div>
            """);

            // Success rate indicator
            var successfulSections = sections.Count(s => s.Success);
            var totalSections = sections.Count;

            if (successfulSections < totalSections)
            {
                html.AppendLine($$"""
                <div class="warning">
                    <strong>Note:</strong> {{successfulSections}} of {{totalSections}} sections loaded successfully. 
                    Some analytics may be incomplete due to temporary data issues.
                </div>
                """);
            }

            // Append all sections
            foreach (var section in sections)
            {
                html.AppendLine(section.Html);
            }

            // Footer
            html.AppendLine($$"""
                    <div style="text-align: center; margin-top: 30px; color: #7f8c8d; font-size: 0.9em;">
                        <hr>
                        <p>Report generated by BinanceTestnet • {{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}}</p>
                        <p>Note: For best printing results, use Chrome or Edge and select "Background graphics" in print settings.</p>
                    </div>
                </body>
                </html>
            """);

            return html.ToString();
        }

        //Generate the regime timeline
        private string GenerateRegimeTimelineHtml(List<MarketRegimeSegment> segments)
        {
            var html = new StringBuilder();

            html.AppendLine("""
                <div class="section">
                    <h2>📅 BTC Market Regime Timeline</h2>
                    <table>
                        <tr>
                            <th>Period</th>
                            <th>Market Regime</th>
                            <th>Your Trades</th>
                            <th>Performance</th>
                            <th>Insight</th>
                        </tr>
                """);

            foreach (var segment in segments)
            {
                var performanceClass = segment.TotalPnL >= 0 ? "positive" : "negative";
                var performanceText = segment.TotalPnL >= 0 ? $"+{segment.TotalPnL:F1}" : segment.TotalPnL.ToString("F1");
                var insight = GetRegimeInsight(segment);

                html.AppendLine($$"""
                    <tr>
                        <td>{{segment.StartTime:HH:mm}} - {{segment.EndTime:HH:mm}} UTC</td>
                        <td class="{{GetRegimeColorClass(segment.Regime.Type)}}">
                            {{GetRegimeIcon(segment.Regime.Type)}} {{segment.Regime.Type}}<br>
                            <small>RSI: {{segment.Regime.RSI:F0}} | ATR: {{segment.Regime.ATRRatio:F1}}x</small>
                        </td>
                        <td>{{segment.TradeCount}} trades<br>{{segment.WinRate:F1}}% win rate</td>
                        <td class="{{performanceClass}}">{{performanceText}} PnL</td>
                        <td><small>{{insight}}</small></td>
                    </tr>
                """);
            }

            html.AppendLine("""
                    </table>
                </div>
            """);

            return html.ToString();
        }

        private string GetRegimeInsight(MarketRegimeSegment segment)
        {
            if (segment.TradeCount == 0) return "No trades";

            if (segment.WinRate > 60 && segment.TotalPnL > 0)
                return "✅ Strong performance";
            else if (segment.WinRate > 45 && segment.TotalPnL > 0)
                return "⚠️ Moderate performance";
            else if (segment.TotalPnL < 0)
                return "❌ Consider avoiding this regime";
            else
                return "➡️ Neutral performance";
        }

        private PerformanceMetrics CalculatePerformanceMetrics(List<Trade> trades)
        {
            var metrics = new PerformanceMetrics();
            if (!trades.Any()) return metrics;

            metrics.TotalTrades = trades.Count;
            metrics.WinningTrades = trades.Count(t => t.Profit > 0);
            metrics.LosingTrades = trades.Count(t => t.Profit <= 0);
            metrics.NetProfit = trades.Sum(t => t.Profit ?? 0);
            metrics.WinRate = (decimal)metrics.WinningTrades / metrics.TotalTrades * 100;

            return metrics;
        }

        private string GetTimeSegmentsHtml(List<Trade> allTrades, List<Trade> executedTrades)
        {
            var segments = new List<string>();
            var startDate = allTrades.Min(t => t.EntryTime);
            var endDate = allTrades.Max(t => t.EntryTime);
            var totalDuration = endDate - startDate;

            // Determine appropriate segmentation based on total duration
            if (totalDuration.TotalHours <= 24)
            {
                // For intraday reports, split by 4-hour blocks
                for (int i = 0; i < 6; i++) // 6 segments = 24 hours / 4
                {
                    var segmentStart = startDate.AddHours(i * 4);
                    var segmentEnd = i == 5 ? endDate : segmentStart.AddHours(4);

                    var segmentAll = allTrades.Count(t => t.EntryTime >= segmentStart && t.EntryTime < segmentEnd);
                    var segmentExecuted = executedTrades.Count(t => t.EntryTime >= segmentStart && t.EntryTime < segmentEnd);

                    segments.Add($$"""
                        <tr>
                            <td>{{segmentStart:HH:mm}} - {{segmentEnd:HH:mm}} UTC</td>
                            <td>{{segmentExecuted}}</td>
                            <td>{{segmentAll - segmentExecuted}}</td>
                            <td>{{Math.Min(8, segmentExecuted)}}</td>
                        </tr>
                    """);
                }
            }
            else if (totalDuration.TotalDays <= 7)
            {
                // For weekly reports, split by day
                for (int i = 0; i < (int)Math.Ceiling(totalDuration.TotalDays); i++)
                {
                    var segmentStart = startDate.AddDays(i);
                    var segmentEnd = i == (int)totalDuration.TotalDays ? endDate : segmentStart.AddDays(1);

                    var segmentAll = allTrades.Count(t => t.EntryTime >= segmentStart && t.EntryTime < segmentEnd);
                    var segmentExecuted = executedTrades.Count(t => t.EntryTime >= segmentStart && t.EntryTime < segmentEnd);

                    segments.Add($$"""
                        <tr>
                            <td>{{segmentStart.ToString("ddd MMM dd", CultureInfo.InvariantCulture)}}</td>
                            <td>{{segmentExecuted}}</td>
                            <td>{{segmentAll - segmentExecuted}}</td>
                            <td>{{Math.Min(8, segmentExecuted)}}</td>
                        </tr>
                    """);
                }
            }
            else
            {
                // For longer reports, split by week
                for (int i = 0; i < (int)Math.Ceiling(totalDuration.TotalDays / 7); i++)
                {
                    var segmentStart = startDate.AddDays(i * 7);
                    var segmentEnd = i == (int)(totalDuration.TotalDays / 7) ? endDate : segmentStart.AddDays(7);

                    var segmentAll = allTrades.Count(t => t.EntryTime >= segmentStart && t.EntryTime < segmentEnd);
                    var segmentExecuted = executedTrades.Count(t => t.EntryTime >= segmentStart && t.EntryTime < segmentEnd);

                    segments.Add($$"""
                        <tr>
                            <td>Week of {{segmentStart:MMM dd}}</td>
                            <td>{{segmentExecuted}}</td>
                            <td>{{segmentAll - segmentExecuted}}</td>
                            <td>{{Math.Min(8, segmentExecuted)}}</td>
                        </tr>
                    """);
                }
            }

            return string.Join("\n", segments);
        }

        private string GetMarketSessionAnalysis(List<Trade> trades)
        {
            var sessionGroups = trades
                .GroupBy(t => GetMarketSession(t.EntryTime))
                .Select(g => new
                {
                    Session = g.Key,
                    Count = g.Count(),
                    AvgProfit = g.Average(t => t.Profit ?? 0),
                    WinRate = (decimal)g.Count(t => t.Profit > 0) / g.Count() * 100
                })
                .OrderByDescending(x => x.Count);

            return string.Join("\n", sessionGroups.Select(s => $$"""
                <tr>
                    <td>{{s.Session}}</td>
                    <td>{{s.Count}}</td>
                    <td class="{{(s.AvgProfit >= 0 ? "positive" : "negative")}}">
                        {{s.AvgProfit.ToString("F2")}}
                    </td>
                    <td>{{s.WinRate.ToString("F1")}}%</td>
                </tr>
            """));
        }

        public List<MarketRegimeSegment> CorrelateTradesWithSegments(List<MarketRegimeSegment> segments, List<Trade> trades)
        {
            foreach (var segment in segments)
            {
                var segmentTrades = trades.Where(t =>
                    t.EntryTime >= segment.StartTime &&
                    t.EntryTime < segment.EndTime).ToList();

                segment.TradeCount = segmentTrades.Count;
                segment.TotalPnL = segmentTrades.Sum(t => t.Profit ?? 0);
                segment.WinRate = segmentTrades.Count > 0 ?
                    (decimal)segmentTrades.Count(t => t.Profit > 0) / segmentTrades.Count * 100 : 0;
            }

            return segments;
        }

        private string GetMarketSession(DateTime time)
        {
            int hour = time.Hour;
            if (hour >= 0 && hour < 5) return "Late NY (00-05 UTC)";
            if (hour >= 5 && hour < 9) return "Asia (05-09 UTC)";
            if (hour >= 9 && hour < 14) return "London (09-14 UTC)";
            if (hour >= 14 && hour < 18) return "NY (14-18 UTC)";
            return "Evening (18-24 UTC)";
        }

        private string GetRegimeColorClass(MarketRegimeType regime)
        {
            return regime switch
            {
                MarketRegimeType.BullishTrend => "positive",
                MarketRegimeType.BearishTrend => "negative",
                MarketRegimeType.HighVolatility => "warning",
                _ => "" // neutral for ranging/unknown
            };
        }

        private string GetRegimeIcon(MarketRegimeType regime)
        {
            return regime switch
            {
                MarketRegimeType.BullishTrend => "▲",
                MarketRegimeType.BearishTrend => "▼",
                MarketRegimeType.HighVolatility => "⚡",
                MarketRegimeType.RangingMarket => "➡️",
                _ => "❓"
            };
        }

        private string GetVolatilityColorClass(VolatilityLevel volatility)
        {
            return volatility switch
            {
                VolatilityLevel.VeryHigh or VolatilityLevel.High => "warning",
                VolatilityLevel.Low => "negative",
                _ => "" // normal
            };
        }

        private string GetTradingTimeframeDescription(string tradingTimeframe)
        {
            var (primary, secondary) = GetTradingAlignedTimeframes(tradingTimeframe);
            return $"{primary}/{secondary}";
        }

        private string GenerateRegimeComparison(MarketRegime general, MarketRegime trading)
        {
            var sameRegime = general.Type == trading.Type;
            var generalConfidence = general.OverallConfidence;
            var tradingConfidence = trading.OverallConfidence;

            if (sameRegime)
            {
                return $$"""
                    <div class="suggestion">
                        <strong>✅ Regime Alignment:</strong> Both analyses agree on <strong>{{general.Type}}</strong> market
                        (General: {{generalConfidence}}% confidence, Trading: {{tradingConfidence}}% confidence)
                    </div>
                """;
            }
            else
            {
                return $$"""
                    <div class="warning">
                        <strong>⚠️ Regime Mismatch:</strong> 
                        General analysis: <strong>{{general.Type}}</strong> ({{generalConfidence}}% confidence) | 
                        Trading analysis: <strong>{{trading.Type}}</strong> ({{tradingConfidence}}% confidence)
                        <br><em>Different market conditions detected at different timeframes</em>
                    </div>
                """;
            }
        }

        private (string primaryTF, string secondaryTF) GetTradingAlignedTimeframes(string tradingTimeframe)
        {
            return tradingTimeframe.ToLower() switch
            {
                "1m" => ("5m", "15m"),
                "5m" => ("15m", "1h"),
                "15m" => ("1h", "4h"),
                "30m" => ("1h", "4h"),
                "1h" => ("4h", "1d"),
                "4h" => ("1d", "3d"),
                "1d" => ("3d", "1w"),
                _ => ("1h", "4h")
            };
        }

        private string GenerateRegimeInsightsHtml(List<MarketRegimeSegment> segments, List<Trade> allTrades, Dictionary<string, decimal> strategyPerformance, Dictionary<string, decimal> coinPerformance)
        {
            var insights = new List<string>();

            // NEW: Regime Performance Analysis
            var regimePerformance = segments
                .Where(s => s.TradeCount > 5)
                .Select(s => new
                {
                    Regime = s.Regime,
                    WinRate = s.WinRate,
                    AvgPnL = s.TotalPnL / Math.Max(s.TradeCount, 1),
                    Trades = s.TradeCount
                });

            // 1. Market Session Insights (ENHANCED with regime context)
            var sessionPerformance = allTrades
                .GroupBy(t => GetMarketSession(t.EntryTime))
                .Select(g => new
                {
                    Session = g.Key,
                    WinRate = (decimal)g.Count(t => t.Profit > 0) / g.Count() * 100,
                    AvgPnL = g.Average(t => t.Profit ?? 0),
                    Count = g.Count(),
                    // NEW: Add regime context
                    BestRegime = CleanRegimeName(GetBestRegimeForSession(g.ToList(), segments)),
                    WorstRegime = CleanRegimeName(GetWorstRegimeForSession(g.ToList(), segments))
                })
                .Where(x => x.Count >= 10)
                .OrderByDescending(x => x.WinRate)
                .ToList();

            if (sessionPerformance.Any())
            {
                var bestSession = sessionPerformance.First();
                var worstSession = sessionPerformance.Last();

                if (bestSession.WinRate > 50)
                {
                    var hasValidBestRegime = !string.IsNullOrEmpty(bestSession.BestRegime) && bestSession.BestRegime != "Unknown";
                    var regimeContext = hasValidBestRegime ? $" (best in {bestSession.BestRegime} regimes)" : "";
                    insights.Add($"Focus on <strong>{bestSession.Session}</strong> sessions ({bestSession.WinRate:F1}% win rate{regimeContext})");
                }

                if (worstSession.WinRate < 40 && worstSession.Count >= 15)
                {
                    var hasValidWorstRegime = !string.IsNullOrEmpty(worstSession.WorstRegime) && worstSession.WorstRegime != "Unknown";
                    var regimeContext = hasValidWorstRegime ? $" (avoids {worstSession.WorstRegime} regimes)" : "";
                    insights.Add($"Reduce trading during <strong>{worstSession.Session}</strong> sessions ({worstSession.WinRate:F1}% win rate{regimeContext})");
                }
            }

            // 2. Strategy Performance Insights (ENHANCED with regime context)
            var strategyRanking = strategyPerformance
                .Select(kvp => new
                {
                    Strategy = kvp.Key,
                    PnL = kvp.Value,
                    Trades = allTrades.Count(t => t.Signal == kvp.Key),
                    WinRate = CalculateWinRate(allTrades.Where(t => t.Signal == kvp.Key).ToList()),
                    // NEW: Regime performance
                    BestRegime = CleanRegimeName(GetBestRegimeForStrategy(kvp.Key, allTrades, segments)),
                    WorstRegime = CleanRegimeName(GetWorstRegimeForStrategy(kvp.Key, allTrades, segments))
                })
                .Where(s => s.Trades >= 10)
                .OrderByDescending(s => s.PnL)
                .ToList();

            if (strategyRanking.Any())
            {
                var topStrategy = strategyRanking.First();
                if (topStrategy.PnL > 0 && topStrategy.Trades >= 20)
                {
                    var regimeAdvice = topStrategy.BestRegime != null ?
                        $" in <strong>{topStrategy.BestRegime}</strong> markets" : "";
                    insights.Add($"Prioritize <strong>{topStrategy.Strategy}</strong> strategy{regimeAdvice} (+{topStrategy.PnL:F1} PnL)");
                }

                // NEW: Add regime-specific warnings
                var problematicStrategies = strategyRanking
                    .Where(s => s.WorstRegime != null && s.Trades >= 15)
                    .Take(2); // Limit to top 2 warnings

                foreach (var strategy in problematicStrategies)
                {
                    if (strategy.WorstRegime != "Unknown" && !string.IsNullOrEmpty(strategy.WorstRegime))
                    {
                        insights.Add($"Avoid <strong>{strategy.Strategy}</strong> in <strong>{strategy.WorstRegime}</strong> regimes ({strategy.WinRate:F1}% WR)");
                    }
                }

            }

            // 3. Duration Insights (keep as-is - already good)
            var durationGroups = allTrades
                .GroupBy(t => (int)(t.Duration / 30))
                .Select(g => new
                {
                    Range = $"{g.Key * 30}-{(g.Key + 1) * 30} mins",
                    WinRate = CalculateWinRate(g.ToList()),
                    Count = g.Count()
                })
                .Where(g => g.Count >= 5)
                .OrderByDescending(g => g.WinRate)
                .ToList();

            if (durationGroups.Any())
            {
                var bestDuration = durationGroups.First();
                var worstDuration = durationGroups.Last();

                if (bestDuration.WinRate > 55 && bestDuration.Count >= 10)
                    insights.Add($"Let winners run <strong>{bestDuration.Range}</strong> ({bestDuration.WinRate:F1}% win rate)");

                if (worstDuration.WinRate < 40 && worstDuration.Count >= 10)
                    insights.Add($"Avoid closing in <strong>{worstDuration.Range}</strong> range ({worstDuration.WinRate:F1}% win rate)");
            }

            // 4. NEW: Regime-Specific Activation Rules
            if (regimePerformance.Any())
            {
                var bestRegime = regimePerformance.OrderByDescending(r => r.AvgPnL).First();
                var worstRegime = regimePerformance.OrderBy(r => r.AvgPnL).First();

                if (bestRegime.AvgPnL > 0.5m && bestRegime.Trades >= 10)
                {
                    var regimeName = CleanRegimeName(bestRegime.Regime.Type.ToString());
                    if (regimeName != "Unknown" && !string.IsNullOrEmpty(regimeName))
                    {
                        insights.Add($"<strong>Activate</strong> during <strong>{regimeName}</strong> regimes (+{bestRegime.AvgPnL:F1} avg PnL)");
                    }
                }

                if (worstRegime.AvgPnL < -0.3m && worstRegime.Trades >= 10)
                {
                    var regimeName = CleanRegimeName(worstRegime.Regime.Type.ToString());
                    if (regimeName != "Unknown" && !string.IsNullOrEmpty(regimeName))
                    {
                        insights.Add($"<strong>Reduce exposure</strong> in <strong>{regimeName}</strong> regimes ({worstRegime.AvgPnL:F1} avg PnL)");
                    }
                }
            }

            // 5. Position Bias Insight (keep as-is)
            var longTrades = allTrades.Where(t => t.IsLong).ToList();
            var shortTrades = allTrades.Where(t => !t.IsLong).ToList();

            if (longTrades.Count >= 20 && shortTrades.Count >= 20)
            {
                var longWinRate = CalculateWinRate(longTrades);
                var shortWinRate = CalculateWinRate(shortTrades);

                if (longWinRate - shortWinRate > 10)
                    insights.Add($"Consider <strong>long bias</strong> ({longWinRate:F1}% vs {shortWinRate:F1}% short win rate)");
            }

            // 6. Coin Performance Insight (keep as-is)
            var topCoins = coinPerformance
                .Where(kvp => allTrades.Count(t => t.Symbol == kvp.Key) >= 5)
                .OrderByDescending(kvp => kvp.Value)
                .Take(3)
                .ToList();

            if (topCoins.Any(c => c.Value > 10))
            {
                var bestCoin = topCoins.First();
                insights.Add($"<strong>{bestCoin.Key}</strong> performing well (+{bestCoin.Value:F1} PnL)");
            }

            // 7. Weekend/Off-Hours Insights
            var weekendTrades = allTrades.Where(t =>
                t.EntryTime.DayOfWeek == DayOfWeek.Saturday ||
                t.EntryTime.DayOfWeek == DayOfWeek.Sunday).ToList();

            if (weekendTrades.Any())
            {
                var weekendWinRate = CalculateWinRate(weekendTrades);
                var weekdayWinRate = CalculateWinRate(allTrades.Except(weekendTrades).ToList());

                if (weekendWinRate - weekdayWinRate > 15)
                    insights.Add($"<strong>Weekend opportunities</strong> detected ({weekendWinRate:F1}% vs {weekdayWinRate:F1}% weekday)");
                else if (weekdayWinRate - weekendWinRate > 15)
                    insights.Add($"<strong>Focus on weekdays</strong> ({weekdayWinRate:F1}% vs {weekendWinRate:F1}% weekend)");
            }

            // 8. Extended Hours Insight
            var extendedHours = allTrades.Where(t => t.EntryTime.Hour >= 22 || t.EntryTime.Hour < 6).ToList();
            if (extendedHours.Any() && extendedHours.Count >= 10)
            {
                var extendedWinRate = CalculateWinRate(extendedHours);
                if (extendedWinRate < 40)
                    insights.Add($"<strong>Reduce overnight trading</strong> ({extendedWinRate:F1}% win rate in extended hours)");
            }

            // Generate the HTML
            if (!insights.Any())
                return "<div class='suggestion'><h3>🎯 Actionable Insights</h3><p>Collect more trade data for personalized insights</p></div>";

            return $"""
                <div class="suggestion">
                    <h3>🎯 This Trade Session's Focus</h3>
                    <ul>
                        {string.Join("", insights.Take(5).Select(i => $"<li>{i}</li>"))}
                    </ul>
                </div>
            """;
        }

        private string CleanRegimeName(string regimeName)
        {
            if (string.IsNullOrEmpty(regimeName) || regimeName == "Unknown")
                return "Unknown";

            // Clean up the enum name
            return regimeName switch
            {
                "BullishTrend" => "Bullish",
                "BearishTrend" => "Bearish",
                "HighVolatility" => "High Volatility",
                "RangingMarket" => "Ranging",
                "Unknown" => "Unknown",
                "MarketRegime" => "Unknown", // Handle the generic case
                _ => regimeName.Replace("Market", "").Replace("Trend", "").Trim()
            };
        }

        private string GetDayOfWeekAnalysis(List<Trade> trades)
        {
            var dayGroups = trades
                .GroupBy(t => t.EntryTime.DayOfWeek)
                .Select(g => new
                {
                    Day = g.Key.ToString(),
                    Count = g.Count(),
                    WinRate = CalculateWinRate(g.ToList()),
                    AvgPnL = g.Average(t => t.Profit ?? 0),
                    TotalPnL = g.Sum(t => t.Profit ?? 0)
                })
                .OrderBy(x => x.Day == "Sunday" ? 7 : (int)Enum.Parse(typeof(DayOfWeek), x.Day))
                .ToList();

            return $$"""
                <div class="section">
                    <h2>📆 Day-of-Week Performance</h2>
                    <table>
                        <tr>
                            <th>Day</th>
                            <th>Trades</th>
                            <th>Win Rate</th>
                            <th>Avg PnL</th>
                            <th>Total PnL</th>
                        </tr>
                        {{string.Join("\n", dayGroups.Select(d => $$"""
                        <tr>
                            <td>{{d.Day}}</td>
                            <td>{{d.Count}}</td>
                            <td>{{d.WinRate.ToString("F1")}}%</td>
                            <td class="{{(d.AvgPnL >= 0 ? "positive" : "negative")}}">
                                {{d.AvgPnL.ToString("F2")}}
                            </td>
                            <td class="{{(d.TotalPnL >= 0 ? "positive" : "negative")}}">
                                {{d.TotalPnL.ToString("F2")}}
                            </td>
                        </tr>
                        """))}}
                    </table>
                </div>
            """;
        }

        private string GetExtendedHoursAnalysis(List<Trade> trades)
        {
            // Define low-liquidity hours (varies by market)
            var extendedHours = trades.Where(t =>
                (t.EntryTime.Hour >= 22 || t.EntryTime.Hour < 6) // Late US to Early Asia
            ).ToList();

            var regularHours = trades.Where(t =>
                t.EntryTime.Hour >= 6 && t.EntryTime.Hour < 22
            ).ToList();

            return $$"""
                <div class="section">
                    <h2>🌙 Extended Hours Performance</h2>
                    <div class="warning">
                        <strong>Note:</strong> Extended hours = 22:00-06:00 UTC (Lower liquidity periods)
                    </div>
                    <table>
                        <tr>
                            <th>Trading Hours</th>
                            <th>Trades</th>
                            <th>Win Rate</th>
                            <th>Avg PnL</th>
                            <th>Volatility Impact</th>
                        </tr>
                        <tr>
                            <td>Regular Hours (06:00-22:00 UTC)</td>
                            <td>{{regularHours.Count}}</td>
                            <td>{{CalculateWinRate(regularHours).ToString("F1")}}%</td>
                            <td class="{{(regularHours.Average(t => t.Profit ?? 0) >= 0 ? "positive" : "negative")}}">
                                {{regularHours.Average(t => t.Profit ?? 0).ToString("F2")}}
                            </td>
                            <td>Standard</td>
                        </tr>
                        <tr>
                            <td>Extended Hours (22:00-06:00 UTC)</td>
                            <td>{{extendedHours.Count}}</td>
                            <td>{{CalculateWinRate(extendedHours).ToString("F1")}}%</td>
                            <td class="{{(extendedHours.Average(t => t.Profit ?? 0) >= 0 ? "positive" : "negative")}}">
                                {{extendedHours.Average(t => t.Profit ?? 0).ToString("F2")}}
                            </td>
                            <td>Higher spreads</td>
                        </tr>
                    </table>
                </div>
            """;
        }
        
        private string GetWeekendWeekdayAnalysis(List<Trade> trades)
        {
            var weekdayTrades = trades.Where(t => t.EntryTime.DayOfWeek != DayOfWeek.Saturday &&
                                                t.EntryTime.DayOfWeek != DayOfWeek.Sunday).ToList();
            var weekendTrades = trades.Where(t => t.EntryTime.DayOfWeek == DayOfWeek.Saturday ||
                                                t.EntryTime.DayOfWeek == DayOfWeek.Sunday).ToList();

            return $$"""
                <div class="section">
                    <h2>📅 Weekend vs Weekday Performance</h2>
                    <table>
                        <tr>
                            <th>Period</th>
                            <th>Trades</th>
                            <th>Win Rate</th>
                            <th>Avg PnL</th>
                            <th>Avg Duration</th>
                        </tr>
                        <tr>
                            <td>Weekdays (Mon-Fri)</td>
                            <td>{{weekdayTrades.Count}}</td>
                            <td>{{CalculateWinRate(weekdayTrades).ToString("F1")}}%</td>
                            <td class="{{(weekdayTrades.Average(t => t.Profit ?? 0) >= 0 ? "positive" : "negative")}}">
                                {{weekdayTrades.Average(t => t.Profit ?? 0).ToString("F2")}}
                            </td>
                            <td>{{weekdayTrades.Average(t => t.Duration).ToString("F0")}} mins</td>
                        </tr>
                        <tr>
                            <td>Weekend (Sat-Sun)</td>
                            <td>{{weekendTrades.Count}}</td>
                            <td>{{CalculateWinRate(weekendTrades).ToString("F1")}}%</td>
                            <td class="{{(weekendTrades.Average(t => t.Profit ?? 0) >= 0 ? "positive" : "negative")}}">
                                {{weekendTrades.Average(t => t.Profit ?? 0).ToString("F2")}}
                            </td>
                            <td>{{weekendTrades.Average(t => t.Duration).ToString("F0")}} mins</td>
                        </tr>
                    </table>
                </div>
            """;
        }

        // NEW: Helper methods for regime analysis
        private string GetBestRegimeForSession(List<Trade> sessionTrades, List<MarketRegimeSegment> segments)
        {
            // Cross-reference session trades with regime performance
            var regimePerformance = segments
                .Where(s => sessionTrades.Any(t => IsTimeInSegment(t.EntryTime, s)))
                .OrderByDescending(s => s.TotalPnL / Math.Max(s.TradeCount, 1))
                .FirstOrDefault();

            return regimePerformance?.Regime?.Type.ToString() ?? "Unknown";
        }

        private string GetWorstRegimeForSession(List<Trade> sessionTrades, List<MarketRegimeSegment> segments)
        {
            var regimePerformance = segments
                .Where(s => sessionTrades.Any(t => IsTimeInSegment(t.EntryTime, s)))
                .OrderBy(s => s.TotalPnL / Math.Max(s.TradeCount, 1))
                .FirstOrDefault();

            return regimePerformance?.Regime?.Type.ToString() ?? "Unknown";
        }

        private string GetBestRegimeForStrategy(string strategy, List<Trade> allTrades, List<MarketRegimeSegment> segments)
        {
            var strategyTrades = allTrades.Where(t => t.Signal == strategy).ToList();
            var regimePerformance = segments
                .Where(s => strategyTrades.Any(t => IsTimeInSegment(t.EntryTime, s)))
                .OrderByDescending(s => s.TotalPnL / Math.Max(s.TradeCount, 1))
                .FirstOrDefault();

            return regimePerformance?.Regime?.Type.ToString() ?? "Unknown";
        }

        private string GetWorstRegimeForStrategy(string strategy, List<Trade> allTrades, List<MarketRegimeSegment> segments)
        {
            var strategyTrades = allTrades.Where(t => t.Signal == strategy).ToList();
            var regimePerformance = segments
                .Where(s => strategyTrades.Any(t => IsTimeInSegment(t.EntryTime, s)))
                .OrderBy(s => s.TotalPnL / Math.Max(s.TradeCount, 1))
                .FirstOrDefault();

            return regimePerformance?.Regime?.Type.ToString() ?? "Unknown";
        }

        private bool IsTimeInSegment(DateTime time, MarketRegimeSegment segment)
        {
            return time >= segment.StartTime && time <= segment.EndTime;
        }

        // Add this method to generate the executive summary
        private string GenerateExecutiveSummary(PerformanceMetrics metrics, List<Trade> allTrades,
            Dictionary<string, decimal> strategyPerformance, List<MarketRegimeSegment> regimeSegments,
            decimal limitedNetProfit, int limitedTrades)
        {
            var summary = new StringBuilder();

            summary.AppendLine("""
                <div class="section" style="background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); color: white;">
                    <h2 style="color: white; border-bottom: 2px solid rgba(255,255,255,0.3);">🚀 Executive Summary</h2>
                    <div class="metric-grid" style="grid-template-columns: repeat(2, 1fr);">
                """);

            // Key Performance Indicators
            var kpis = new[]
            {
                new {
                    Title = "Overall Performance",
                    Value = metrics.NetProfit >= 0 ? "PROFITABLE" : "UNPROFITABLE",
                    Class = metrics.NetProfit >= 0 ? "positive" : "negative",
                    Icon = metrics.NetProfit >= 0 ? "📈" : "📉"
                },
                new {
                    Title = "Risk-Adjusted Score",
                    Value = GetRiskAdjustedScore(metrics),
                    Class = GetRiskScoreClass(metrics),
                    Icon = "🎯"
                },
                new {
                    Title = "8-Trade Limit Impact",
                    Value = limitedNetProfit > metrics.NetProfit ? "POSITIVE" : "NEGATIVE",
                    Class = limitedNetProfit > metrics.NetProfit ? "positive" : "negative",
                    Icon = limitedNetProfit > metrics.NetProfit ? "✅" : "⚠️"
                },
                new {
                    Title = "Market Alignment",
                    Value = GetMarketAlignmentScore(regimeSegments),
                    Class = GetAlignmentClass(regimeSegments),
                    Icon = "🎪"
                }
            };

            foreach (var kpi in kpis)
            {
                summary.AppendLine($$"""
                    <div class="metric-card" style="background: rgba(255,255,255,0.1); border: 1px solid rgba(255,255,255,0.2);">
                        <h3 style="color: white; font-size: 0.9em;">{{kpi.Icon}} {{kpi.Title}}</h3>
                        <p class="{{kpi.Class}}" style="font-size: 1.2em; font-weight: bold; margin: 5px 0;">{{kpi.Value}}</p>
                    </div>
                """);
            }

            summary.AppendLine("</div>");

            // Top 3 Actionable Insights
            var topInsights = GenerateTopInsights(metrics, allTrades, strategyPerformance, regimeSegments);

            summary.AppendLine($$"""
                <div style="margin-top: 20px; background: rgba(255,255,255,0.1); padding: 15px; border-radius: 5px;">
                    <h3 style="color: white; margin-top: 0;">🎯 Top 3 Actionable Insights</h3>
                    <ol style="color: white; padding-left: 20px;">
                        {{string.Join("", topInsights.Select((insight, index) => $"<li style=\"margin-bottom: 8px;\">{insight}</li>"))}}
                    </ol>
                </div>
            """);

            // Quick Stats
            summary.AppendLine($$"""
                <div style="display: grid; grid-template-columns: repeat(3, 1fr); gap: 10px; margin-top: 15px;">
                    <div style="text-align: center;">
                        <div style="font-size: 1.8em; font-weight: bold;">{{metrics.WinRate:F1}}%</div>
                        <div style="font-size: 0.8em;">Win Rate</div>
                    </div>
                    <div style="text-align: center;">
                        <div style="font-size: 1.8em; font-weight: bold;" class="{{(metrics.NetProfit >= 0 ? "positive" : "negative")}}">{{metrics.NetProfit:F0}}</div>
                        <div style="font-size: 0.8em;">Net PnL</div>
                    </div>
                    <div style="text-align: center;">
                        <div style="font-size: 1.8em; font-weight: bold;">{{allTrades.Count}}</div>
                        <div style="font-size: 0.8em;">Total Trades</div>
                    </div>
                </div>
            """);

            summary.AppendLine("</div>");
            return summary.ToString();
        }

        // Helper methods for the executive summary
        private string GetRiskAdjustedScore(PerformanceMetrics metrics)
        {
            if (metrics.SharpeRatio > 1) return "EXCELLENT";
            if (metrics.SharpeRatio > 0) return "GOOD";
            if ((double)metrics.SharpeRatio > -0.5) return "MODERATE";
            return "POOR";
        }

        private string GetRiskScoreClass(PerformanceMetrics metrics)
        {
            if (metrics.SharpeRatio > 1) return "positive";
            if (metrics.SharpeRatio > 0) return "positive";
            if ((double)metrics.SharpeRatio > -0.5) return "warning";
            return "negative";
        }

        private string GetMarketAlignmentScore(List<MarketRegimeSegment> segments)
        {
            if (!segments.Any()) return "UNKNOWN";

            var profitableSegments = segments.Count(s => s.TotalPnL > 0);
            var totalSegments = segments.Count(s => s.TradeCount > 0);

            var alignment = (decimal)profitableSegments / totalSegments * 100;

            if (alignment > 70) return "STRONG";
            if (alignment > 50) return "MODERATE";
            return "WEAK";
        }

        private string GetAlignmentClass(List<MarketRegimeSegment> segments)
        {
            if (!segments.Any()) return "warning";

            var profitableSegments = segments.Count(s => s.TotalPnL > 0);
            var totalSegments = segments.Count(s => s.TradeCount > 0);

            var alignment = (decimal)profitableSegments / totalSegments * 100;

            if (alignment > 70) return "positive";
            if (alignment > 50) return "warning";
            return "negative";
        }

        private List<string> GenerateTopInsights(PerformanceMetrics metrics, List<Trade> allTrades,
            Dictionary<string, decimal> strategyPerformance, List<MarketRegimeSegment> regimeSegments)
        {
            var insights = new List<string>();

            // 1. Best performing strategy insight
            var bestStrategy = strategyPerformance
                .Where(kvp => allTrades.Count(t => t.Signal == kvp.Key) >= 10)
                .OrderByDescending(kvp => kvp.Value)
                .FirstOrDefault();

            if (!bestStrategy.Equals(default(KeyValuePair<string, decimal>)) && bestStrategy.Value > 0)
            {
                insights.Add($"<strong>{bestStrategy.Key}</strong> was your best strategy (+{bestStrategy.Value:F1} PnL)");
            }

            // 2. Market timing insight
            var sessionPerformance = allTrades
                .GroupBy(t => GetMarketSession(t.EntryTime))
                .Select(g => new
                {
                    Session = g.Key,
                    WinRate = (decimal)g.Count(t => t.Profit > 0) / g.Count() * 100
                })
                .OrderByDescending(x => x.WinRate)
                .FirstOrDefault();

            if (sessionPerformance != null && sessionPerformance.WinRate > 55)
            {
                insights.Add($"Focus on <strong>{sessionPerformance.Session}</strong> sessions ({sessionPerformance.WinRate:F1}% win rate)");
            }

            // 3. Risk management insight
            var maxLoss = allTrades.Min(t => t.Profit ?? 0);
            if (maxLoss < -5)
            {
                insights.Add($"Review risk management - largest loss was {maxLoss:F1} (consider tighter stops)");
            }
            else if (metrics.WinRate < 45)
            {
                insights.Add("Win rate below 45% - consider strategy adjustments or position sizing changes");
            }
            else
            {
                var bestDuration = allTrades
                    .GroupBy(t => (int)(t.Duration / 60))
                    .Select(g => new
                    {
                        Hours = g.Key,
                        WinRate = (decimal)g.Count(t => t.Profit > 0) / g.Count() * 100
                    })
                    .OrderByDescending(g => g.WinRate)
                    .FirstOrDefault();

                if (bestDuration != null && bestDuration.WinRate > 60)
                {
                    insights.Add($"Best performance in <strong>{bestDuration.Hours}-{bestDuration.Hours + 1} hour</strong> trades ({bestDuration.WinRate:F1}% win rate)");
                }
            }

            // Ensure we have exactly 3 insights
            while (insights.Count < 3)
            {
                insights.Add("Collect more trade data for personalized insights");
            }

            return insights.Take(3).ToList();
        }
        
        private string GenerateStrategyHeatmapHtml(List<StrategyRegimePerformance> performances)
        {
            if (!performances.Any() || performances.All(p =>
                p.BullishWinRate == 0 && p.BearishWinRate == 0 &&
                p.RangingWinRate == 0 && p.HighVolWinRate == 0))
            {
                return """
                    <div class="section">
                        <h2>🎯 Strategy vs Market Regime Heatmap</h2>
                        <div class="warning">
                            Not enough regime data available for heatmap analysis. 
                            Need more trades across different market conditions.
                        </div>
                    </div>
                    """;
            }

            var html = new StringBuilder();
            html.AppendLine("""
                <div class="section">
                    <h2>🎯 Strategy vs Market Regime Heatmap</h2>
                    <div class="suggestion">
                        <strong>Color Guide:</strong> 
                        <span style="color: #27ae60;">🟢 Use (>55% WR)</span> | 
                        <span style="color: #ffc107;">🟡 Moderate (40-55% WR)</span> | 
                        <span style="color: #e74c3c;">🔴 Avoid (<40% WR)</span> |
                        <span>─ (No data)</span>
                    </div>
                    <table>
                        <tr>
                            <th>Strategy</th>
                            <th>Bullish<br><small>▲ Trend</small></th>
                            <th>Bearish<br><small>▼ Trend</small></th>
                            <th>Ranging<br><small>➡️ Sideways</small></th>
                            <th>High Vol<br><small>⚡ Spike</small></th>
                            <th>Overall PnL</th>
                        </tr>
                """);

            foreach (var perf in performances.OrderByDescending(p => p.TotalPnL))
            {
                html.AppendLine($$"""
                    <tr>
                        <td><strong>{{perf.Strategy}}</strong></td>
                        <td style="text-align: center; font-size: 1.2em;">{{GetHeatmapCell(perf.BullishWinRate)}}</td>
                        <td style="text-align: center; font-size: 1.2em;">{{GetHeatmapCell(perf.BearishWinRate)}}</td>
                        <td style="text-align: center; font-size: 1.2em;">{{GetHeatmapCell(perf.RangingWinRate)}}</td>
                        <td style="text-align: center; font-size: 1.2em;">{{GetHeatmapCell(perf.HighVolWinRate)}}</td>
                        <td class="{{(perf.TotalPnL >= 0 ? "positive" : "negative")}}" style="text-align: right; font-weight: bold;">
                            {{perf.TotalPnL.ToString("+0.0;-0.0")}}
                        </td>
                    </tr>
                """);
            }

            html.AppendLine("</table></div>");
            return html.ToString();
        }

        private string GetHeatmapCell(decimal winRate)
        {
            if (winRate == 0) return "─";
            if (winRate > 55) return "<span style=\"color: #27ae60;\">🟢</span>";
            if (winRate > 40) return "<span style=\"color: #ffc107;\">🟡</span>";
            return "<span style=\"color: #e74c3c;\">🔴</span>";
        }

        private string GetRegimeTimeframe(string tradingTimeframe)
        {
            return tradingTimeframe.ToLower() switch
            {
                "1m" => "5m",
                "5m" => "15m",
                "15m" => "1h",
                "30m" => "1h",
                "1h" => "4h",
                "4h" => "1d",
                _ => "1h"
            };
        }

        private ReportSectionResult GenerateExecutiveSummarySafe(string sessionId, ReportSettings settings, List<Trade> allTrades)
        {
            try
            {
                var metrics = _tradeLogger.CalculatePerformanceMetrics(sessionId);
                var strategyPerformance = _tradeLogger.GetStrategyPerformance(sessionId);
                
                // Run the simulation with 8-trade limit
                var simulator = new StrictConcurrentTradeSimulator();
                var simulatedTrades = simulator.Simulate(allTrades, 8);
                var executedTrades = simulatedTrades.Where(t => t.WasExecuted).Select(t => t.OriginalTrade).ToList();
                var limitedMetrics = CalculatePerformanceMetrics(executedTrades);

                var html = GenerateExecutiveSummary(metrics, allTrades, strategyPerformance, savedRegimeSegments, limitedMetrics.NetProfit, executedTrades.Count);
                
                return ReportSectionResult.CreateSuccess("Executive Summary", html);
            }
            catch (Exception ex)
            {
                return ReportSectionResult.CreateError("Executive Summary", ex.Message);
            }
        }

        private ReportSectionResult GenerateStrategyParametersSafe(string sessionId, ReportSettings settings)
        {
            try
            {
                var strategyPerformance = _tradeLogger.GetStrategyPerformance(sessionId);

                var html = $$"""
                    <div class="section">
                        <h2>⚙️ Strategy Parameters</h2>
                        <table>
                            <tr><td>Strategies</td><td>{{string.Join(", ", strategyPerformance.Keys)}}</td></tr>
                            <tr><td>Leverage</td><td>{{settings.Leverage}}x</td></tr>
                            <tr><td>Take Profit</td><td>{{settings.TakeProfitMultiplier.ToString(settings.NumberFormat)}}x ATR(14)</td></tr>
                            <tr><td>Risk Ratio 1:X</td><td>{{settings.StopLossRatio}}</td></tr>
                            <tr><td>Margin/Trade</td><td>${{settings.MarginPerTrade.ToString(settings.NumberFormat)}}</td></tr>
                            <tr><td>Interval</td><td>{{settings.Interval}}</td></tr>
                        </table>
                    </div>
                """;

                return ReportSectionResult.CreateSuccess("Strategy Parameters", html);
            }
            catch (Exception ex)
            {
                return ReportSectionResult.CreateError("Strategy Parameters", ex.Message);
            }
        }

        private ReportSectionResult GeneratePerformanceSnapshotSafe(string sessionId, ReportSettings settings, List<Trade> allTrades)
        {
            try
            {
                var metrics = _tradeLogger.CalculatePerformanceMetrics(sessionId);
                var tradeDistribution = _tradeLogger.GetTradeDistribution(sessionId);

                // Fee calculation
                var feeCalculator = new FeeCalculatorService();
                var (totalFees, netPnLAfterFees) = feeCalculator.CalculateFeeImpact(allTrades, settings.MarginPerTrade);

                var html = $$"""
                    <div class="section">
                        <h2>📈 Performance Snapshot</h2>
                        <div class="metric-grid">
                            <div class="metric-card">
                                <h3>Win Rate</h3>
                                <p class="{{(metrics.WinRate >= 50 ? "positive" : "negative")}}">{{metrics.WinRate.ToString("F2")}}%</p>
                            </div>
                            <div class="metric-card">
                                <h3>Net PnL</h3>
                                <p class="{{(metrics.NetProfit >= 0 ? "positive" : "negative")}}">{{metrics.NetProfit.ToString("F2")}}</p>
                            </div>
                            <div class="metric-card">
                                <h3>Net PnL (After Fees)</h3>
                                <p class="{{(netPnLAfterFees >= 0 ? "positive" : "negative")}}">{{netPnLAfterFees.ToString("F2")}}</p>
                                <small>Fees: {{totalFees.ToString("F2")}}</small>
                            </div>                            
                            <div class="metric-card">
                                <h3>ROI</h3>
                                <p>{{(metrics.NetProfit / (settings.MarginPerTrade * metrics.TotalTrades)).ToString("F2")}}</p>
                            </div>
                            <div class="metric-card">
                                <h3>Max Drawdown</h3>
                                <p class="negative">{{metrics.MaximumDrawdown.ToString("F2")}}</p>
                            </div>
                            <div class="metric-card">
                                <h3>Sharpe Ratio</h3>
                                <p class="{{(metrics.SharpeRatio >= 0 ? "positive" : "negative")}}">{{metrics.SharpeRatio.ToString("F2")}}</p>
                            </div>
                            <div class="metric-card">
                                <h3>Avg Duration</h3>
                                <p>{{(metrics.TotalTrades > 0 ? allTrades.Average(t => t.Duration) : 0).ToString("F1")}} mins</p>
                            </div>
                        </div>
                        <table>
                            <tr><th>Position</th><th>Trades</th><th>Win Rate</th><th>Avg PnL</th></tr>
                            <tr>
                                <td>Long</td>
                                <td>{{tradeDistribution.LongTrades}}</td>
                                <td>{{CalculateWinRate(allTrades.Where(t => t.IsLong).ToList()).ToString("F1")}}%</td>
                                <td class="{{(allTrades.Where(t => t.IsLong).Average(t => t.Profit ?? 0) >= 0 ? "positive" : "negative")}}">
                                    {{allTrades.Where(t => t.IsLong).Average(t => t.Profit ?? 0).ToString("F2")}}
                                </td>
                            </tr>
                            <tr>
                                <td>Short</td>
                                <td>{{tradeDistribution.ShortTrades}}</td>
                                <td>{{CalculateWinRate(allTrades.Where(t => !t.IsLong).ToList()).ToString("F1")}}%</td>
                                <td class="{{(allTrades.Where(t => !t.IsLong).Average(t => t.Profit ?? 0) >= 0 ? "positive" : "negative")}}">
                                    {{allTrades.Where(t => !t.IsLong).Average(t => t.Profit ?? 0).ToString("F2")}}
                                </td>
                            </tr>
                        </table>
                    </div>
                """;

                return ReportSectionResult.CreateSuccess("Performance Snapshot", html);
            }
            catch (Exception ex)
            {
                return ReportSectionResult.CreateError("Performance Snapshot", ex.Message);
            }
        }

        private ReportSectionResult GenerateStrategyComparisonSafe(string sessionId, ReportSettings settings, List<Trade> allTrades)
        {
            try
            {
                var strategyPerformance = _tradeLogger.GetStrategyPerformance(sessionId);

                var html = new StringBuilder();
                html.AppendLine("""
                    <div class="section strategy-comparison">
                        <h2>🔄 Strategy Comparison</h2>
                        <table>
                            <tr>
                                <th>Strategy</th>
                                <th>PnL</th>
                                <th>Win Rate</th>
                                <th>Trades</th>
                                <th>Long/Short</th>
                            </tr>
                    """);

                foreach (var strategy in strategyPerformance)
                {
                    var stratTrades = allTrades.Where(t => t.Signal == strategy.Key).ToList();
                    var winRate = CalculateWinRate(stratTrades);

                    html.AppendLine($$"""
                        <tr>
                            <td>{{strategy.Key}}</td>
                            <td class="{{(strategy.Value >= 0 ? "positive" : "negative")}}">{{strategy.Value.ToString("F1")}}</td>
                            <td>{{winRate.ToString("F1")}}%</td>
                            <td>{{stratTrades.Count}}</td>
                            <td>{{stratTrades.Count(t => t.IsLong)}}/{{stratTrades.Count(t => !t.IsLong)}}</td>
                        </tr>
                    """);
                }

                html.AppendLine("""
                        </table>
                    </div>
                """);

                return ReportSectionResult.CreateSuccess("Strategy Comparison", html.ToString());
            }
            catch (Exception ex)
            {
                return ReportSectionResult.CreateError("Strategy Comparison", ex.Message);
            }
        }

        private ReportSectionResult GenerateTradeSimulationSafe(string sessionId, ReportSettings settings, List<Trade> allTrades)
        {
            try
            {
                var metrics = _tradeLogger.CalculatePerformanceMetrics(sessionId);

                // Run the simulation with 8-trade limit
                var simulator = new StrictConcurrentTradeSimulator();
                var simulatedTrades = simulator.Simulate(allTrades, 8);
                var executedTrades = simulatedTrades.Where(t => t.WasExecuted).Select(t => t.OriginalTrade).ToList();
                var skippedTrades = simulatedTrades.Where(t => !t.WasExecuted).ToList();
                var limitedMetrics = CalculatePerformanceMetrics(executedTrades);

                var html = $$"""
                    <div class="section">
                        <h2>🔀 Realistic 8-Trade Limit Simulation</h2>
                        <table>
                            <tr>
                                <th>Metric</th>
                                <th>All Trades</th>
                                <th>8-Trade Limit</th>
                                <th>Difference</th>
                            </tr>
                            <tr>
                                <td>Total Trades</td>
                                <td>{{allTrades.Count}}</td>
                                <td>{{executedTrades.Count}}</td>
                                <td>{{allTrades.Count - executedTrades.Count}} fewer</td>
                            </tr>
                            <tr>
                                <td>Win Rate</td>
                                <td>{{metrics.WinRate.ToString("F1")}}%</td>
                                <td>{{limitedMetrics.WinRate.ToString("F1")}}%</td>
                                <td class="{{(limitedMetrics.WinRate >= metrics.WinRate ? "positive" : "negative")}}">
                                    {{(limitedMetrics.WinRate - metrics.WinRate).ToString("F1")}}%
                                </td>
                            </tr>
                            <tr>
                                <td>Net PnL</td>
                                <td class="{{(metrics.NetProfit >= 0 ? "positive" : "negative")}}">
                                    {{metrics.NetProfit.ToString("F2")}}
                                </td>
                                <td class="{{(limitedMetrics.NetProfit >= 0 ? "positive" : "negative")}}">
                                    {{limitedMetrics.NetProfit.ToString("F2")}}
                                </td>
                                <td class="{{(limitedMetrics.NetProfit >= metrics.NetProfit ? "positive" : "negative")}}">
                                    {{(limitedMetrics.NetProfit - metrics.NetProfit).ToString("F2")}}
                                </td>
                            </tr>
                        </table>
                        
                        <h3>Execution Details</h3>
                        <table>
                            <tr>
                                <th>Period</th>
                                <th>Trades Executed</th>
                                <th>Trades Skipped</th>
                                <th>Max Concurrent</th>
                            </tr>
                            {{GetTimeSegmentsHtml(allTrades, executedTrades)}}
                        </table>
                    </div>
                """;

                return ReportSectionResult.CreateSuccess("8-Trade Simulation", html);
            }
            catch (Exception ex)
            {
                return ReportSectionResult.CreateError("8-Trade Simulation", ex.Message);
            }
        }

        private ReportSectionResult GenerateTradeDurationSafe(string sessionId, ReportSettings settings, List<Trade> allTrades)
        {
            try
            {
                var html = new StringBuilder();
                html.AppendLine("""
                    <div class="section">
                        <h2>⏳ Trade Duration Distribution</h2>
                        <table>
                            <tr>
                                <th>Duration Range</th>
                                <th>Trades</th>
                                <th>Avg PnL</th>
                                <th>Win Rate</th>
                            </tr>
                    """);

                var durationGroups = allTrades
                    .GroupBy(t => (int)(t.Duration / 30))
                    .Where(g => g.Count() >= 5)
                    .OrderBy(g => g.Key)
                    .Select(g => new
                    {
                        Range = $"{g.Key * 30}-{(g.Key + 1) * 30} mins",
                        Count = g.Count(),
                        AvgProfit = g.Average(t => t.Profit ?? 0),
                        WinRate = CalculateWinRate(g.ToList())
                    })
                    .ToList();

                if (!durationGroups.Any())
                {
                    html.AppendLine("""
                        <tr>
                            <td colspan="4" class="warning">
                                <strong>Note:</strong> Insufficient trades for duration analysis (need at least 5 trades per duration range)
                            </td>
                        </tr>
                    """);
                }

                foreach (var group in durationGroups)
                {
                    html.AppendLine($$"""
                        <tr>
                            <td>{{group.Range}}</td>
                            <td>{{group.Count}}</td>
                            <td class="{{(group.AvgProfit >= 0 ? "positive" : "negative")}}">
                                {{group.AvgProfit.ToString("F2", CultureInfo.InvariantCulture)}}
                            </td>
                            <td>{{group.WinRate.ToString("F1", CultureInfo.InvariantCulture)}}%</td>
                        </tr>
                    """);
                }

                var shownTrades = durationGroups.Sum(g => g.Count);
                var filteredOut = allTrades.Count - shownTrades;
                if (filteredOut > 0)
                {
                    html.AppendLine($$"""
                        <tr>
                            <td colspan="4" class="warning">
                                <strong>Note:</strong> Showing {{shownTrades}} trades ({{filteredOut}} trades in sparse duration ranges not shown)
                            </td>
                        </tr>
                    """);
                }

                html.AppendLine("""
                        </table>
                    </div>
                """);
                
                return ReportSectionResult.CreateSuccess("Trade Duration Analysis", html.ToString());
            }
            catch (Exception ex)
            {
                return ReportSectionResult.CreateError("Trade Duration Analysis", ex.Message);
            }
        }

        private ReportSectionResult GeneratePerformanceOverTimeSafe(string sessionId, ReportSettings settings, List<Trade> allTrades)
        {
            try
            {
                // Calculate time segments
                var minDate = allTrades.Min(t => t.EntryTime);
                var maxDate = allTrades.Max(t => t.EntryTime);

                var firstHalfTrades = allTrades;
                var secondHalfTrades = new List<Trade>();
                var midpoint = minDate;

                if (allTrades.Count >= 2)
                {
                    midpoint = minDate.AddDays((maxDate - minDate).TotalDays / 2);
                    firstHalfTrades = allTrades.Where(t => t.EntryTime < midpoint).ToList();
                    secondHalfTrades = allTrades.Where(t => t.EntryTime >= midpoint).ToList();
                }

                // Pre-calculate metrics
                var firstHalfWinRate = CalculateWinRate(firstHalfTrades);
                var secondHalfWinRate = CalculateWinRate(secondHalfTrades);
                var firstHalfPnL = firstHalfTrades.Sum(t => t.Profit ?? 0);
                var secondHalfPnL = secondHalfTrades.Sum(t => t.Profit ?? 0);
                var firstHalfCount = firstHalfTrades.Count;
                var secondHalfCount = secondHalfTrades.Count;

                var html = $$"""
                    <div class="section">
                        <h2>⏳ Performance Over Time</h2>
                        <div class="suggestion">
                            <strong>Note:</strong> Periods divided by session midpoint at {{midpoint.ToString("MMM dd, yyyy HH:mm", CultureInfo.InvariantCulture)}} UTC
                            ({{firstHalfCount}} vs {{secondHalfCount}} trades)
                        </div>
                        <table>
                            <tr>
                                <th>Period</th>
                                <th>Win Rate</th>
                                <th>Avg PnL</th>
                                <th>Trades</th>
                            </tr>
                            <tr>
                                <td>First Half<br>({{minDate:MMM dd HH:mm}} to {{midpoint:MMM dd HH:mm}})</td>
                                <td>{{firstHalfWinRate.ToString("F1")}}%</td>
                                <td class="{{(firstHalfPnL >= 0 ? "positive" : "negative")}}">
                                    {{firstHalfPnL.ToString("F1")}}
                                </td>
                                <td>{{firstHalfCount}}</td>
                            </tr>
                            <tr>
                                <td>Second Half<br>({{midpoint:MMM dd HH:mm}} to {{maxDate:MMM dd HH:mm}})</td>
                                <td>{{secondHalfWinRate.ToString("F1")}}%</td>
                                <td class="{{(secondHalfPnL >= 0 ? "positive" : "negative")}}">
                                    {{secondHalfPnL.ToString("F1")}}
                                </td>
                                <td>{{secondHalfCount}}</td>
                            </tr>
                        </table>
                    </div>
                """;

                return ReportSectionResult.CreateSuccess("Performance Over Time", html);
            }
            catch (Exception ex)
            {
                return ReportSectionResult.CreateError("Performance Over Time", ex.Message);
            }
        }

        private ReportSectionResult GenerateCriticalTradesSafe(string sessionId, ReportSettings settings, List<Trade> allTrades)
        {
            try
            {
                var criticalTrades = allTrades
                    .Where(t => t.Profit.HasValue)
                    .OrderBy(t => t.Profit)
                    .Take(5);

                if (!criticalTrades.Any())
                {
                    return ReportSectionResult.CreateSuccess("Critical Trades", """
                <div class="section">
                    <h2>⚠️ Critical Trades</h2>
                    <div class="suggestion">No critical trades to display</div>
                </div>
            """);
                }

                var html = new StringBuilder(); // ← FIX: Use StringBuilder
                html.AppendLine("""
            <div class="section">
                <h2>⚠️ Critical Trades</h2>
                <table>
                    <tr>
                        <th>Symbol</th>
                        <th>Type</th>
                        <th>PnL</th>
                        <th>Entry</th>
                        <th>Exit</th>
                    </tr>
            """);

                foreach (var trade in criticalTrades)
                {
                    html.AppendLine($$"""
                <tr class="critical-trade">
                    <td>{{trade.Symbol}}</td>
                    <td>{{(trade.IsLong ? "Long" : "Short")}}</td>
                    <td class="negative">{{trade.Profit?.ToString("F1")}}</td>
                    <td>{{trade.EntryPrice.ToString(settings.NumberFormat)}} @ {{trade.EntryTime:HH:mm}}</td>
                    <td>{{trade.ExitPrice?.ToString(settings.NumberFormat) ?? "Open"}}</td>
                </tr>
            """);
                }

                html.AppendLine("</table></div>");

                return ReportSectionResult.CreateSuccess("Critical Trades", html.ToString());
            }
            catch (Exception ex)
            {
                return ReportSectionResult.CreateError("Critical Trades", ex.Message);
            }
        }


        private ReportSectionResult GenerateCoinPerformanceSafe(string sessionId, ReportSettings settings, List<Trade> allTrades)
        {
            try
            {
                var coinPerformance = _tradeLogger.GetCoinPerformance(sessionId);

                var html = new StringBuilder(); // ← FIX: Use StringBuilder
                html.AppendLine("""
            <div class="section">
                <h2>🏆 Coin Performance</h2>
                <div style="display: flex; gap: 20px;">
                    <div style="flex: 1;">
                        <h3>Top Performers</h3>
                        <table>
                            <tr><th>Symbol</th><th>PnL</th></tr>
            """);

                // Top performers
                foreach (var coin in coinPerformance.OrderByDescending(kvp => kvp.Value).Take(5))
                {
                    html.AppendLine($$"""
                <tr>
                    <td>{{coin.Key}}</td>
                    <td class="positive">▲ {{coin.Value.ToString(settings.NumberFormat)}}</td>
                </tr>
            """);
                }

                html.AppendLine("""
                        </table>
                    </div>
                    <div style="flex: 1;">
                        <h3>Worst Performers</h3>
                        <table>
                            <tr><th>Symbol</th><th>PnL</th></tr>
            """);

                // Worst performers
                foreach (var coin in coinPerformance.OrderBy(kvp => kvp.Value).Take(5))
                {
                    html.AppendLine($$"""
                <tr>
                    <td>{{coin.Key}}</td>
                    <td class="negative">▼ {{coin.Value.ToString(settings.NumberFormat)}}</td>
                </tr>
            """);
                }

                html.AppendLine("""
                        </table>
                    </div>
                </div>
            </div>
        """);

                return ReportSectionResult.CreateSuccess("Coin Performance", html.ToString());
            }
            catch (Exception ex)
            {
                return ReportSectionResult.CreateError("Coin Performance", ex.Message);
            }
        }

        private ReportSectionResult GenerateRiskAnalysisSafe(string sessionId, ReportSettings settings, List<Trade> allTrades)
        {
            try
            {
                var (nearLiquidation, _) = _tradeLogger.CalculateLiquidationStats(sessionId, 0.9m);

                var html = $$"""
                    <div class="section">
                        <h2>⚠️ Risk Analysis</h2>
                        <div class="metric-grid">
                            <div class="metric-card">
                                <h3>Liquidation Threshold</h3>
                                <p class="negative">{{settings.GetLiquidationThreshold().ToString("F2")}}%</p>
                            </div>
                            <div class="metric-card">
                                <h3>Near-Liquidations</h3>
                                <p class="{{(nearLiquidation == 0 ? "positive" : "negative")}}">{{nearLiquidation}}</p>
                            </div>
                            <div class="metric-card">
                                <h3>Max Loss</h3>
                                <p class="negative">{{allTrades.Min(t => t.Profit ?? 0).ToString("F1")}}</p>
                            </div>
                        </div>
                    </div>
                """;

                return ReportSectionResult.CreateSuccess("Risk Analysis", html);
            }
            catch (Exception ex)
            {
                return ReportSectionResult.CreateError("Risk Analysis", ex.Message);
            }
        }

        private async Task<ReportSectionResult> GenerateBtcMarketContextSafe(string sessionId, ReportSettings settings, List<Trade> allTrades)
        {
            try
            {
                var marketContext = await _marketAnalyzer.AnalyzePeriodAsync(
                    "BTCUSDT",
                    allTrades.Min(t => t.EntryTime),
                    allTrades.Max(t => t.EntryTime),
                    settings.Interval,
                    allTrades
                );

                // Safe access to properties with null checks
                var generalRegime = marketContext.GeneralMarketRegime ?? new MarketRegime { Type = MarketRegimeType.Unknown };
                var tradingRegime = marketContext.TradingAlignedRegime ?? new MarketRegime { Type = MarketRegimeType.Unknown };
                var generalPerformance = marketContext.PerformanceByGeneralRegime?.ContainsKey(generalRegime.Type) == true
                    ? marketContext.PerformanceByGeneralRegime[generalRegime.Type]
                    : new StrategyPerformance();
                var tradingPerformance = marketContext.PerformanceByTradingRegime?.ContainsKey(tradingRegime.Type) == true
                    ? marketContext.PerformanceByTradingRegime[tradingRegime.Type]
                    : new StrategyPerformance();

                var html = $$"""
            <div class="section">
                <h2>🌊 BTC Market Context Analysis</h2>
                
                <!-- General Market Context (1h/4h) -->
                <h3>📊 General Market Context (1h/4h Analysis)</h3>
                <div class="metric-grid">
                    <div class="metric-card">
                        <h3>Market Regime</h3>
                        <p class="{{GetRegimeColorClass(generalRegime.Type)}}">
                            {{GetRegimeIcon(generalRegime.Type)}} {{generalRegime.Type}}
                        </p>
                        <small>Confidence: {{generalRegime.OverallConfidence}}%</small>
                    </div>
                    <div class="metric-card">
                        <h3>Strategy Performance</h3>
                        <p class="{{(generalPerformance.TotalPnL >= 0 ? "positive" : "negative")}}">
                            {{generalPerformance.TotalPnL.ToString("F2")}} PnL
                        </p>
                        <small>{{generalPerformance.WinRate.ToString("F1")}}% win rate</small>
                    </div>
                    <div class="metric-card">
                        <h3>Trend Strength</h3>
                        <p>{{generalRegime.TrendStrength}}</p>
                        <small>{{marketContext.GeneralTrendAnalysis?.Primary1H?.PriceVs200EMA.ToString("F1") ?? "0.0"}}% vs 200EMA</small>
                    </div>
                </div>

                <!-- Trading-Aligned Context -->
                <h3>🎯 Trading-Aligned Context ({{GetTradingTimeframeDescription(settings.Interval)}} Analysis)</h3>
                <div class="metric-grid">
                    <div class="metric-card">
                        <h3>Market Regime</h3>
                        <p class="{{GetRegimeColorClass(tradingRegime.Type)}}">
                            {{GetRegimeIcon(tradingRegime.Type)}} {{tradingRegime.Type}}
                        </p>
                        <small>Confidence: {{tradingRegime.OverallConfidence}}%</small>
                    </div>
                    <div class="metric-card">
                        <h3>Strategy Performance</h3>
                        <p class="{{(tradingPerformance.TotalPnL >= 0 ? "positive" : "negative")}}">
                            {{tradingPerformance.TotalPnL.ToString("F2")}} PnL
                        </p>
                        <small>{{tradingPerformance.WinRate.ToString("F1")}}% win rate</small>
                    </div>
                    <div class="metric-card">
                        <h3>Trend Strength</h3>
                        <p>{{tradingRegime.TrendStrength}}</p>
                        <small>{{marketContext.TradingTrendAnalysis?.Primary1H?.PriceVs200EMA.ToString("F1") ?? "0.0"}}% vs 200EMA</small>
                    </div>
                </div>

                <!-- Regime Comparison -->
                {{GenerateRegimeComparison(generalRegime, tradingRegime)}}
            </div>
        """;

                return ReportSectionResult.CreateSuccess("BTC Market Context", html);
            }
            catch (Exception ex)
            {
                return ReportSectionResult.CreateError("BTC Market Context", ex.Message);
            }
        }

        private async Task<ReportSectionResult> GenerateRegimeTimelineSafe(string sessionId, ReportSettings settings, List<Trade> allTrades)
        {
            try
            {
                var regimeSegments = await _marketAnalyzer.GetRegimeSegmentsAsync(
                    allTrades.Min(t => t.EntryTime),
                    allTrades.Max(t => t.EntryTime),
                    settings.Interval
                );

                // Correlate trades with segments
                regimeSegments = CorrelateTradesWithSegments(regimeSegments, allTrades);

                // SAVE regimeSegments for later use
                savedRegimeSegments = regimeSegments;

                var html = GenerateRegimeTimelineHtml(regimeSegments);
                return ReportSectionResult.CreateSuccess("Market Regime Timeline", html);
            }
            catch (Exception ex)
            {
                return ReportSectionResult.CreateError("Market Regime Timeline", ex.Message);
            }
        }

        private async Task<ReportSectionResult> GenerateStrategyHeatmapSafe(string sessionId, ReportSettings settings, List<Trade> allTrades)
        {
            try
            {
                var regimeTF = GetRegimeTimeframe(settings.Interval);
                var tradingRegimes = await _marketAnalyzer.GetRegimeSegmentsAsync(
                    allTrades.Min(t => t.EntryTime),
                    allTrades.Max(t => t.EntryTime),
                    regimeTF
                );

                var heatmapCalculator = new HeatmapCalculatorService();
                var regimePerformance = heatmapCalculator.CalculateStrategyRegimePerformance(allTrades, tradingRegimes);

                var html = GenerateStrategyHeatmapHtml(regimePerformance);
                return ReportSectionResult.CreateSuccess("Strategy vs Regime Heatmap", html);
            }
            catch (Exception ex)
            {
                return ReportSectionResult.CreateError("Strategy vs Regime Heatmap", ex.Message);
            }
        }

        private ReportSectionResult GenerateActionableInsightsSafe(string sessionId, ReportSettings settings, List<Trade> allTrades)
        {
            try
            {
                var strategyPerformance = _tradeLogger.GetStrategyPerformance(sessionId);
                var coinPerformance = _tradeLogger.GetCoinPerformance(sessionId);

                var html = GenerateRegimeInsightsHtml(savedRegimeSegments, allTrades, strategyPerformance, coinPerformance);
                return ReportSectionResult.CreateSuccess("Actionable Insights", html);
            }
            catch (Exception ex)
            {
                return ReportSectionResult.CreateError("Actionable Insights", ex.Message);
            }
        }

        private ReportSectionResult GenerateDayOfWeekAnalysisSafe(string sessionId, ReportSettings settings, List<Trade> allTrades)
        {
            try
            {
                var html = GetDayOfWeekAnalysis(allTrades);
                return ReportSectionResult.CreateSuccess("Day-of-Week Analysis", html);
            }
            catch (Exception ex)
            {
                return ReportSectionResult.CreateError("Day-of-Week Analysis", ex.Message);
            }
        }

        private ReportSectionResult GenerateExtendedHoursAnalysisSafe(string sessionId, ReportSettings settings, List<Trade> allTrades)
        {
            try
            {
                var html = GetExtendedHoursAnalysis(allTrades);
                return ReportSectionResult.CreateSuccess("Extended Hours Analysis", html);
            }
            catch (Exception ex)
            {
                return ReportSectionResult.CreateError("Extended Hours Analysis", ex.Message);
            }
        }

        private ReportSectionResult GenerateWeekendWeekdayAnalysisSafe(string sessionId, ReportSettings settings, List<Trade> allTrades)
        {
            try
            {
                var html = GetWeekendWeekdayAnalysis(allTrades);
                return ReportSectionResult.CreateSuccess("Weekend vs Weekday Analysis", html);
            }
            catch (Exception ex)
            {
                return ReportSectionResult.CreateError("Weekend vs Weekday Analysis", ex.Message);
            }
        }

        private ReportSectionResult GenerateMarketSessionAnalysisSafe(string sessionId, ReportSettings settings, List<Trade> allTrades)
        {
            try
            {
                var html = new StringBuilder(); // ← FIX: Use StringBuilder instead of string

                html.AppendLine($$"""
            <div class="section">
                <h2>🌐 Market Session Performance - All Trades</h2>
                <table>
                    <tr>
                        <th>Session</th>
                        <th>Trades</th>
                        <th>Avg PnL</th>
                        <th>Win Rate</th>
                    </tr>
                    {{GetMarketSessionAnalysis(allTrades)}}
                </table>
            </div>
            <div class="section">
                <h2>🌐 Market Session Performance - Realistic Limited Trades</h2>
                <table>
                    <tr>
                        <th>Session</th>
                        <th>Trades</th>
                        <th>Avg PnL</th>
                        <th>Win Rate</th>
                    </tr>
        """);

                // Add realistic trades simulation for comparison
                var simulator = new StrictConcurrentTradeSimulator();
                var simulatedTrades = simulator.Simulate(allTrades, 8);
                var executedTrades = simulatedTrades.Where(t => t.WasExecuted).Select(t => t.OriginalTrade).ToList();

                html.AppendLine(GetMarketSessionAnalysis(executedTrades));
                html.AppendLine("</table></div>");

                return ReportSectionResult.CreateSuccess("Market Session Analysis", html.ToString());
            }
            catch (Exception ex)
            {
                return ReportSectionResult.CreateError("Market Session Analysis", ex.Message);
            }
        }

    }
}