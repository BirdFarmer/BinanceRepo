using System;
using Microsoft.Data.Sqlite;

namespace BinanceTestnet.Database
{
    public class UserRepository
    {
        private readonly DatabaseManager _dbManager;

        public UserRepository(DatabaseManager dbManager)
        {
            _dbManager = dbManager;
        }

        // Add methods for CRUD operations on the Users table
        public void AddUser(string username, string apiKey, string apiSecret)
        {
            _dbManager.CreateConnection(connection =>
            {
                string insertUser = "INSERT INTO Users (Username, ApiKey, ApiSecret) VALUES (@Username, @ApiKey, @ApiSecret)";
                using var command = new SqliteCommand(insertUser, connection);
                command.Parameters.AddWithValue("@Username", username);
                command.Parameters.AddWithValue("@ApiKey", apiKey);
                command.Parameters.AddWithValue("@ApiSecret", apiSecret);
                command.ExecuteNonQuery();
            });
        }

        // Additional methods for retrieving, updating, and deleting users...
    }
}