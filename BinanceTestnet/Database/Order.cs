using System;

namespace BinanceTestnet.Database;

public class Order
{
        public int OrderID { get; set; }          // Unique identifier for each order
        public string TradeID { get; set; }       // ID of the associated trade
        public int UserID { get; set; }           // ID of the user placing the order
        public string Symbol { get; set; }        // Trading pair (e.g., BTCUSDT)
        public decimal Price { get; set; }        // Order price
        public decimal Quantity { get; set; }     // Quantity involved in the order
        public string OrderType { get; set; }     // Type (SL, TP, Market, etc.)
        public string Status { get; set; }        // Status of the order (Active, Closed, etc.)
        public DateTime Timestamp { get; set; }   // When the order was created
        public DateTime LastUpdated { get; set; } // Last status update time
}
