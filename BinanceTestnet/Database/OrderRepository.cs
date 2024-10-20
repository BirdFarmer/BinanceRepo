using System;
using Microsoft.Data.Sqlite;

namespace BinanceTestnet.Database
{
    public class OrderRepository
    {
        private readonly DatabaseManager _dbManager;

        public OrderRepository(DatabaseManager dbManager)
        {
            _dbManager = dbManager;
        }

        // Add methods for CRUD operations on the OpenOrders table
        public void AddOpenOrder(string tradeId, int userId, bool active, string symbol, decimal entryPrice)
        {
            _dbManager.CreateConnection(connection =>
            {
                string insertOrder = "INSERT INTO OpenOrders (TradeId, UserId, Active, Symbol, EntryPrice) VALUES (@TradeId, @UserId, @Active, @Symbol, @EntryPrice)";
                using var command = new SqliteCommand(insertOrder, connection);
                command.Parameters.AddWithValue("@TradeId", tradeId);
                command.Parameters.AddWithValue("@UserId", userId);
                command.Parameters.AddWithValue("@Active", active);
                command.Parameters.AddWithValue("@Symbol", symbol);
                command.Parameters.AddWithValue("@EntryPrice", entryPrice);
                command.ExecuteNonQuery();
            });
        }

        // Additional methods for retrieving, updating, and deleting open orders...
    }
}
