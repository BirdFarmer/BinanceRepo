using System;
using Microsoft.Data.Sqlite;

namespace BinanceTestnet.Database
{
    public class DatabaseManager
    {
        private readonly string _connectionString;

        public DatabaseManager(string databasePath)
        {
            _connectionString = $"Data Source={databasePath};";
        }

        public void CreateConnection(Action<SqliteConnection> action)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            action(connection);
        }

        public void InitializeDatabase()
        {
            CreateConnection(connection =>
            {
                string createUsersTable = @"
                    CREATE TABLE IF NOT EXISTS Users (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Username TEXT UNIQUE,
                        ApiKey TEXT,
                        ApiSecret TEXT,
                        Balance REAL,
                        Leverage REAL,
                        CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
                        UpdatedAt DATETIME DEFAULT CURRENT_TIMESTAMP
                    );";
                
                string createOpenOrdersTable = @"
                    CREATE TABLE IF NOT EXISTS OpenOrders (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        TradeId TEXT UNIQUE,
                        UserId INTEGER,
                        Active BOOLEAN,
                        Symbol TEXT,
                        EntryPrice REAL,
                        EntryTime DATETIME,
                        StopLossId TEXT,
                        TakeProfitId TEXT,
                        StopLossPrice REAL,
                        TakeProfitPrice REAL,
                        Quantity REAL,
                        Leverage REAL,
                        ProfitLoss REAL,
                        LastUpdated DATETIME DEFAULT CURRENT_TIMESTAMP,
                        FOREIGN KEY (UserId) REFERENCES Users(Id)
                    );";

                using var command = new SqliteCommand(createUsersTable, connection);
                command.ExecuteNonQuery();

                command.CommandText = createOpenOrdersTable;
                command.ExecuteNonQuery();
            });
        }
    }
}