using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BinanceTestnet.Database
{
    public class EnhancedReportGenerator
    {
        private readonly TradeLogger _tradeLogger;
        private readonly TextWriter _writer;

        // Style configuration
        private const string DIVIDER = "╠════════════════════════════════════╣";
        private const string HEADER_LINE = "║ {0,-36} ║";
        private const string WARNING_ICON = "◼";
        private const string POSITIVE_CHANGE = "▲";
        private const string NEGATIVE_CHANGE = "▼";
        
        public EnhancedReportGenerator(TradeLogger tradeLogger, TextWriter writer)
        {
            _tradeLogger = tradeLogger;
            _writer = writer;
        }
        
        public void GenerateEnhancedReport(string sessionId, ReportSettings settings)
        {
            try
            {
        
                WriteHeader(sessionId, settings);
                WriteParameters(sessionId, settings);
                WritePerformanceSnapshot(sessionId, settings);
                WriteSessionSegmentation(sessionId, settings);  
                WriteRiskAnalysis(sessionId, settings);
                WriteStrategyComparison(sessionId, settings);      
                WriteTradingHoursAnalysis(sessionId, settings);  // Market session trends
                WriteExitClusterAnalysis(sessionId, settings);   // Crypto-specific patterns          
                WriteTradeHighlights(sessionId, settings);
                WriteCoinPerformance(sessionId);
                var allTrades = _tradeLogger.GetTrades(sessionId);
                ValidateTradeProfits(allTrades, settings.MarginPerTrade);
                WriteFooter();
            }
            catch (Exception ex)
            {
                _writer.WriteLine($"\n!!! REPORT GENERATION FAILED: {ex.Message} !!!");
            }
        }

        private void WriteHeader(string sessionId, ReportSettings settings)
        {
            var sessionOverview = _tradeLogger.GetSessionOverview(sessionId);
            TimeZoneInfo localZone = TimeZoneInfo.Local;
            _writer.WriteLine("==================================================");
            _writer.WriteLine($"        TRADING REPORT - SESSION {sessionId}");
            _writer.WriteLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss} ({localZone.StandardName})");      
            _writer.WriteLine("\n");      
            _writer.WriteLine($"First Trade:\t\t{sessionOverview.StartTime.ToString(settings.DateFormat)} UTC");
            _writer.WriteLine($"Last Trade:\t\t{sessionOverview.EndTime.ToString(settings.DateFormat)} UTC");
            _writer.WriteLine("==================================================\n");
        }

        private void WriteStyledHeader(string title)
        {
            _writer.WriteLine($"╔{new string('═', 38)}╗");
            _writer.WriteLine(string.Format(HEADER_LINE, title));
            _writer.WriteLine($"╚{new string('═', 38)}╝");
        }

        private string FormatChangeStyled(decimal first, decimal second, string unit = "")
        {
            if (first == 0) return "N/A";
            decimal change = (second - first) / Math.Abs(first) * 100;
            string icon = change >= 0 ? POSITIVE_CHANGE : NEGATIVE_CHANGE;
            return $"{icon} {Math.Abs(change):F1}%{unit}";
        }

        private void WriteParameters(string sessionId, ReportSettings settings)
        {
            var sessionOverview = _tradeLogger.GetSessionOverview(sessionId);
            _writer.WriteLine("================ STRATEGY PARAMETERS ================");    
            // If we have multiple strategies, show them all
            var allTrades = _tradeLogger.GetTrades(sessionId);
            var distinctStrategies = allTrades.Select(t => t.Signal).Distinct().ToList();
            
            if (distinctStrategies.Count > 1)
            {
                _writer.WriteLine($"Strategies Used:\t{string.Join(", ", distinctStrategies)}");
            }
            else
            {
                _writer.WriteLine($"Strategy:\t\t{settings.StrategyName}");
            }

            _writer.WriteLine($"Leverage:\t\t{settings.Leverage}x (Isolated)");
            _writer.WriteLine($"Take Profit:\t\t{settings.TakeProfitMultiplier.ToString(settings.NumberFormat)}x ATR(14)");
            _writer.WriteLine($"Stop Loss Ratio:\t1:{settings.StopLossRatio}"); // Changed to show actual ratio
            _writer.WriteLine($"Margin/Trade:\t\t${settings.MarginPerTrade.ToString(settings.NumberFormat)}");
            _writer.WriteLine($"Interval:\t\t{settings.Interval}");
            _writer.WriteLine($"Estimated Liquidation:\t{-100m/settings.Leverage:N2}%");
            _writer.WriteLine();
        }

        private void WritePerformanceSnapshot(string sessionId, ReportSettings settings)
        {
            var allTrades = _tradeLogger.GetTrades(sessionId);
            var metrics = CalculateCappedPerformanceMetrics(allTrades, settings.MarginPerTrade);

            var (avgWinSize, avgLossSize, largestWin, largestLoss, _, _, avgTradeDuration, 
                minTradeDuration, maxTradeDuration, winningTradesCount, losingTradesCount) = 
                _tradeLogger.CalculateAdditionalMetrics(sessionId);
            
            var strategyPerformance = _tradeLogger.GetStrategyPerformance(sessionId);
            var tradeDistribution = _tradeLogger.GetTradeDistribution(sessionId);            
            
            // For position direction analysis
            var longTrades = allTrades.Where(t => t.IsLong).ToList();
            var shortTrades = allTrades.Where(t => !t.IsLong).ToList();
            
            var longMetrics = CalculatePositionPerformance(longTrades);
            var shortMetrics = CalculatePositionPerformance(shortTrades);
            
            _writer.WriteLine("=============== PERFORMANCE SNAPSHOT ===============");
            _writer.WriteLine($"► Win Rate:\t\t{metrics.WinRate.ToString(settings.NumberFormat)}% ({winningTradesCount} Wins / {losingTradesCount} Losses)");
            _writer.WriteLine($"► Net PnL:\t\t{metrics.NetProfit.ToString(settings.NumberFormat)}");
            decimal totalMarginUsed = settings.MarginPerTrade * metrics.TotalTrades;
            decimal roi = totalMarginUsed > 0 ? metrics.NetProfit / totalMarginUsed : 0;
            _writer.WriteLine($"► ROI (Margin):\t\t{roi.ToString("P2")}");
            _writer.WriteLine($"► Max Drawdown:\t\t{metrics.MaximumDrawdown.ToString(settings.NumberFormat)}");
            _writer.WriteLine($"► Sharpe Ratio:\t\t{metrics.SharpeRatio.ToString(settings.NumberFormat)}");
            _writer.WriteLine($"► Avg Trade Duration:\t{avgTradeDuration.ToString(settings.NumberFormat)} mins");
            _writer.WriteLine($"► Long/Short Ratio:\t{tradeDistribution.LongTrades}/{tradeDistribution.ShortTrades}\n");

            _writer.WriteLine("--- Position Direction Analysis ---");
            _writer.WriteLine($"Longs: {tradeDistribution.LongTrades} trades | " + 
                            $"Avg: {longMetrics.AvgProfit.ToString(settings.NumberFormat)} | " +
                            $"Best: {longMetrics.BestTrade.ToString(settings.NumberFormat)} | " +
                            $"Worst: {longMetrics.WorstTrade.ToString(settings.NumberFormat)}");
            _writer.WriteLine($"Shorts: {tradeDistribution.ShortTrades} trades | " +
                            $"Avg: {shortMetrics.AvgProfit.ToString(settings.NumberFormat)} | " +
                            $"Best: {shortMetrics.BestTrade.ToString(settings.NumberFormat)} | " +
                            $"Worst: {shortMetrics.WorstTrade.ToString(settings.NumberFormat)}");
            _writer.WriteLine($"Long Win Rate: {longMetrics.WinRate.ToString(settings.NumberFormat)}%");
            _writer.WriteLine($"Short Win Rate: {shortMetrics.WinRate.ToString(settings.NumberFormat)}%\n");
            
            
            var sessionOverview = _tradeLogger.GetSessionOverview(sessionId);
            var (naturalExits, forcedExits) = GetExitsByType(sessionId, sessionOverview.EndTime);

            _writer.WriteLine("\n=== EXIT TYPE ANALYSIS ===");
            _writer.WriteLine($"Natural Exits: {naturalExits.Count} trades | " +
                            $"Avg PnL: {naturalExits.Average(t => t.Profit ?? 0).ToString(settings.NumberFormat)}");
            _writer.WriteLine($"Forced Closures: {forcedExits.Count} trades | " +
                            $"Avg PnL: {forcedExits.Average(t => t.Profit ?? 0).ToString(settings.NumberFormat)}");            
            
            if (strategyPerformance.Count > 1)
            {
                _writer.WriteLine("--- Strategy Breakdown ---");
                foreach (var strategy in strategyPerformance)
                {
                    _writer.WriteLine($"{strategy.Key}:\t{strategy.Value.ToString(settings.NumberFormat)}");
                }
                _writer.WriteLine();
            }
        }

        private void WriteRiskAnalysis(string sessionId, ReportSettings settings)
        {
            var metrics = _tradeLogger.CalculatePerformanceMetrics(sessionId);
            var trades = _tradeLogger.GetTrades(sessionId);
            var (_, _, _, largestLoss, _, _, _, _, _, _, _) = _tradeLogger.CalculateAdditionalMetrics(sessionId);
            
            int nearLiquidations = trades.Count(t => 
                t.ExitPrice.HasValue && 
                settings.IsNearLiquidation((t.ExitPrice.Value - t.EntryPrice) / t.EntryPrice * 100));
            
            _writer.WriteLine("=============== RISK ANALYSIS ===============");
            _writer.WriteLine($"! Liquidation Threshold:\t{settings.GetLiquidationThreshold().ToString(settings.NumberFormat)}%");
            _writer.WriteLine($"! Near-Liquidations:\t\t{nearLiquidations} trades");
            _writer.WriteLine($"! Max Loss:\t\t\t{largestLoss.ToString(settings.NumberFormat)}\n");
        }
        
        private void WriteTradeHighlights(string sessionId, ReportSettings settings)
        {
            // Only get completed trades (those with exit price)
            var trades = _tradeLogger.GetTrades(sessionId)
                        .Where(t => t.ExitPrice.HasValue)
                        .OrderBy(t => t.EntryTime)
                        .ToList();
                        
            var criticalTrades = trades
                .OrderBy(t => t.Profit ?? 0)
                .Take(settings.MaxCriticalTradesToShow)
                .ToList();
            
            // Rest of the method remains the same...
            _writer.WriteLine("============== TRADE HIGHLIGHTS ==============");
            _writer.WriteLine($"▲ Best Trade:\t\t{trades.OrderByDescending(t => t.Profit ?? 0).FirstOrDefault()?.Symbol}");
            _writer.WriteLine($"▼ Worst Trade:\t\t{criticalTrades.FirstOrDefault()?.Symbol}");
            _writer.WriteLine($"⚡ Fastest Close:\t{trades.OrderBy(t => t.Duration).FirstOrDefault()?.Symbol}");
            _writer.WriteLine($"⏳ Longest Trade:\t{trades.OrderByDescending(t => t.Duration).FirstOrDefault()?.Symbol}\n");
            
            // Strategy performance comparison
            var strategyPerformance = _tradeLogger.GetStrategyPerformance(sessionId);
            if (strategyPerformance.Count > 1)
            {
                var bestStrategy = strategyPerformance.OrderByDescending(kvp => kvp.Value).First();
                var worstStrategy = strategyPerformance.OrderBy(kvp => kvp.Value).First();
                _writer.WriteLine($"★ Best Strategy:\t{bestStrategy.Key} ({bestStrategy.Value.ToString(settings.NumberFormat)})");
                _writer.WriteLine($"☆ Worst Strategy:\t{worstStrategy.Key} ({worstStrategy.Value.ToString(settings.NumberFormat)})\n");
            }
            
            if (settings.ShowTradeDetails && criticalTrades.Any())
            {
                _writer.WriteLine("▼▼▼ CRITICAL TRADES (COMPLETED ONLY) ▼▼▼");
                foreach (var trade in criticalTrades)
                {
                    _writer.WriteLine($"\n{trade.Symbol} {trade.TradeType} {trade.Profit?.ToString(settings.NumberFormat)}");
                    _writer.WriteLine($"  Entry: {trade.EntryPrice.ToString(settings.NumberFormat)} @ {trade.EntryTime.ToString(settings.DateFormat)}");
                    
                    // Since we filtered for completed trades, we know ExitPrice has value
                    decimal pctChange = (trade.ExitPrice!.Value - trade.EntryPrice) / trade.EntryPrice * 100;
                    _writer.WriteLine($"  Exit: {trade.ExitPrice.Value.ToString(settings.NumberFormat)} ({pctChange.ToString(settings.NumberFormat)}%)");
                    _writer.WriteLine($"  Duration: {trade.Duration} mins | Leverage: {trade.Leverage}x | Strategy: {trade.Signal}");
                    
                    if (settings.IsNearLiquidation(pctChange))
                    {
                        _writer.WriteLine("  ⚠️ NEAR LIQUIDATION ⚠️");
                    }
                }
                _writer.WriteLine("\n");
            }
        }

        private void WriteCoinPerformance(string sessionId)
        {
            // Get all coin performance data once (more efficient)
            var allCoins = _tradeLogger.GetCoinPerformance(sessionId);

            // Top 10 performing coins (descending order)
            var topCoins = allCoins.OrderByDescending(kvp => kvp.Value).Take(10);
            _writer.WriteLine("============= TOP PERFORMING COINS =============");
            foreach (var coin in topCoins)
            {
                _writer.WriteLine($"{coin.Key}: {coin.Value.ToString("F8")}");
            }
            _writer.WriteLine("\n");

            // Bottom 10 performing coins (ascending order)
            var worstCoins = allCoins.OrderBy(kvp => kvp.Value).Take(10);
            _writer.WriteLine("============= WORST PERFORMING COINS =============");
            foreach (var coin in worstCoins)
            {
                _writer.WriteLine($"{coin.Key}: {coin.Value.ToString("F8")}");
            }
            _writer.WriteLine("\n");
        }

        private void WriteStrategyComparison(string sessionId, ReportSettings settings)
        {
            var strategyPerformance = _tradeLogger.GetStrategyPerformance(sessionId);
            if (strategyPerformance.Count <= 1) return;
            
            _writer.WriteLine("============= STRATEGY COMPARISON =============");
            
            foreach (var strategy in strategyPerformance.OrderByDescending(kvp => kvp.Value))
            {
                var strategyTrades = _tradeLogger.GetTrades(sessionId)
                                    .Where(t => t.Signal == strategy.Key)
                                    .ToList();
                
                var metrics = CalculateCappedPerformanceMetrics(strategyTrades, settings.MarginPerTrade);
                var longTrades = strategyTrades.Where(t => t.IsLong).ToList();
                var shortTrades = strategyTrades.Where(t => !t.IsLong).ToList();

                decimal totalMarginUsed = settings.MarginPerTrade * metrics.TotalTrades;
                decimal roi = totalMarginUsed > 0 ? metrics.NetProfit / totalMarginUsed : 0;
                
                _writer.WriteLine($"\n★ {strategy.Key} ★");
                _writer.WriteLine($"  Net PnL:\t\t{strategy.Value.ToString(settings.NumberFormat)}");
                _writer.WriteLine($"► ROI (Margin):\t\t{roi.ToString("P2")}");
                _writer.WriteLine($"  Win Rate:\t\t{metrics.WinRate.ToString(settings.NumberFormat)}%");
                _writer.WriteLine($"  Sharpe Ratio:\t\t{metrics.SharpeRatio.ToString(settings.NumberFormat)}");
                _writer.WriteLine($"  Trades Count:\t\t{strategyTrades.Count}");
                
                // Add long/short breakdown
                _writer.WriteLine($"  Long Trades:\t\t{longTrades.Count} " + 
                                $"(Win Rate: {CalculateWinRate(longTrades).ToString(settings.NumberFormat)}%)");
                _writer.WriteLine($"  Short Trades:\t\t{shortTrades.Count} " +
                                $"(Win Rate: {CalculateWinRate(shortTrades).ToString(settings.NumberFormat)}%)");
                
                // Optional: Add best/worst for each direction
                _writer.WriteLine($"  Best Long:\t\t{GetBestTrade(longTrades)?.Profit?.ToString(settings.NumberFormat) ?? "N/A"}");
                _writer.WriteLine($"  Worst Long:\t\t{GetWorstTrade(longTrades)?.Profit?.ToString(settings.NumberFormat) ?? "N/A"}");                
                _writer.WriteLine($"  Best Short:\t\t{GetBestTrade(shortTrades)?.Profit?.ToString(settings.NumberFormat) ?? "N/A"}");
                _writer.WriteLine($"  Worst Short:\t\t{GetWorstTrade(shortTrades)?.Profit?.ToString(settings.NumberFormat) ?? "N/A"}");
            }
            _writer.WriteLine();
        }

        // Helper method similar to the one in ReportGenerator
        private PerformanceMetrics CalculatePerformanceMetrics(List<Trade> trades)
        {
            var metrics = new PerformanceMetrics();
            
            metrics.TotalTrades = trades.Count;
            metrics.WinningTrades = trades.Count(t => t.Profit > 0);
            metrics.LosingTrades = trades.Count(t => t.Profit <= 0);
            metrics.NetProfit = trades.Sum(t => t.Profit ?? 0);
            metrics.WinRate = metrics.TotalTrades == 0 ? 0 : (decimal)metrics.WinningTrades / metrics.TotalTrades * 100;
            
            // Calculate Sharpe Ratio (simplified)
            var profits = trades.Select(t => t.Profit ?? 0).ToList();
            if (profits.Any())
            {
                decimal avgReturn = profits.Average();
                decimal sumOfSquares = profits.Sum(p => (p - avgReturn) * (p - avgReturn));
                decimal stdDev = (decimal)Math.Sqrt((double)(sumOfSquares / profits.Count));
                metrics.SharpeRatio = stdDev == 0 ? 0 : avgReturn / stdDev;
            }
            
            return metrics;
        }   

        private PerformanceMetrics CalculateCappedPerformanceMetrics(List<Trade> trades, decimal marginPerTrade)
        {
            var metrics = new PerformanceMetrics();
            var validTrades = trades.Where(t => t.Profit.HasValue).ToList();
            
            // Apply capping
            var cappedProfits = validTrades.Select(t => 
                Math.Max(t.Profit.Value, -marginPerTrade)).ToList();
            
            metrics.TotalTrades = cappedProfits.Count;
            metrics.WinningTrades = cappedProfits.Count(p => p > 0);
            metrics.LosingTrades = cappedProfits.Count(p => p <= 0);
            metrics.NetProfit = cappedProfits.Sum();
            metrics.WinRate = metrics.TotalTrades == 0 ? 0 : 
                (decimal)metrics.WinningTrades / metrics.TotalTrades * 100;
            metrics.MaxLoss = cappedProfits.DefaultIfEmpty(0).Min();
            
            // Calculate Sharpe Ratio
            if (cappedProfits.Any())
            {
                decimal avgReturn = cappedProfits.Average();
                decimal sumOfSquares = cappedProfits.Sum(p => (p - avgReturn) * (p - avgReturn));
                metrics.SharpeRatio = (decimal)Math.Sqrt((double)(sumOfSquares / cappedProfits.Count)) == 0 ? 
                    0 : avgReturn / (decimal)Math.Sqrt((double)(sumOfSquares / cappedProfits.Count));
            }
            
            return metrics;
        }

        private PerformanceMetrics CalculatePositionPerformance(IEnumerable<Trade> trades)
        {
            var tradeList = trades.Where(t => t.Profit.HasValue).ToList();
            var metrics = new PerformanceMetrics();
            
            if (!tradeList.Any()) 
                return metrics;

            metrics.TotalTrades = tradeList.Count;
            metrics.WinningTrades = tradeList.Count(t => t.Profit > 0);
            metrics.LosingTrades = tradeList.Count(t => t.Profit <= 0);
            metrics.NetProfit = tradeList.Sum(t => t.Profit ?? 0);
            metrics.WinRate = metrics.TotalTrades == 0 ? 0 : (decimal)metrics.WinningTrades / metrics.TotalTrades * 100;
            metrics.AvgProfit = tradeList.Average(t => t.Profit ?? 0);
            metrics.BestTrade = tradeList.Max(t => t.Profit ?? 0);
            metrics.WorstTrade = tradeList.Min(t => t.Profit ?? 0);
            
            return metrics;
        }

        private void WriteTradingHoursAnalysis(string sessionId, ReportSettings settings)
        {
            // var trades = _tradeLogger.GetTrades(sessionId)
            //     .Where(t => t.ExitTime.HasValue)
            //     .ToList();
            
            var sessionOverview = _tradeLogger.GetSessionOverview(sessionId);
            // In both WriteTradingHoursAnalysis and WriteExitClusterAnalysis:
            var trades = _tradeLogger.GetTrades(sessionId)
                .Where(t => t.ExitTime.HasValue && 
                        t.ExitTime.Value != sessionOverview.EndTime) // Add this line
                .ToList();

            _writer.WriteLine("\n============= TRADING HOURS ANALYSIS =============");
            
            // 1. Market Session Performance (UTC)
            _writer.WriteLine("\n► Best/Worst Market Sessions:");
            var sessionResults = trades
                .GroupBy(t => GetMarketSession(t.EntryTime))
                .Select(g => new {
                    Session = g.Key,
                    PnL = g.Sum(t => t.Profit ?? 0),
                    ExitClusters = g.GroupBy(t => t.ExitTime!.Value.ToString("HH:mm"))
                                .OrderByDescending(x => x.Count())
                                .First()
                })
                .OrderByDescending(x => x.PnL);

            foreach (var session in sessionResults)
            {
                _writer.WriteLine($"{session.Session,-8} | " +
                                $"PnL: {session.PnL.ToString(settings.NumberFormat),-7} | " +
                                $"Peak Exit: {session.ExitClusters.Key} ({session.ExitClusters.Count()} trades)");
            }

            // 2. Liquidation Hotspots
            var liquidations = trades
                .Where(t => (t.ExitPrice!.Value - t.LiquidationPrice)/t.LiquidationPrice < 0.05m)
                .GroupBy(t => t.ExitTime!.Value.ToString("HH:mm"))
                .OrderByDescending(g => g.Count())
                .Take(3);
            
            _writer.WriteLine("\n⚠️ Liquidation Hotspots (UTC):");
            foreach (var liq in liquidations)
            {
                _writer.WriteLine($"{liq.Key}: {liq.Count()} liquidations");
            }
        }

        private void WriteExitClusterAnalysis(string sessionId, ReportSettings settings)
        {
            // var trades = _tradeLogger.GetTrades(sessionId)
            //     .Where(t => t.ExitTime.HasValue)
            //     .ToList();


            
            var sessionOverview = _tradeLogger.GetSessionOverview(sessionId);
            var trades = _tradeLogger.GetTrades(sessionId)
                .Where(t => t.ExitTime.HasValue && 
                        t.ExitTime.Value != sessionOverview.EndTime) // Add this line
                .ToList();                

            _writer.WriteLine("\n============= EXIT CLUSTER ANALYSIS =============");
            
            // 1. Volatility Events (5-minute windows)
            var exitClusters = trades
                .GroupBy(t => new {
                    Time = t.ExitTime!.Value.ToString("yyyy-MM-dd HH:mm"),
                    Type = t.Profit >= 0 ? "TakeProfit" : "StopLoss"
                })
                .Select(g => new {
                    g.Key.Time,
                    g.Key.Type,
                    Count = g.Count(),
                    AvgLeverage = g.Average(t => t.Leverage),
                    Symbols = string.Join(", ", g.Select(t => t.Symbol).Distinct().Take(3))
                })
                .OrderByDescending(x => x.Count)
                .Take(5);

            _writer.WriteLine("\n► Top 5 Exit Events:");
            foreach (var cluster in exitClusters)
            {
                _writer.WriteLine($"{cluster.Time} | " +
                                $"{cluster.Type,-11} | " +
                                $"{cluster.Count}x trades | " +
                                $"Avg Lev: {cluster.AvgLeverage:F1}x | " +
                                $"Symbols: {cluster.Symbols}");
            }

            // 2. BTC Correlation
            if (trades.Any(t => t.Symbol == "BTCUSDT"))
            {
                var btcExits = trades
                    .Where(t => t.Symbol == "BTCUSDT")
                    .GroupBy(t => t.ExitTime!.Value.ToString("HH:mm"))
                    .OrderByDescending(g => g.Count())
                    .First();
                
                _writer.WriteLine($"\n⏰ Most common BTC exit time: {btcExits.Key} UTC");
            }
        }       

        private string GetMarketSession(DateTime time)
        {
            int hour = time.Hour;
            if (hour >= 0 && hour < 5) return "Late NY";
            if (hour >= 5 && hour < 9) return "Asia";
            if (hour >= 9 && hour < 14) return "London";
            if (hour >= 14 && hour < 18) return "NY";
            return "Evening";
        }         

        private void ValidateTradeProfits(List<Trade> trades, decimal marginPerTrade)
        {
            var uncappedTrades = trades.Where(t => 
                t.Profit.HasValue && t.Profit.Value < -marginPerTrade).ToList();
            
            if (uncappedTrades.Any())
            {
                _writer.WriteLine("\n⚠️ WARNING: Found uncapped losses:");
                foreach (var trade in uncappedTrades.Take(3))
                {
                    _writer.WriteLine($"- {trade.Symbol} {trade.TradeType}: {trade.Profit} (Margin: {marginPerTrade})");
                }
                if (uncappedTrades.Count > 3)
                    _writer.WriteLine($"- Plus {uncappedTrades.Count - 3} more...");
            }
        }

        public (List<Trade> naturalExits, List<Trade> forcedExits) GetExitsByType(string sessionId, DateTime sessionEndTime)
        {
            var allTrades = _tradeLogger.GetTrades(sessionId).Where(t => t.ExitTime.HasValue).ToList();
            return (
                naturalExits: allTrades.Where(t => t.ExitTime != sessionEndTime).ToList(),
                forcedExits: allTrades.Where(t => t.ExitTime == sessionEndTime).ToList()
            );
        }
        
        private void WriteSessionSegmentation(string sessionId, ReportSettings settings)
        {
            var allTrades = _tradeLogger.GetTrades(sessionId);
            var (firstHalf, secondHalf) = _tradeLogger.GetSessionHalves(sessionId);
            var (candleCount, tradesPerCandle) = _tradeLogger.GetCandleMetrics(sessionId, settings.GetIntervalMinutes());

            // 1. Styled Header
            WriteStyledHeader("SESSION SEGMENTATION");
            
            // 2. Candle Metrics with symbols
            _writer.WriteLine($"\n◉ Candle Metrics ({settings.Interval}):");
            _writer.WriteLine($"◷ Total Candles: {candleCount}");
            _writer.WriteLine($"◷ Avg Trades/Candle: {tradesPerCandle.ToString(settings.NumberFormat)}");
            
            // 3. Active window detection
            var activeWindow = allTrades
                .GroupBy(t => t.EntryTime.ToString("HH:mm"))
                .OrderByDescending(g => g.Count())
                .First();
            _writer.WriteLine($"◷ Most Active Window: {activeWindow.Key} UTC ({activeWindow.Count()} trades)");

            // 4. Comparison table with box-drawing characters
            _writer.WriteLine("\n┌──────────────────┬────────────┬─────────────┬────────────┐");
            _writer.WriteLine("│     Metric       │ First Half │ Second Half │   Change   │");
            _writer.WriteLine("├──────────────────┼────────────┼─────────────┼────────────┤");
            
            // Trades row
            _writer.WriteLine($"│ {"Trades",-15} │ {firstHalf.Count,-10} │ {secondHalf.Count,-11} │ {FormatChangeStyled(firstHalf.Count, secondHalf.Count),-10} │");
            
            // Win Rate row
            _writer.WriteLine($"│ {"Win Rate",-15} │ {CalculateWinRate(firstHalf):F1}%    │ {CalculateWinRate(secondHalf):F1}%     │ {FormatChangeStyled(CalculateWinRate(firstHalf), CalculateWinRate(secondHalf), "pp"),-10} │");
            
            // Net PnL row
            _writer.WriteLine($"│ {"Net PnL",-15} │ {firstHalf.Sum(t => t.Profit ?? 0).ToString(settings.NumberFormat),-10} │ {secondHalf.Sum(t => t.Profit ?? 0).ToString(settings.NumberFormat),-11} │ {FormatChangeStyled(firstHalf.Sum(t => t.Profit ?? 0), secondHalf.Sum(t => t.Profit ?? 0)),-10} │");
            
            // Long/Short row
            var firstHalfLongs = firstHalf.Count(t => t.IsLong);
            var secondHalfLongs = secondHalf.Count(t => t.IsLong);
            _writer.WriteLine($"│ {"Long/Short",-15} │ {firstHalfLongs}/{firstHalf.Count - firstHalfLongs,-10} │ {secondHalfLongs}/{secondHalf.Count - secondHalfLongs,-11} │ {(secondHalfLongs > firstHalfLongs ? "+" : "")}{secondHalfLongs - firstHalfLongs} longs │");
            
            _writer.WriteLine("└──────────────────┴────────────┴─────────────┴────────────┘");

            // 5. Critical period warning
            var warning = GetCriticalPeriodWarning(allTrades);
            if (!string.IsNullOrEmpty(warning))
            {
                _writer.WriteLine($"\n{WARNING_ICON} {warning}");
            }
            
            // 6. Actionable suggestions
            WriteActionableSuggestions(allTrades, firstHalf, secondHalf, settings);
        }

        private void WriteActionableSuggestions(List<Trade> allTrades, List<Trade> firstHalf, List<Trade> secondHalf, ReportSettings settings)
        {
            var suggestions = new List<string>();
            
            // 1. Performance trend suggestion
            decimal pnlChange = secondHalf.Sum(t => t.Profit ?? 0) - firstHalf.Sum(t => t.Profit ?? 0);
            if (pnlChange > 0)
            {
                suggestions.Add("Strategy gained momentum in second half - consider letting winners run longer");
            }
            else
            {
                suggestions.Add("Performance declined in second half - review recent market conditions");
            }

            // 2. Liquidation warning
            var liquidationHour = allTrades
                .Where(t => t.ExitPrice.HasValue && (t.ExitPrice.Value - t.EntryPrice)/t.EntryPrice <= -0.05m)
                .GroupBy(t => t.ExitTime?.Hour)
                .OrderByDescending(g => g.Count())
                .FirstOrDefault()?.Key;
            
            if (liquidationHour.HasValue)
            {
                suggestions.Add($"Reduce exposure during {liquidationHour:00}:00-{liquidationHour:00}:59 UTC (liquidation hotspot)");
            }

            // 3. Long/Short ratio
            var longWinRate = CalculateWinRate(allTrades.Where(t => t.IsLong).ToList());
            var shortWinRate = CalculateWinRate(allTrades.Where(t => !t.IsLong).ToList());
            
            if (shortWinRate - longWinRate > 15) // 15% difference
            {
                suggestions.Add($"Shorts outperformed longs by {shortWinRate - longWinRate:F1}% - consider directional bias");
            }

            // Only show if we have suggestions
            if (suggestions.Any())
            {
                _writer.WriteLine("\n💡 Actionable Suggestions:");
                suggestions.ForEach(s => _writer.WriteLine($"- {s}"));
            }
        }
        
        private string FormatChange(decimal first, decimal second, string unit = "")
        {
            if (first == 0) return "N/A";
            decimal change = (second - first) / Math.Abs(first) * 100;
            return $"{(change >= 0 ? "+" : "")}{change:F1}%{unit}";
        }

        private string FormatChangePercent(decimal first, decimal second)
        {
            if (first == 0) return "N/A";
            decimal change = second - first;
            return $"{(change >= 0 ? "+" : "")}{change:F1}pp";
        }

        private string GetCriticalPeriodWarning(List<Trade> trades, int thresholdMinutes = 10)
        {
            var exitGroups = trades
                .Where(t => t.ExitTime.HasValue)
                .GroupBy(t => t.ExitTime.Value.ToString("HH:mm"))
                .OrderByDescending(g => g.Count())
                .Take(3);

            var liquidations = trades
                .Where(t => t.ExitPrice.HasValue && 
                        (t.ExitPrice.Value - t.EntryPrice) / t.EntryPrice <= -0.05m)
                .GroupBy(t => t.ExitTime.Value.ToString("HH:mm"))
                .OrderByDescending(g => g.Count())
                .FirstOrDefault();

            return liquidations != null && exitGroups.Any(g => g.Key == liquidations.Key)
                ? $"⚠️ Critical Period: {liquidations.Key} UTC\n" +
                $"- {liquidations.Count()} liquidations ({liquidations.Count() * 100f / trades.Count:F1}% of total)\n" +
                $"- Win rate: {CalculateWinRate(trades.Where(t => t.ExitTime?.ToString("HH:mm") == liquidations.Key).ToList()):F1}% " +
                $"(vs session avg {CalculateWinRate(trades):F1}%)"
                : string.Empty;
        }        

        private decimal CalculateWinRate(List<Trade> trades)
        {
            if (trades.Count == 0) return 0;
            return (decimal)trades.Count(t => t.Profit > 0) / trades.Count * 100;
        }

        private Trade? GetBestTrade(List<Trade> trades)
        {
            return trades.OrderByDescending(t => t.Profit ?? 0).FirstOrDefault();
        }

        private Trade? GetWorstTrade(List<Trade> trades)
        {
            return trades.OrderBy(t => t.Profit ?? 0).FirstOrDefault();
        }        

        private void WriteFooter()
        {
            _writer.WriteLine("\n==================================================");
            _writer.WriteLine("               END OF REPORT");
            _writer.WriteLine("==================================================");
        }
    }
}