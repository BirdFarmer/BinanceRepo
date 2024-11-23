using System;

namespace BinanceTestnet.Models;

public class OpenOrder
{
    public string Symbol { get; set; }           // Trading symbol (e.g., "BTCUSDT")
    public long OrderId { get; set; }            // Order ID
    public string ClientOrderId { get; set; }    // Client-defined order ID
    public decimal Price { get; set; }           // Price at which the order is set
    public decimal OrigQty { get; set; }         // Original quantity of the order
    public decimal ExecutedQty { get; set; }     // Quantity that has been executed so far
    public decimal CumQuote { get; set; }        // Cumulative quote quantity
    public string Status { get; set; }           // Order status (e.g., "NEW", "FILLED")
    public string TimeInForce { get; set; }      // Time in force for the order (e.g., "GTC")
    public string Type { get; set; }             // Order type (e.g., "LIMIT")
    public string Side { get; set; }             // Order side (e.g., "BUY" or "SELL")
    public long Time { get; set; }               // Order creation time
    public long UpdateTime { get; set; }         // Last update time for the order
}

