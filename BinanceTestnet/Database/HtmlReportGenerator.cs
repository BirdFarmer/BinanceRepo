using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace BinanceTestnet.Database
{
    public class HtmlReportGenerator
    {
        private readonly TradeLogger _tradeLogger;
        
        public HtmlReportGenerator(TradeLogger tradeLogger)
        {
            _tradeLogger = tradeLogger;
        }

        private decimal CalculateWinRate(List<Trade> trades)
        {
            if (trades.Count == 0) return 0;
            return (decimal)trades.Count(t => t.Profit > 0) / trades.Count * 100;
        }

        public string GenerateHtmlReport(string sessionId, ReportSettings settings)
        {
            try
            {
                var allTrades = _tradeLogger.GetTrades(sessionId);
                var metrics = _tradeLogger.CalculatePerformanceMetrics(sessionId);
                var strategyPerformance = _tradeLogger.GetStrategyPerformance(sessionId);
                var coinPerformance = _tradeLogger.GetCoinPerformance(sessionId);
                var tradeDistribution = _tradeLogger.GetTradeDistribution(sessionId);

                // Calculate time segments
                var minDate = allTrades.Min(t => t.EntryTime);
                var maxDate = allTrades.Max(t => t.EntryTime);
                var midpoint = minDate.AddDays((maxDate - minDate).TotalDays / 2);

                var firstHalfTrades = allTrades.Where(t => t.EntryTime < midpoint).ToList();
                var secondHalfTrades = allTrades.Where(t => t.EntryTime >= midpoint).ToList();

                // Pre-calculate all metrics
                var firstHalfWinRate = CalculateWinRate(firstHalfTrades);
                var secondHalfWinRate = CalculateWinRate(secondHalfTrades);
                var firstHalfPnL = firstHalfTrades.Sum(t => t.Profit ?? 0);
                var secondHalfPnL = secondHalfTrades.Sum(t => t.Profit ?? 0);
                var firstHalfCount = firstHalfTrades.Count;
                var secondHalfCount = secondHalfTrades.Count;

                // Run the simulation with 8-trade limit
                var simulator = new StrictConcurrentTradeSimulator();
                var simulatedTrades = simulator.Simulate(allTrades, 8);
                
                // Get statistics
                var executedTrades = simulatedTrades.Where(t => t.WasExecuted).Select(t => t.OriginalTrade).ToList();
                var skippedTrades = simulatedTrades.Where(t => !t.WasExecuted).ToList();
                
                var limitedMetrics = CalculatePerformanceMetrics(executedTrades);

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

                // 2. Complete Strategy Parameters (all strategies)
                html.AppendLine($$"""
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
                """);

                // 3. Enhanced Performance Snapshot
                html.AppendLine($$"""
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
                                <h3>ROI</h3>
                                <p>{{(metrics.NetProfit/(settings.MarginPerTrade*metrics.TotalTrades)).ToString("F2")}}</p>
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
                """);

                // Add comparison section
                html.AppendLine($$"""
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
                """);

                html.AppendLine($$"""
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
                """);

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
                    .GroupBy(t => (int)(t.Duration / 30)) // 30-minute intervals
                    .OrderBy(g => g.Key)
                    .Select(g => new {
                        Range = $"{g.Key * 30}-{(g.Key + 1) * 30} mins",
                        Count = g.Count(),
                        AvgProfit = g.Average(t => t.Profit ?? 0),
                        WinRate = CalculateWinRate(g.ToList())
                    });

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

                html.AppendLine("""
                        </table>
                    </div>
                """);

                // 4. Detailed Active Window (with date)
                var activeWindow = allTrades
                    .GroupBy(t => t.EntryTime.ToString("yyyy-MM-dd HH:mm"))
                    .OrderByDescending(g => g.Count())
                    .First();
                
                html.AppendLine($$"""
                    <div class="section">
                        <h2>⏰ Most Active Trading Window</h2>
                        <div class="warning">
                            <strong>{{activeWindow.Key}} UTC</strong><br>
                            {{activeWindow.Count()}} trades executed<br>
                            Avg PnL: {{activeWindow.Average(t => t.Profit ?? 0).ToString("P2")}}
                        </div>
                    </div>
                """);

                // 5. Full Strategy Comparison
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

                html.AppendLine("""
                        </table>
                    </div>
                """);

                // 6. Risk Analysis
                var (nearLiquidation, _) = _tradeLogger.CalculateLiquidationStats(sessionId, 0.9m);
                html.AppendLine($$"""
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
                """);

                // 4. Session Segmentation                
                var (candleCount, tradesPerCandle) = _tradeLogger.GetCandleMetrics(sessionId, settings.GetIntervalMinutes());
                html.AppendLine($$"""
                    <div class="section">
                        <h2>⏱️ Session Segmentation</h2>
                        <table>
                            <tr><th>Metric</th><th>First Half</th><th>Second Half</th><th>Change</th></tr>
                            <tr>
                                <td>Trades</td>
                                <td>{{firstHalfCount}}</td>
                                <td>{{secondHalfCount}}</td>
                                <td class="{{(secondHalfCount >= firstHalfCount ? "positive" : "negative")}}">
                                    {{(secondHalfCount >= firstHalfCount ? "▲" : "▼")}} {{Math.Abs(secondHalfCount - firstHalfCount)}}
                                </td>
                            </tr>
                            <tr>
                                <td>Win Rate</td>
                                <td>{{firstHalfWinRate:F1}}%</td>
                                <td>{{secondHalfWinRate:F1}}%</td>
                                <td class="{{(secondHalfWinRate >= firstHalfWinRate ? "positive" : "negative")}}">
                                    {{(secondHalfWinRate >= firstHalfWinRate ? "▲" : "▼")}} {{Math.Abs(secondHalfWinRate - firstHalfWinRate):F1}}pp
                                </td>
                            </tr>
                            <tr>
                                <td>Net PnL</td>
                                <td class="{{(firstHalfPnL >= 0 ? "positive" : "negative")}}">{{firstHalfPnL.ToString("F2")}}</td>
                                <td class="{{(secondHalfPnL >= 0 ? "positive" : "negative")}}">{{secondHalfPnL.ToString("F2")}}</td>
                                <td class="{{(secondHalfPnL >= firstHalfPnL ? "positive" : "negative")}}">
                                    {{(secondHalfPnL >= firstHalfPnL ? "▲" : "▼")}} {{(firstHalfPnL != 0 ? Math.Abs((secondHalfPnL - firstHalfPnL) / Math.Abs(firstHalfPnL) * 100) : 0):F1}}%
                                </td>
                            </tr>
                        </table>
                        <div class="warning">
                            <strong>⚠️ Most Active Window:</strong> {{allTrades.GroupBy(t => t.EntryTime.ToString("HH:mm")).OrderByDescending(g => g.Count()).First().Key}} UTC
                        </div>
                    </div>                    
                    <div class="section">
                        <h2>🌐 Market Session Performance All Trades</h2>
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
                        <h2>🌐 Market Session Performance Realistic Limited Trades</h2>
                        <table>
                            <tr>
                                <th>Session</th>
                                <th>Trades</th>
                                <th>Avg PnL</th>
                                <th>Win Rate</th>
                            </tr>
                            {{GetMarketSessionAnalysis(executedTrades)}}
                        </table>
                    </div>             
                """);   

                // 5. Critical Trades (Top 5)
                var criticalTrades = allTrades
                    .Where(t => t.Profit.HasValue)
                    .OrderBy(t => t.Profit)
                    .Take(5);
                
                if (criticalTrades.Any())
                {
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

                    html.AppendLine("""
                                </table>
                            </div>
                        """);
                }

                // 6. Top/Bottom Performers
                coinPerformance = _tradeLogger.GetCoinPerformance(sessionId);
                html.AppendLine("""
                        <div class="section">
                            <h2>🏆 Coin Performance</h2>
                            <div style="display: flex; gap: 20px;">
                                <div style="flex: 1;">
                                    <h3>Top Performers</h3>
                                    <table>
                                        <tr><th>Symbol</th><th>PnL</th></tr>
                        """);

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

                // 7. Footer
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
                .Select(g => new {
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

        private string GetMarketSession(DateTime time)
        {
            int hour = time.Hour;
            if (hour >= 0 && hour < 5) return "Late NY (00-05 UTC)";
            if (hour >= 5 && hour < 9) return "Asia (05-09 UTC)";
            if (hour >= 9 && hour < 14) return "London (09-14 UTC)";
            if (hour >= 14 && hour < 18) return "NY (14-18 UTC)";
            return "Evening (18-24 UTC)";
        }        

    }
}