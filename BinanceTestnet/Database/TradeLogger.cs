using System;
using System.Collections.Generic;
using System.Globalization;
using BinanceTestnet.Trading;
using Microsoft.Data.Sqlite;

namespace BinanceTestnet.Database
{
    public class TradeLogger
    {
        private readonly string _connectionString;

        public TradeLogger(string databasePath)
        {
            _connectionString = $"Data Source={databasePath};";
        }

        public int LogOpenTrade(Trade trade, string sessionId)
        {
            try
            {
                using (var connection = new SqliteConnection(_connectionString))
                {
                    connection.Open();
                    string insertQuery = @"
                        INSERT INTO Trades (
                            SessionId, Symbol, TradeType, Signal, EntryTime, 
                            EntryPrice, TakeProfit, StopLoss, Leverage, Interval, KlineTimestamp)
                        VALUES (
                            @SessionId, @Symbol, @TradeType, @Signal, @EntryTime, 
                            @EntryPrice, @TakeProfit, @StopLoss, @Leverage, @Interval, @KlineTimestamp);";

                    using (var command = new SqliteCommand(insertQuery, connection))
                    {
                        // Add parameters (do not include TradeId)
                        command.Parameters.AddWithValue("@SessionId", sessionId);
                        command.Parameters.AddWithValue("@Symbol", trade.Symbol);
                        command.Parameters.AddWithValue("@TradeType", trade.TradeType);
                        command.Parameters.AddWithValue("@Signal", trade.Signal);
                        command.Parameters.AddWithValue("@EntryTime", trade.EntryTime);
                        command.Parameters.AddWithValue("@EntryPrice", trade.EntryPrice);
                        command.Parameters.AddWithValue("@TakeProfit", trade.TakeProfit);
                        command.Parameters.AddWithValue("@StopLoss", trade.StopLoss);
                        command.Parameters.AddWithValue("@Leverage", trade.Leverage);
                        command.Parameters.AddWithValue("@Interval", trade.Interval);
                        command.Parameters.AddWithValue("@KlineTimestamp", trade.KlineTimestamp);

                        int rowsAffected = command.ExecuteNonQuery();
                        Console.WriteLine($"Rows affected: {rowsAffected}"); // Debugging output

                        if (rowsAffected > 0)
                        {
                            // Retrieve the auto-generated TradeId
                            command.CommandText = "SELECT last_insert_rowid();";
                            int tradeId = Convert.ToInt32(command.ExecuteScalar());
                            return tradeId;
                        }
                        else
                        {
                            return -1; // Indicate failure
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error logging open trade: {ex.Message}"); // Debugging output
                return -1; // Indicate failure
            }
        }

        /// <summary>
        /// Logs the closure of an existing trade.
        /// </summary>
        public void LogCloseTrade(Trade trade, string sessionId)
        {
            try
            {
                using (var connection = new SqliteConnection(_connectionString))
                {
                    connection.Open();
                    string updateQuery = @"
                        UPDATE Trades
                        SET 
                            ExitTime = @ExitTime,
                            ExitPrice = @ExitPrice,
                            Profit = @Profit,
                            Duration = @Duration,
                            FundsAdded = @FundsAdded
                        WHERE TradeId = @TradeId;";

                    using (var command = new SqliteCommand(updateQuery, connection))
                    {
                        // Add parameters
                        command.Parameters.AddWithValue("@ExitTime", trade.ExitTime);
                        command.Parameters.AddWithValue("@ExitPrice", trade.ExitPrice ?? (object)DBNull.Value);
                        command.Parameters.AddWithValue("@Profit", trade.Profit ?? (object)DBNull.Value);
                        command.Parameters.AddWithValue("@Duration", trade.Duration);
                        command.Parameters.AddWithValue("@FundsAdded", trade.FundsAdded);
                        command.Parameters.AddWithValue("@TradeId", trade.TradeId);

                        int rowsAffected = command.ExecuteNonQuery();
                        Console.WriteLine($"Rows affected: {rowsAffected}"); // Debugging output
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error logging close trade: {ex.Message}"); // Debugging output
            }
        }

        /// <summary>
        /// Fetches all trades for a specific session.
        /// </summary>
        public List<Trade> GetTrades(string sessionId)
        {
            var trades = new List<Trade>();

            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();
                string selectQuery = @"
                    SELECT * FROM Trades 
                    WHERE SessionId = @SessionId 
                    ORDER BY EntryTime;";

                using (var command = new SqliteCommand(selectQuery, connection))
                {
                    command.Parameters.AddWithValue("@SessionId", sessionId);

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            // Use column names instead of indexes
                            int tradeId = reader.GetInt32(reader.GetOrdinal("TradeId"));
                            string symbol = reader.GetString(reader.GetOrdinal("Symbol"));
                            decimal entryPrice = reader.GetDecimal(reader.GetOrdinal("EntryPrice"));
                            decimal takeProfitPrice = reader.GetDecimal(reader.GetOrdinal("TakeProfit"));
                            decimal stopLossPrice = reader.GetDecimal(reader.GetOrdinal("StopLoss"));
                            decimal quantity = reader.GetDecimal(reader.GetOrdinal("FundsAdded")) / entryPrice;
                            bool isLong = reader.GetString(reader.GetOrdinal("TradeType")) == "Long";
                            decimal leverage = reader.GetDecimal(reader.GetOrdinal("Leverage"));
                            string signal = reader.GetString(reader.GetOrdinal("Signal"));
                            string interval = reader.IsDBNull(reader.GetOrdinal("Interval")) ? "N/A" : reader.GetString(reader.GetOrdinal("Interval"));
                            long timestamp = new DateTimeOffset(reader.GetDateTime(reader.GetOrdinal("EntryTime"))).ToUnixTimeMilliseconds();

                            // Create the Trade object
                            var trade = new Trade(
                                tradeId: tradeId,
                                sessionId: sessionId,
                                symbol: symbol,
                                entryPrice: entryPrice,
                                takeProfitPrice: takeProfitPrice,
                                stopLossPrice: stopLossPrice,
                                quantity: quantity,
                                isLong: isLong,
                                leverage: leverage,
                                signal: signal,
                                interval: interval,
                                timestamp: timestamp
                            );

                            // Set additional properties
                            trade.ExitTime = reader.IsDBNull(reader.GetOrdinal("ExitTime")) ? (DateTime?)null : reader.GetDateTime(reader.GetOrdinal("ExitTime"));
                            trade.ExitPrice = reader.IsDBNull(reader.GetOrdinal("ExitPrice")) ? (decimal?)null : reader.GetDecimal(reader.GetOrdinal("ExitPrice"));
                            trade.Profit = reader.IsDBNull(reader.GetOrdinal("Profit")) ? (decimal?)null : reader.GetDecimal(reader.GetOrdinal("Profit"));
                            trade.Duration = reader.GetInt32(reader.GetOrdinal("Duration"));

                            trades.Add(trade);
                        }
                    }
                }
            }

            return trades;
        }

        /// <summary>
        /// Calculates performance metrics for a specific session.
        /// </summary>
        public PerformanceMetrics CalculatePerformanceMetrics(string sessionId)
        {
            var metrics = new PerformanceMetrics();

            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();

                // Total Trades
                string totalTradesQuery = "SELECT COUNT(*) FROM Trades WHERE SessionId = @SessionId;";
                using (var command = new SqliteCommand(totalTradesQuery, connection))
                {
                    command.Parameters.AddWithValue("@SessionId", sessionId);
                    metrics.TotalTrades = Convert.ToInt32(command.ExecuteScalar());
                }

                // Winning Trades
                string winningTradesQuery = "SELECT COUNT(*) FROM Trades WHERE SessionId = @SessionId AND Profit > 0;";
                using (var command = new SqliteCommand(winningTradesQuery, connection))
                {
                    command.Parameters.AddWithValue("@SessionId", sessionId);
                    metrics.WinningTrades = Convert.ToInt32(command.ExecuteScalar());
                }

                // Losing Trades
                string losingTradesQuery = "SELECT COUNT(*) FROM Trades WHERE SessionId = @SessionId AND Profit <= 0;";
                using (var command = new SqliteCommand(losingTradesQuery, connection))
                {
                    command.Parameters.AddWithValue("@SessionId", sessionId);
                    metrics.LosingTrades = Convert.ToInt32(command.ExecuteScalar());
                }

                // Net Profit
                string netProfitQuery = "SELECT SUM(Profit) FROM Trades WHERE SessionId = @SessionId;";
                using (var command = new SqliteCommand(netProfitQuery, connection))
                {
                    command.Parameters.AddWithValue("@SessionId", sessionId);
                    var result = command.ExecuteScalar();
                    metrics.NetProfit = result == DBNull.Value ? 0 : Convert.ToDecimal(result);
                }

                // Win Rate
                metrics.WinRate = metrics.TotalTrades == 0 ? 0 : (decimal)metrics.WinningTrades / metrics.TotalTrades * 100;

                // Maximum Drawdown
                metrics.MaximumDrawdown = CalculateMaximumDrawdown(sessionId, connection);

                // Profit Factor
                metrics.ProfitFactor = CalculateProfitFactor(sessionId, connection);

                // Sharpe Ratio
                metrics.SharpeRatio = CalculateSharpeRatio(sessionId, connection);
            }

            return metrics;
        }

        private decimal CalculateSharpeRatio(string sessionId, SqliteConnection connection)
        {
            // Fetch all profits for the session
            string profitsQuery = "SELECT Profit FROM Trades WHERE SessionId = @SessionId;";
            var profits = new List<decimal>();

            using (var command = new SqliteCommand(profitsQuery, connection))
            {
                command.Parameters.AddWithValue("@SessionId", sessionId);

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        if (!reader.IsDBNull(0))
                        {
                            profits.Add(reader.GetDecimal(0));
                        }
                    }
                }
            }

            if (profits.Count == 0)
            {
                return 0; // No trades, Sharpe Ratio is 0
            }

            // Calculate average return
            decimal averageReturn = profits.Average();

            // Calculate standard deviation of returns
            decimal sumOfSquares = profits.Sum(p => (p - averageReturn) * (p - averageReturn));
            decimal standardDeviation = (decimal)Math.Sqrt((double)(sumOfSquares / profits.Count));

            // Avoid division by zero
            if (standardDeviation == 0)
            {
                return 0;
            }

            // Calculate Sharpe Ratio (assuming risk-free rate = 0)
            decimal sharpeRatio = averageReturn / standardDeviation;
            return sharpeRatio;
        }

        private decimal CalculateMaximumDrawdown(string sessionId, SqliteConnection connection)
        {
            decimal maxDrawdown = 0;
            decimal peak = decimal.MinValue;
            decimal trough = decimal.MaxValue;

            string cumulativeProfitQuery = @"
                SELECT SUM(Profit) OVER (ORDER BY EntryTime) AS CumulativeProfit
                FROM Trades
                WHERE SessionId = @SessionId
                ORDER BY EntryTime;";

            using (var command = new SqliteCommand(cumulativeProfitQuery, connection))
            {
                command.Parameters.AddWithValue("@SessionId", sessionId);

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        // Check if CumulativeProfit is NULL
                        if (reader.IsDBNull(0))
                        {
                            continue; // Skip this row
                        }

                        decimal cumulativeProfit = reader.GetDecimal(0);

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
                }
            }

            return maxDrawdown;
        }

        private decimal CalculateProfitFactor(string sessionId, SqliteConnection connection)
        {
            decimal totalProfit = 0;
            decimal totalLoss = 0;

            string profitQuery = "SELECT SUM(Profit) FROM Trades WHERE SessionId = @SessionId AND Profit > 0;";
            string lossQuery = "SELECT SUM(Profit) FROM Trades WHERE SessionId = @SessionId AND Profit <= 0;";

            using (var command = new SqliteCommand(profitQuery, connection))
            {
                command.Parameters.AddWithValue("@SessionId", sessionId);
                var result = command.ExecuteScalar();
                totalProfit = result == DBNull.Value ? 0 : Convert.ToDecimal(result);
            }

            using (var command = new SqliteCommand(lossQuery, connection))
            {
                command.Parameters.AddWithValue("@SessionId", sessionId);
                var result = command.ExecuteScalar();
                totalLoss = result == DBNull.Value ? 0 : Math.Abs(Convert.ToDecimal(result));
            }

            return totalLoss == 0 ? 1 : totalProfit / totalLoss; // Return 1 if totalLoss is 0
        }

        /// <summary>
        /// Fetches session overview (start time, end time, total duration).
        /// </summary>
        public SessionOverview GetSessionOverview(string sessionId)
        {
            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();

                string query = @"
                    SELECT MIN(EntryTime) AS StartTime, MAX(ExitTime) AS EndTime
                    FROM Trades
                    WHERE SessionId = @SessionId;";

                using (var command = new SqliteCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@SessionId", sessionId);

                    using (var reader = command.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            DateTime startTime = reader.IsDBNull(0) ? DateTime.MinValue : reader.GetDateTime(0);
                            DateTime endTime = reader.IsDBNull(1) ? DateTime.MinValue : reader.GetDateTime(1);

                            return new SessionOverview
                            {
                                SessionId = sessionId,
                                StartTime = startTime,
                                EndTime = endTime,
                                TotalDuration = (endTime - startTime).ToString(@"hh\:mm\:ss")
                            };
                        }
                    }
                }
            }

            return new SessionOverview { SessionId = sessionId };
        }

        /// <summary>
        /// Fetches strategy performance (net profit by strategy).
        /// </summary>
        public Dictionary<string, decimal> GetStrategyPerformance(string sessionId)
        {
            var strategyPerformance = new Dictionary<string, decimal>();

            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();

                string query = @"
                    SELECT Signal, COALESCE(SUM(Profit), 0) AS NetProfit
                    FROM Trades
                    WHERE SessionId = @SessionId
                    GROUP BY Signal;";

                using (var command = new SqliteCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@SessionId", sessionId);

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string signal = reader.GetString(0);
                            decimal netProfit = reader.IsDBNull(1) ? 0 : reader.GetDecimal(1);
                            strategyPerformance.Add(signal, netProfit);
                        }
                    }
                }
            }

            return strategyPerformance;
        }

        /// <summary>
        /// Fetches coin performance (net profit by coin pair).
        /// </summary>
        public Dictionary<string, decimal> GetCoinPerformance(string sessionId)
        {
            var coinPerformance = new Dictionary<string, decimal>();

            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();

                string query = @"
                    SELECT Symbol, SUM(Profit) AS NetProfit
                    FROM Trades
                    WHERE SessionId = @SessionId
                    GROUP BY Symbol;";

                using (var command = new SqliteCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@SessionId", sessionId);

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            // Check if Symbol or NetProfit is NULL
                            if (reader.IsDBNull(0) || reader.IsDBNull(1))
                            {
                                continue; // Skip this row
                            }

                            string symbol = reader.GetString(0);
                            decimal netProfit = reader.GetDecimal(1);

                            coinPerformance.Add(symbol, netProfit);
                        }
                    }
                }
            }

            return coinPerformance;
        }

        /// <summary>
        /// Fetches trade distribution (long vs. short trades, average duration).
        /// </summary>
        public TradeDistribution GetTradeDistribution(string sessionId)
        {
            var tradeDistribution = new TradeDistribution();

            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();

                string query = @"
                    SELECT 
                        COUNT(CASE WHEN TradeType = 'Long' THEN 1 END) AS LongTrades,
                        COUNT(CASE WHEN TradeType = 'Short' THEN 1 END) AS ShortTrades,
                        AVG(Duration) AS AverageDuration
                    FROM Trades
                    WHERE SessionId = @SessionId;";

                using (var command = new SqliteCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@SessionId", sessionId);

                    using (var reader = command.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            tradeDistribution.LongTrades = reader.GetInt32(0);
                            tradeDistribution.ShortTrades = reader.GetInt32(1);
                            tradeDistribution.AverageDuration = reader.IsDBNull(2) ? 0 : reader.GetDouble(2);
                        }
                    }
                }
            }

            return tradeDistribution;
        }
    }
}