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
            reportTable.Rows.Add("Win Rate", $"{metrics.WinRate:F2}%".Replace(",", ".")); // Format win rate
            reportTable.Rows.Add("Net Profit", metrics.NetProfit.ToString("F8").Replace(",", ".")); // Format net profit
            reportTable.Rows.Add("Max Drawdown", metrics.MaximumDrawdown.ToString("F8").Replace(",", ".")); // Format max drawdown
            reportTable.Rows.Add("Profit Factor", metrics.ProfitFactor.ToString("F4").Replace(",", ".")); // Format profit factor
            reportTable.Rows.Add($"Sharpe Ratio,{metrics.SharpeRatio:F4}"); // Include Sharpe Ratio

            // Add rows for strategy performance
            foreach (var strategy in strategyPerformance)
            {
                reportTable.Rows.Add($"Strategy: {strategy.Key}", $"Net Profit: {strategy.Value.ToString("F8").Replace(",", ".")}");
            }

            // Add rows for coin performance
            foreach (var coin in coinPerformance)
            {
                reportTable.Rows.Add($"Coin: {coin.Key}", $"Net Profit: {coin.Value.ToString("F8").Replace(",", ".")}");
            }

            // Add rows for trade distribution
            reportTable.Rows.Add("Long Trades", tradeDistribution.LongTrades);
            reportTable.Rows.Add("Short Trades", tradeDistribution.ShortTrades);
            reportTable.Rows.Add("Average Trade Duration (minutes)", tradeDistribution.AverageDuration.ToString("F2").Replace(",", ".")); // Format average duration

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
    }
}