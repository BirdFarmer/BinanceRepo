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
        /// Generates a summary report for a specific session.
        /// </summary>
        public void GenerateSummaryReport(string sessionId, string outputPath)
        {
            // Fetch data for the summary report
            var metrics = _tradeLogger.CalculatePerformanceMetrics(sessionId);
            var sessionOverview = _tradeLogger.GetSessionOverview(sessionId);
            var strategyPerformance = _tradeLogger.GetStrategyPerformance(sessionId);
            var coinPerformance = _tradeLogger.GetCoinPerformance(sessionId);
            var tradeDistribution = _tradeLogger.GetTradeDistribution(sessionId);

            // Calculate new metrics
            var (longestWinStreakStart, longestLossStreakStart, maxConsecutiveWins, maxConsecutiveLosses) = _tradeLogger.CalculateStreakTimes(sessionId);
            var (avgWinSize, avgLossSize, largestWin, largestLoss, _, _, avgTradeDuration, minTradeDuration, maxTradeDuration, winningTradesCount, losingTradesCount) = _tradeLogger.CalculateAdditionalMetrics(sessionId);

            // Create a DataTable for the report
            var reportTable = new DataTable("Summary Report");

            // Add columns
            reportTable.Columns.Add("Metric", typeof(string));
            reportTable.Columns.Add("Value", typeof(string));

            // Add rows for session overview
            reportTable.Rows.Add("Session ID", sessionOverview.SessionId);
            reportTable.Rows.Add("Start Time", sessionOverview.StartTime.ToString("yyyy-MM-dd HH:mm:ss"));
            reportTable.Rows.Add("End Time", sessionOverview.EndTime.ToString("yyyy-MM-dd HH:mm:ss"));
            reportTable.Rows.Add("Total Duration", sessionOverview.TotalDuration);

            // Add rows for key metrics
            reportTable.Rows.Add("Win Rate", $"{metrics.WinRate:F2}%".Replace(",", "."));
            reportTable.Rows.Add("Net Profit", metrics.NetProfit.ToString("F2").Replace(",", "."));
            reportTable.Rows.Add("Max Drawdown", metrics.MaximumDrawdown.ToString("F2").Replace(",", "."));
            reportTable.Rows.Add("Profit Factor", metrics.ProfitFactor.ToString("F4").Replace(",", "."));
            reportTable.Rows.Add("Sharpe Ratio", $"{metrics.SharpeRatio:F4}".Replace(",", "."));

            // Add rows for streaks
            reportTable.Rows.Add("Longest Win Streak Start", longestWinStreakStart?.ToString("yyyy-MM-dd HH:mm:ss") ?? "N/A");
            reportTable.Rows.Add("Longest Loss Streak Start", longestLossStreakStart?.ToString("yyyy-MM-dd HH:mm:ss") ?? "N/A");
            reportTable.Rows.Add("Max Consecutive Wins", maxConsecutiveWins);
            reportTable.Rows.Add("Max Consecutive Losses", maxConsecutiveLosses);

            // Add rows for strategy performance
            foreach (var strategy in strategyPerformance)
            {
                reportTable.Rows.Add($"Strategy: {strategy.Key}", $"Net Profit: {strategy.Value.ToString("F8").Replace(",", ".")}");

                // Calculate Sharpe ratio for each strategy
                var strategyTrades = _tradeLogger.GetTrades(sessionId).Where(t => t.Signal == strategy.Key).ToList();
                List<decimal?> strategyProfits = strategyTrades.Select(t => t.Profit).ToList();
                decimal sharpeRatio = CalculateSharpeRatio(strategyProfits); // Use the local method
                reportTable.Rows.Add($"Sharpe Ratio ({strategy.Key})", $"{sharpeRatio:F4}".Replace(",", "."));
            }

            // Add rows for additional metrics
            reportTable.Rows.Add("Average Win Size", avgWinSize.ToString("F2").Replace(",", "."));
            reportTable.Rows.Add("Average Loss Size", avgLossSize.ToString("F2").Replace(",", "."));
            reportTable.Rows.Add("Largest Win", largestWin.ToString("F2").Replace(",", "."));
            reportTable.Rows.Add("Largest Loss", largestLoss.ToString("F2").Replace(",", "."));
            reportTable.Rows.Add("Winning Trades", winningTradesCount);
            reportTable.Rows.Add("Losing Trades", losingTradesCount);
            reportTable.Rows.Add("Average Trade Duration (minutes)", avgTradeDuration.ToString("F2").Replace(",", "."));
            reportTable.Rows.Add("Min Trade Duration (minutes)", minTradeDuration.ToString("F2").Replace(",", "."));
            reportTable.Rows.Add("Max Trade Duration (minutes)", maxTradeDuration.ToString("F2").Replace(",", "."));

            // Export the report to CSV
            ExportToCsv(reportTable, outputPath);
        }
        private void ExportToCsv(DataTable dataTable, string filePath)
        {
            using (var writer = new StreamWriter(filePath))
            {
                // Write headers
                writer.WriteLine(string.Join(",", dataTable.Columns.Cast<DataColumn>().Select(col => col.ColumnName)));

                // Write rows
                foreach (DataRow row in dataTable.Rows)
                {
                    writer.WriteLine(string.Join(",", row.ItemArray));
                }
            }

            Console.WriteLine($"Report exported to: {filePath}");
        }

        /// <summary>
        /// Calculates the Sharpe ratio for a given set of profits.
        /// </summary>
        private decimal CalculateSharpeRatio(List<decimal?> profits)
        {
            // Filter out null values and treat them as 0
            var filteredProfits = profits.Select(p => p ?? 0).ToList();

            if (filteredProfits.Count == 0) return 0;

            decimal averageReturn = filteredProfits.Average();
            decimal sumOfSquares = filteredProfits.Sum(p => (p - averageReturn) * (p - averageReturn));
            decimal standardDeviation = (decimal)Math.Sqrt((double)(sumOfSquares / filteredProfits.Count));

            if (standardDeviation == 0) return 0;

            return averageReturn / standardDeviation; // Risk-free rate assumed to be 0
        }
    }
}