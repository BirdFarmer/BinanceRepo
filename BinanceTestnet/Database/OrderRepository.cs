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

        // Add a new order, linked to a user and trade
        public void AddOrder(string tradeId, int userId, string symbol, decimal price, string orderType, bool active)
        {
            _dbManager.CreateConnection(connection =>
            {
                string insertOrder = "INSERT INTO Orders (TradeId, UserId, Symbol, Price, OrderType, Status) VALUES (@TradeId, @UserId, @Symbol, @Price, @OrderType, @Status)";
                using var command = new SqliteCommand(insertOrder, connection);
                command.Parameters.AddWithValue("@TradeId", tradeId);
                command.Parameters.AddWithValue("@UserId", userId);
                command.Parameters.AddWithValue("@Symbol", symbol);
                command.Parameters.AddWithValue("@Price", price);
                command.Parameters.AddWithValue("@OrderType", orderType);
                command.Parameters.AddWithValue("@Status", active ? "Active" : "Closed");
                command.ExecuteNonQuery();
            });
        }

        // Check for residual orders based on status
        public List<Order> GetResidualOrders(string tradeId)
        {
            List<Order> residualOrders = new List<Order>();

            _dbManager.CreateConnection(connection =>
            {
                string query = "SELECT * FROM Orders WHERE TradeId = @TradeId AND Status = 'Active'";
                using var command = new SqliteCommand(query, connection);
                command.Parameters.AddWithValue("@TradeId", tradeId);

                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    residualOrders.Add(new Order
                    {
                        OrderID = reader.GetInt32(0),
                        TradeID = reader.GetString(1),
                        Symbol = reader.GetString(2),
                        Price = reader.GetDecimal(3),
                        OrderType = reader.GetString(4),
                        Status = reader.GetString(5),
                        Timestamp = reader.GetDateTime(6)
                    });
                }
            });

            return residualOrders;
        }
    }
}
