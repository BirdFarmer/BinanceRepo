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
                // Existing table creation queries
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

                string createCoinPairDataTable = @"
                    CREATE TABLE IF NOT EXISTS CoinPairData (
                        Symbol TEXT PRIMARY KEY,
                        CurrentPrice REAL,
                        PreviousPrice REAL,
                        PriceDiff REAL,
                        PricePercentChange REAL,   -- Add this column for price percent change
                        CurrentVolume REAL,
                        PreviousVolume REAL,
                        VolumeDiff REAL,
                        VolumePercentChange REAL,  -- Add this column for volume percent change
                        VolumeInUSDT REAL, 
                        PreviousVolumeInUSDT REAL, 
                        LastUpdated DATETIME DEFAULT CURRENT_TIMESTAMP
                    );";

                string createTradesTable = @"
                    CREATE TABLE IF NOT EXISTS Trades (
                        TradeId INTEGER PRIMARY KEY AUTOINCREMENT,
                        SessionId TEXT NOT NULL,              -- Unique identifier for the backtest/live session
                        Symbol TEXT NOT NULL,                -- Coin pair (e.g., BTCUSDT)
                        TradeType TEXT NOT NULL,             -- 'Long' or 'Short'
                        Signal TEXT NOT NULL,                -- Strategy name (e.g., RSI, MACD)
                        EntryTime DATETIME NOT NULL,         -- Time the trade was opened
                        ExitTime DATETIME,                   -- Time the trade was closed
                        EntryPrice REAL NOT NULL,            -- Price at entry
                        ExitPrice REAL,                     -- Price at exit
                        Profit REAL,                         -- Profit or loss
                        Leverage INTEGER,                   -- Leverage used
                        TakeProfit REAL,                     -- Take profit level
                        StopLoss REAL,                      -- Stop loss level
                        Duration INTEGER,                    -- Duration of the trade in minutes
                        FundsAdded REAL,                    -- Funds added to the wallet
                        Interval TEXT,                        -- Interval (e.g., 1m, 5m, 1h)
                        KlineTimestamp DATETIME             -- Timestamp of the kline data (nullable)
                    );";

                using var command = new SqliteCommand(createUsersTable, connection);
                command.ExecuteNonQuery();

                command.CommandText = createOpenOrdersTable;
                command.ExecuteNonQuery();

                command.CommandText = createCoinPairDataTable;
                command.ExecuteNonQuery();

                // Execute the query to create the Trades table
                command.CommandText = createTradesTable;
                command.ExecuteNonQuery();
            });
        }

        public void UpsertCoinPairData(string symbol, decimal currentPrice, decimal currentVolume)
        {
            decimal volumeInUSDT = currentPrice * currentVolume; // Calculate volume in USDT

            CreateConnection(connection =>
            {
                // Fetch the previous price, volume, and volume in USDT if they exist
                string selectQuery = "SELECT CurrentPrice, CurrentVolume FROM CoinPairData WHERE Symbol = @symbol;";
                decimal previousPrice = 0;
                decimal previousVolume = 0;
                decimal previousVolumeInUSDT = 0;

                using (var selectCommand = new SqliteCommand(selectQuery, connection))
                {
                    selectCommand.Parameters.AddWithValue("@symbol", symbol);
                    using var reader = selectCommand.ExecuteReader();
                    if (reader.Read())
                    {
                        previousPrice = reader.GetDecimal(0);
                        previousVolume = reader.GetDecimal(1);
                        previousVolumeInUSDT = previousPrice * previousVolume; // Calculate previous volume in USDT
                    }
                }

                // Calculate differences and percent changes
                decimal priceDiff = currentPrice - previousPrice;
                decimal volumeDiff = currentVolume - previousVolume;
                decimal pricePercentChange = previousPrice != 0 ? (priceDiff / previousPrice) * 100 : 0;
                decimal volumePercentChange = previousVolume != 0 ? (volumeDiff / previousVolume) * 100 : 0;

                // Insert or update the row with volume in USDT and calculated differences
                string upsertQuery = @"
                    INSERT INTO CoinPairData (
                        Symbol, CurrentPrice, PreviousPrice, PriceDiff, PricePercentChange, 
                        CurrentVolume, PreviousVolume, VolumeDiff, VolumePercentChange, 
                        VolumeInUSDT, PreviousVolumeInUSDT, LastUpdated) 
                    VALUES (
                        @symbol, @currentPrice, @previousPrice, @priceDiff, @pricePercentChange, 
                        @currentVolume, @previousVolume, @volumeDiff, @volumePercentChange, 
                        @volumeInUSDT, @previousVolumeInUSDT, CURRENT_TIMESTAMP)
                    ON CONFLICT(Symbol) DO UPDATE SET 
                        CurrentPrice = @currentPrice,
                        PreviousPrice = CoinPairData.CurrentPrice,
                        PriceDiff = @priceDiff,
                        PricePercentChange = @pricePercentChange,
                        CurrentVolume = @currentVolume,
                        PreviousVolume = CoinPairData.CurrentVolume,
                        VolumeDiff = @volumeDiff,
                        VolumePercentChange = @volumePercentChange,
                        VolumeInUSDT = @volumeInUSDT,
                        PreviousVolumeInUSDT = CoinPairData.VolumeInUSDT,
                        LastUpdated = CURRENT_TIMESTAMP;";

                using (var upsertCommand = new SqliteCommand(upsertQuery, connection))
                {
                    upsertCommand.Parameters.AddWithValue("@symbol", symbol);
                    upsertCommand.Parameters.AddWithValue("@currentPrice", currentPrice);
                    upsertCommand.Parameters.AddWithValue("@previousPrice", previousPrice);
                    upsertCommand.Parameters.AddWithValue("@priceDiff", priceDiff);
                    upsertCommand.Parameters.AddWithValue("@pricePercentChange", pricePercentChange);
                    upsertCommand.Parameters.AddWithValue("@currentVolume", currentVolume);
                    upsertCommand.Parameters.AddWithValue("@previousVolume", previousVolume);
                    upsertCommand.Parameters.AddWithValue("@volumeDiff", volumeDiff);
                    upsertCommand.Parameters.AddWithValue("@volumePercentChange", volumePercentChange);
                    upsertCommand.Parameters.AddWithValue("@volumeInUSDT", volumeInUSDT);
                    upsertCommand.Parameters.AddWithValue("@previousVolumeInUSDT", previousVolumeInUSDT);
                    upsertCommand.ExecuteNonQuery();
                }
            });
        }

        public List<string> GetTopCoinPairsByVolume(int limit)
        {
            List<string> topCoinPairs = new List<string>();
            
            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();
                
                string query = @"
                    SELECT Symbol 
                    FROM CoinPairData
                    ORDER BY CurrentVolume DESC
                    LIMIT @limit;";
                
                using (var command = new SqliteCommand(query, connection))

                {
                    command.Parameters.AddWithValue("@limit", limit);
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            topCoinPairs.Add(reader.GetString(0));  // Assumes Symbol is the first column
                        }
                    }
                }
            }
            
            return topCoinPairs;
        }

        public List<string> GetTopCoinPairsByPriceChange(int limit)
        {
            List<string> topCoinPairs = new List<string>();
            
            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();
                
                string query = @"
                    SELECT Symbol 
                    FROM CoinPairData
                    ORDER BY ABS(PricePercentChange) DESC  -- Order by absolute price percent change
                    LIMIT @limit;";
                
                using (var command = new SqliteCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@limit", limit);
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            topCoinPairs.Add(reader.GetString(0));  // Assumes Symbol is the first column
                        }
                    }
                }
            }
            
            return topCoinPairs;
        }

        public List<string> GetTopCoinPairsByVolumeChange(int limit)
        {
            List<string> topCoinPairs = new List<string>();
            
            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();
                
                string query = @"
                    SELECT Symbol 
                    FROM CoinPairData
                    ORDER BY ABS(VolumePercentChange) DESC  -- Order by absolute volume percent change
                    LIMIT @limit;";
                
                using (var command = new SqliteCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@limit", limit);
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            topCoinPairs.Add(reader.GetString(0));  // Assumes Symbol is the first column
                        }
                    }
                }
            }
            
            return topCoinPairs;
        }

        public List<string> GetTopCoinPairs(int totalLimit = 80)
        {
            List<string> topCoinPairs = new List<string>();
            HashSet<string> uniqueSymbols = new HashSet<string>(); // To track unique symbols

            // Proportions for each category, prioritizing VolumeInUSDT and PricePercentChange
            int volumeLimit = 40; // Target 40 coins from VolumeInUSDT
            int priceChangeLimit = 40; // Target 40 coins from PricePercentChange

            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();

                // Exclude the lowest 150 volume coin pairs
                string excludeLowestVolumeQuery = @"
                    WITH ExcludedSymbols AS (
                        SELECT Symbol
                        FROM CoinPairData
                        WHERE Symbol LIKE '%USDT'  -- Only include USDT pairs
                        ORDER BY VolumeInUSDT ASC
                        LIMIT 150
                    )
                ";

                // Get top coin pairs by highest USDT volume (VolumeInUSDT)
                string topVolumeQuery = excludeLowestVolumeQuery + @"
                    SELECT Symbol
                    FROM CoinPairData
                    WHERE Symbol LIKE '%USDT'  -- Only include USDT pairs
                    AND Symbol NOT IN (SELECT Symbol FROM ExcludedSymbols)
                    ORDER BY VolumeInUSDT DESC
                    LIMIT @limit;";

                // Get top coin pairs by biggest price change percentage (PricePercentChange)
                string topPriceChangeQuery = excludeLowestVolumeQuery + @"
                    SELECT Symbol
                    FROM CoinPairData
                    WHERE Symbol LIKE '%USDT'  -- Only include USDT pairs
                    AND Symbol NOT IN (SELECT Symbol FROM ExcludedSymbols)
                    ORDER BY ABS(PricePercentChange) DESC
                    LIMIT @limit;";

                // Add symbols from VolumeInUSDT
                int addedFromVolume = AddSymbolsToList(connection, topCoinPairs, uniqueSymbols, topVolumeQuery, volumeLimit);

                // Add symbols from PricePercentChange
                int addedFromPriceChange = AddSymbolsToList(connection, topCoinPairs, uniqueSymbols, topPriceChangeQuery, priceChangeLimit);

                // Total unique symbols after prioritizing VolumeInUSDT and PricePercentChange
                //int totalAdded = addedFromVolume + addedFromPriceChange;

                // If the total is less than 80, fill the remaining slots with Volume Percent Change (filler)
                if (topCoinPairs.Count < totalLimit)
                {
                    int remainingToAdd = totalLimit - topCoinPairs.Count;

                    // Use Volume Percent Change as filler
                    string topVolumeChangeQuery = excludeLowestVolumeQuery + @"
                        SELECT Symbol
                        FROM CoinPairData
                        WHERE Symbol LIKE '%USDT'  -- Only include USDT pairs
                        AND Symbol NOT IN (SELECT Symbol FROM ExcludedSymbols)
                        ORDER BY ABS(VolumePercentChange) DESC
                        LIMIT @limit;";

                    AddSymbolsToList(connection, topCoinPairs, uniqueSymbols, topVolumeChangeQuery, remainingToAdd);
                }
            }

            return topCoinPairs;
        }


        private int AddSymbolsToList(SqliteConnection connection, List<string> topCoinPairs, HashSet<string> uniqueSymbols, string query, int limit)
        {
            int addedCount = 0;

            using (var command = new SqliteCommand(query, connection))
            {
                command.Parameters.AddWithValue("@limit", limit);
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string symbol = reader.GetString(0);
                        if (uniqueSymbols.Add(symbol)) // Add only if it's unique
                        {
                            topCoinPairs.Add(symbol);
                            addedCount++;
                        }
                    }
                }
            }

            return addedCount; // Return how many were added from this query
        }
    }
}
