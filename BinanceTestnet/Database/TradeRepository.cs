using System;
using Microsoft.Data.Sqlite;

namespace BinanceTestnet.Database;

public class TradeRepository
{
    private readonly DatabaseManager _dbManager;

    public TradeRepository(DatabaseManager dbManager)
    {
        _dbManager = dbManager;
    }

    // Add a new trade, linked to a user with signal and direction
    public void AddTrade(int userId, string symbol, decimal entryPrice, string signal, string direction)
    {
        _dbManager.CreateConnection(connection =>
        {
            string insertTrade = "INSERT INTO Trades (UserId, Symbol, EntryPrice, Signal, Direction, Status) VALUES (@UserId, @Symbol, @EntryPrice, @Signal, @Direction, 'Open')";
            using var command = new SqliteCommand(insertTrade, connection);
            command.Parameters.AddWithValue("@UserId", userId);
            command.Parameters.AddWithValue("@Symbol", symbol);
            command.Parameters.AddWithValue("@EntryPrice", entryPrice);
            command.Parameters.AddWithValue("@Signal", signal);
            command.Parameters.AddWithValue("@Direction", direction);
            command.ExecuteNonQuery();
        });
    }

    // Update trade status
    public void UpdateTradeStatus(string tradeId, string status)
    {
        _dbManager.CreateConnection(connection =>
        {
            string updateTrade = "UPDATE Trades SET Status = @Status WHERE TradeId = @TradeId";
            using var command = new SqliteCommand(updateTrade, connection);
            command.Parameters.AddWithValue("@Status", status);
            command.Parameters.AddWithValue("@TradeId", tradeId);
            command.ExecuteNonQuery();
        });
    }
}
