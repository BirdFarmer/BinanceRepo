using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;

namespace BinanceTestnet.Database
{
    public class ReportGenerator
    {
        private readonly TradeLogger _tradeLogger;

        public ReportGenerator(TradeLogger tradeLogger)
        {
            _tradeLogger = tradeLogger;
        }

        /// <summary>
        /// Generates a summary report for a specific session, grouped by TakeProfitMultiplier and Interval.
        /// </summary>
        /// 
        public void GenerateSummaryReport(string sessionId, string outputPath, string coinPairsFormatted)
        {
            // Fetch all trades for the session
            var trades = _tradeLogger.GetTrades(sessionId);

            // Group trades by TakeProfitMultiplier and Interval
            var groupedTrades = trades
                .GroupBy(t => $"{t.Interval}_{t.TakeProfitMultiplier}")
                .ToList();

            // Create a directory for the reports
            if (!Directory.Exists(outputPath))
            {
                Directory.CreateDirectory(outputPath);
            }

            // Generate a report for each group
            foreach (var group in groupedTrades)
            {
                var interval = group.Key.Split('_')[0];
                var takeProfitMultiplier = group.Key.Split('_')[1];

                // Calculate metrics for this group
                var metrics = CalculatePerformanceMetrics(group.ToList());
                var sessionOverview = _tradeLogger.GetSessionOverview(sessionId);
                var (longestWinStreakStart, longestLossStreakStart, maxConsecutiveWins, maxConsecutiveLosses) = _tradeLogger.CalculateStreakTimes(sessionId);
                var (avgWinSize, avgLossSize, largestWin, largestLoss, _, _, avgTradeDuration, minTradeDuration, maxTradeDuration, winningTradesCount, losingTradesCount) = _tradeLogger.CalculateAdditionalMetrics(sessionId);

                // Calculate strategy performance for this group
                var strategyPerformance = CalculateStrategyPerformance(group.ToList());

                // Calculate coin performance for this group
                var coinPerformance = CalculateCoinPerformance(group.ToList());

                // Calculate trade distribution for this group
                var tradeDistribution = CalculateTradeDistribution(group.ToList());

                // Calculate initial margin and total funds added for this group
                decimal marginPerTrade = group.First().MarginPerTrade; // All trades have the same margin per trade
                
                decimal totalFundsAdded = _tradeLogger.GetSessionFundsAdded(sessionId);

                // Create a DataTable for the report
                var reportTable = new DataTable($"Summary Report - {interval} Interval - {takeProfitMultiplier} Take Profit Multiplier");

                // Add columns
                reportTable.Columns.Add("Metric", typeof(string));
                reportTable.Columns.Add("Value", typeof(string));

                // Add header for TakeProfitMultiplier and Interval
                reportTable.Rows.Add($"{interval} Interval - {takeProfitMultiplier} Take Profit Multiplier", "");
                reportTable.Rows.Add("", ""); // Blank row for separation

                // Add rows for session overview
                reportTable.Rows.Add("Session Overview", "");
                reportTable.Rows.Add("Session ID", sessionOverview.SessionId);
                reportTable.Rows.Add("Start Time", sessionOverview.StartTime.ToString("yyyy-MM-dd HH:mm:ss"));
                reportTable.Rows.Add("End Time", sessionOverview.EndTime.ToString("yyyy-MM-dd HH:mm:ss"));
                reportTable.Rows.Add("Total Duration", sessionOverview.TotalDuration);
                reportTable.Rows.Add("", ""); // Blank row for separation

                // Add rows for key metrics
                reportTable.Rows.Add("Performance Metrics", "");
                reportTable.Rows.Add("Win Rate", $"{metrics.WinRate:F2}%".Replace(",", "."));
                reportTable.Rows.Add("Net Profit", metrics.NetProfit.ToString("F2").Replace(",", "."));
                reportTable.Rows.Add("Max Drawdown", metrics.MaximumDrawdown.ToString("F2").Replace(",", "."));
                reportTable.Rows.Add("Profit Factor", metrics.ProfitFactor.ToString("F4").Replace(",", "."));
                reportTable.Rows.Add("Sharpe Ratio", $"{metrics.SharpeRatio:F4}".Replace(",", "."));
                reportTable.Rows.Add("", ""); // Blank row for separation

                // Add rows for streaks
                reportTable.Rows.Add("Streaks", "");
                reportTable.Rows.Add("Longest Win Streak Start", longestWinStreakStart?.ToString("yyyy-MM-dd HH:mm:ss") ?? "N/A");
                reportTable.Rows.Add("Longest Loss Streak Start", longestLossStreakStart?.ToString("yyyy-MM-dd HH:mm:ss") ?? "N/A");
                reportTable.Rows.Add("Max Consecutive Wins", maxConsecutiveWins);
                reportTable.Rows.Add("Max Consecutive Losses", maxConsecutiveLosses);
                reportTable.Rows.Add("", ""); // Blank row for separation

                // Add rows for additional metrics
                reportTable.Rows.Add("Additional Metrics", "");
                reportTable.Rows.Add("Average Win Size", avgWinSize.ToString("F2").Replace(",", "."));
                reportTable.Rows.Add("Average Loss Size", avgLossSize.ToString("F2").Replace(",", "."));
                reportTable.Rows.Add("Largest Win", largestWin.ToString("F2").Replace(",", "."));
                reportTable.Rows.Add("Largest Loss", largestLoss.ToString("F2").Replace(",", "."));
                reportTable.Rows.Add("Winning Trades", winningTradesCount);
                reportTable.Rows.Add("Losing Trades", losingTradesCount);
                reportTable.Rows.Add("Average Trade Duration (minutes)", avgTradeDuration.ToString("F2").Replace(",", "."));
                reportTable.Rows.Add("Min Trade Duration (minutes)", minTradeDuration.ToString("F2").Replace(",", "."));
                reportTable.Rows.Add("Max Trade Duration (minutes)", maxTradeDuration.ToString("F2").Replace(",", "."));
                reportTable.Rows.Add("Margin Per Trade", marginPerTrade.ToString("F2").Replace(",", "."));
                reportTable.Rows.Add("Total Funds Added", totalFundsAdded.ToString("F2").Replace(",", "."));
                reportTable.Rows.Add("", ""); // Blank row for separation

                // Add rows for strategy performance
                reportTable.Rows.Add("Strategy Performance", "");
                foreach (var strategy in strategyPerformance)
                {
                    reportTable.Rows.Add($"Strategy: {strategy.Key}", $"Net Profit: {strategy.Value.ToString("F8").Replace(",", ".")}");

                    // Calculate Sharpe ratio for each strategy
                    var strategyTrades = group.Where(t => t.Signal == strategy.Key).ToList();
                    decimal sharpeRatio = CalculateSharpeRatio(strategyTrades); // Pass the list of trades directly
                    reportTable.Rows.Add($"Sharpe Ratio ({strategy.Key})", $"{sharpeRatio:F4}".Replace(",", "."));
                }
                reportTable.Rows.Add("", ""); // Blank row for separation

                // Add rows for coin performance
                reportTable.Rows.Add("Coin Performance", "");
                foreach (var coin in coinPerformance)
                {
                    reportTable.Rows.Add($"Coin: {coin.Key}", $"Net Profit: {coin.Value.ToString("F8").Replace(",", ".")}");
                }
                reportTable.Rows.Add("", ""); // Blank row for separation

                // Add rows for trade distribution
                reportTable.Rows.Add("Trade Distribution", "");
                reportTable.Rows.Add("Long Trades", tradeDistribution.LongTrades);
                reportTable.Rows.Add("Short Trades", tradeDistribution.ShortTrades);
                reportTable.Rows.Add("Average Trade Duration (minutes)", tradeDistribution.AverageDuration.ToString("F2").Replace(",", "."));

                // Add the active coin pair list to the report
                reportTable.Rows.Add("Active Coin Pairs", coinPairsFormatted);

                // Export the report to CSV
                var groupOutputPath = Path.Combine(outputPath, $"{interval}_{takeProfitMultiplier}_report.csv");
                ExportToCsv(reportTable, groupOutputPath);
            }
        }

        /// <summary>
        /// Calculates performance metrics for a specific list of trades.
        /// </summary>
        private PerformanceMetrics CalculatePerformanceMetrics(List<Trade> trades)
        {
            var metrics = new PerformanceMetrics();

            // Total Trades
            metrics.TotalTrades = trades.Count;

            // Winning Trades
            metrics.WinningTrades = trades.Count(t => t.Profit > 0);

            // Losing Trades
            metrics.LosingTrades = trades.Count(t => t.Profit <= 0);

            // Net Profit
            metrics.NetProfit = trades.Sum(t => t.Profit ?? 0);

            // Win Rate
            metrics.WinRate = metrics.TotalTrades == 0 ? 0 : (decimal)metrics.WinningTrades / metrics.TotalTrades * 100;

            // Maximum Drawdown
            metrics.MaximumDrawdown = CalculateMaximumDrawdown(trades);

            // Profit Factor
            metrics.ProfitFactor = CalculateProfitFactor(trades);

            // Sharpe Ratio
            metrics.SharpeRatio = CalculateSharpeRatio(trades);

            return metrics;
        }

        private decimal CalculateMaximumDrawdown(List<Trade> trades)
        {
            decimal maxDrawdown = 0;
            decimal peak = decimal.MinValue;
            decimal trough = decimal.MaxValue;

            decimal cumulativeProfit = 0;

            foreach (var trade in trades.OrderBy(t => t.EntryTime))
            {
                cumulativeProfit += trade.Profit ?? 0;

                if (cumulativeProfit > peak)
                {
                    peak = cumulativeProfit;
                    trough = peak; // Reset trough when a new peak is found
                }
                else if (cumulativeProfit < trough)
                {
                    trough = cumulativeProfit;
                    decimal drawdown = peak - trough;
                    if (drawdown > maxDrawdown)
                    {
                        maxDrawdown = drawdown;
                    }
                }
            }

            return maxDrawdown;
        }

        private decimal CalculateProfitFactor(List<Trade> trades)
        {
            decimal totalProfit = trades.Where(t => t.Profit > 0).Sum(t => t.Profit ?? 0);
            decimal totalLoss = Math.Abs(trades.Where(t => t.Profit <= 0).Sum(t => t.Profit ?? 0));

            return totalLoss == 0 ? 1 : totalProfit / totalLoss; // Return 1 if totalLoss is 0
        }

        private decimal CalculateSharpeRatio(List<Trade> trades)
        {
            var profits = trades.Select(t => t.Profit ?? 0).ToList();

            if (profits.Count == 0) return 0;

            decimal averageReturn = profits.Average();
            decimal sumOfSquares = profits.Sum(p => (p - averageReturn) * (p - averageReturn));
            decimal standardDeviation = (decimal)Math.Sqrt((double)(sumOfSquares / profits.Count));

            if (standardDeviation == 0) return 0;

            return averageReturn / standardDeviation; // Risk-free rate assumed to be 0
        }

        /// <summary>
        /// Calculates strategy performance for a specific list of trades.
        /// </summary>
        private Dictionary<string, decimal> CalculateStrategyPerformance(List<Trade> trades)
        {
            var strategyPerformance = new Dictionary<string, decimal>();

            // Group trades by strategy (Signal)
            var groupedTrades = trades
                .GroupBy(t => t.Signal)
                .ToList();

            // Calculate net profit for each strategy
            foreach (var group in groupedTrades)
            {
                string strategy = group.Key;
                decimal netProfit = group.Sum(t => t.Profit ?? 0);
                strategyPerformance.Add(strategy, netProfit);
            }

            return strategyPerformance;
        }

        /// <summary>
        /// Calculates coin performance for a specific list of trades.
        /// </summary>
        private Dictionary<string, decimal> CalculateCoinPerformance(List<Trade> trades)
        {
            var coinPerformance = new Dictionary<string, decimal>();

            // Group trades by coin (Symbol)
            var groupedTrades = trades
                .GroupBy(t => t.Symbol)
                .ToList();

            // Calculate net profit for each coin
            foreach (var group in groupedTrades)
            {
                string coin = group.Key;
                decimal netProfit = group.Sum(t => t.Profit ?? 0);
                coinPerformance.Add(coin, netProfit);
            }

            return coinPerformance;
        }

        /// <summary>
        /// Calculates trade distribution for a specific list of trades.
        /// </summary>
        private TradeDistribution CalculateTradeDistribution(List<Trade> trades)
        {
            var tradeDistribution = new TradeDistribution
            {
                LongTrades = trades.Count(t => t.IsLong),
                ShortTrades = trades.Count(t => !t.IsLong),
                AverageDuration = trades.Any() ? trades.Average(t => t.Duration) : 0
            };

            return tradeDistribution;
        }        

        private void ExportToCsv(DataTable dataTable, string filePath)
        {
            // Define column widths
            const int metricWidth = 60; // Width for the Metric column
            const int valueWidth = 20;  // Width for the Value column

            using (var writer = new StreamWriter(filePath))
            {
                // Write headers
                writer.WriteLine($"{"Metric".PadRight(metricWidth)}{"Value".PadRight(valueWidth)}");

                // Write rows
                foreach (DataRow row in dataTable.Rows)
                {
                    string metric = row["Metric"].ToString();
                    string value = row["Value"].ToString();

                    // Pad the metric and value to align columns
                    writer.WriteLine($"{metric.PadRight(metricWidth)}{value.PadRight(valueWidth)}");
                }
            }

            Console.WriteLine($"Report exported to: {filePath}");
        }
    }
}