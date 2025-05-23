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
                using var versionCommand = new SqliteCommand("PRAGMA user_version = 1;", connection);
                versionCommand.ExecuteNonQuery();
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
                            SessionId TEXT NOT NULL,
                            Symbol TEXT NOT NULL,
                            TradeType TEXT NOT NULL,
                            Signal TEXT NOT NULL,
                            EntryTime DATETIME NOT NULL,
                            ExitTime DATETIME,
                            EntryPrice REAL NOT NULL,
                            ExitPrice REAL,
                            Profit REAL,
                            Leverage INTEGER,
                            TakeProfit REAL,
                            StopLoss REAL,
                            Duration INTEGER,
                            FundsAdded REAL,
                            Interval TEXT,
                            KlineTimestamp DATETIME,
                            TakeProfitMultiplier DECIMAL,
                            MarginPerTrade DECIMAL,
                            IsNearLiquidation BOOLEAN DEFAULT 0  -- New column
                        );";

                // New table for storing coin pair lists
                string createCoinPairListsTable = @"
                    CREATE TABLE IF NOT EXISTS CoinPairLists (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        CoinPairs TEXT NOT NULL,            -- Comma-separated list of coin pairs
                        StartDateTime DATETIME NOT NULL,    -- When the list became active
                        EndDateTime DATETIME                 -- When the list was replaced (nullable)
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

                // Execute the query to create the CoinPairLists table
                command.CommandText = createCoinPairListsTable;
                command.ExecuteNonQuery();
            });
        }

        public void UpsertCoinPairData(string symbol, decimal currentPrice, decimal currentVolume)
        {
            decimal volumeInUSDT = currentPrice * currentVolume; // Calculate volume in USDT

            CreateConnection(connection =>
            {
                // Fetch the previous price, volume, and volume in USDT if they exist
                string selectQuery = "SELECT CurrentPrice, CurrentVolume, VolumeInUSDT FROM CoinPairData WHERE Symbol = @symbol;";
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
                        previousVolumeInUSDT = reader.GetDecimal(2); // Use the stored VolumeInUSDT
                    }
                }

                // Calculate differences and percent changes
                decimal priceDiff = currentPrice - previousPrice;
                decimal volumeInUSDTDiff = volumeInUSDT - previousVolumeInUSDT; // Use VolumeInUSDT for diff
                decimal pricePercentChange = previousPrice != 0 ? (priceDiff / previousPrice) * 100 : 0;
                decimal volumeInUSDTPercentChange = previousVolumeInUSDT != 0 ? (volumeInUSDTDiff / previousVolumeInUSDT) * 100 : 0;

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
                    upsertCommand.Parameters.AddWithValue("@volumeDiff", volumeInUSDTDiff); // Use VolumeInUSDTDiff
                    upsertCommand.Parameters.AddWithValue("@volumePercentChange", volumeInUSDTPercentChange); // Use VolumeInUSDTPercentChange
                    upsertCommand.Parameters.AddWithValue("@volumeInUSDT", volumeInUSDT);
                    upsertCommand.Parameters.AddWithValue("@previousVolumeInUSDT", previousVolumeInUSDT);
                    upsertCommand.ExecuteNonQuery();
                }
            });
        }

        
        public void UpsertCoinPairList(List<string> coinPairs, DateTime startDateTime)
        {
            CreateConnection(connection =>
            {
                // Format the coin pairs as a comma-separated string with each pair enclosed in quotes
                string formattedCoinPairs = string.Join(",", coinPairs.Select(cp => $"\"{cp}\""));

                // Step 1: Update the EndDateTime of the previous list
                string updatePreviousListQuery = @"
                    UPDATE CoinPairLists
                    SET EndDateTime = @StartDateTime
                    WHERE EndDateTime IS NULL;";

                using (var updateCommand = new SqliteCommand(updatePreviousListQuery, connection))
                {
                    updateCommand.Parameters.AddWithValue("@StartDateTime", startDateTime);
                    updateCommand.ExecuteNonQuery();
                }

                // Step 2: Insert the new list with a null EndDateTime
                string insertNewListQuery = @"
                    INSERT INTO CoinPairLists (CoinPairs, StartDateTime, EndDateTime)
                    VALUES (@CoinPairs, @StartDateTime, NULL);";

                using (var insertCommand = new SqliteCommand(insertNewListQuery, connection))
                {
                    insertCommand.Parameters.AddWithValue("@CoinPairs", formattedCoinPairs);
                    insertCommand.Parameters.AddWithValue("@StartDateTime", startDateTime);
                    insertCommand.ExecuteNonQuery();
                }
            });
        }

        public List<string> GetClosestCoinPairList(DateTime targetDateTime)
        {
            List<string> coinPairs = new List<string>();
            
            CreateConnection(connection =>
            {
                string query = @"
                    SELECT CoinPairs
                    FROM CoinPairLists
                    WHERE StartDateTime <= @TargetDateTime
                    AND (EndDateTime > @TargetDateTime OR EndDateTime IS NULL)
                    ORDER BY StartDateTime DESC
                    LIMIT 1;";

                using (var command = new SqliteCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@TargetDateTime", targetDateTime);
                    using (var reader = command.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            // Parse the comma-separated list of coin pairs
                            string coinPairsString = reader.GetString(0);
                            coinPairs = coinPairsString.Split(',')
                                                    .Select(cp => cp.Trim('"'))
                                                    .ToList();
                        }
                    }
                }
            });

            return coinPairs;
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
                    ORDER BY VolumeInUSDT DESC
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
            HashSet<string> uniqueSymbols = new HashSet<string>();

            // Proportions for each category
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

                // Get top coin pairs by composite score (VolumeInUSDT * ABS(PricePercentChange))
                string topCompositeScoreQuery = excludeLowestVolumeQuery + @"
                    SELECT Symbol
                    FROM CoinPairData
                    WHERE Symbol LIKE '%USDT'  -- Only include USDT pairs
                    AND Symbol NOT IN (SELECT Symbol FROM ExcludedSymbols)
                    ORDER BY (VolumeInUSDT * ABS(PricePercentChange)) DESC
                    LIMIT @limit;";

                // Add symbols from VolumeInUSDT
                int addedFromVolume = AddSymbolsToList(connection, topCoinPairs, uniqueSymbols, topVolumeQuery, volumeLimit);

                // Add symbols from PricePercentChange
                int addedFromPriceChange = AddSymbolsToList(connection, topCoinPairs, uniqueSymbols, topPriceChangeQuery, priceChangeLimit);

                // If the total is less than 80, fill the remaining slots with composite score
                if (topCoinPairs.Count < totalLimit)
                {
                    int remainingToAdd = totalLimit - topCoinPairs.Count;

                    // Use composite score as filler
                    AddSymbolsToList(connection, topCoinPairs, uniqueSymbols, topCompositeScoreQuery, remainingToAdd);
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

        // In DatabaseManager.cs
        public int GetSchemaVersion()
        {
            // Implement your version tracking logic
            return ExecuteScalar<int>("PRAGMA user_version;");
        }

        private T ExecuteScalar<T>(string query)
        {
            T result = default;
            CreateConnection(connection =>
            {
                using var command = new SqliteCommand(query, connection);
                var scalarResult = command.ExecuteScalar();
                if (scalarResult != null && scalarResult != DBNull.Value)
                {
                    result = (T)Convert.ChangeType(scalarResult, typeof(T));
                }
            });
            return result;
        }
    }
}
