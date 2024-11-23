using System;
using BinanceTestnet.Trading;
using RestSharp;
using System.Security.Cryptography;
using System.Text;
using BinanceTestnet.Models;

namespace BinanceTestnet.Services;

public class BinanceActivities
{
    private readonly RestClient _client;
    private string _apiKey;
    private string _secretKey;

    public BinanceActivities(RestClient client)
    {
        _client = client;
        _apiKey = Environment.GetEnvironmentVariable("BINANCE_API_KEY");
        _secretKey = Environment.GetEnvironmentVariable("BINANCE_API_SECRET");            
    }

    public async Task<bool> HandleOpenOrdersAndActiveTrades(List<string> symbols)
    {
        try{
            // Add your logic for handling open orders and active trades
            var activeTrades = await GetActiveTradesFromBinance();
            var openOrders = await GetOpenOrdersFromBinance();

            await CancelInactiveOpenOrders(activeTrades, openOrders);
                    
             return true; // Indicates success
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error handling orders and trades: {ex.Message}");
            return false; // Indicates failure
        }
    }

    public async Task CancelInactiveOpenOrders(List<PositionRisk> activeTrades, List<OpenOrder> openOrders)
    {
        var activeSymbols = activeTrades.Select(trade => trade.Symbol).ToHashSet(); // HashSet for faster lookup

        // Find open orders not associated with active trades
        var ordersToCancel = openOrders.Where(order => !activeSymbols.Contains(order.Symbol)).ToList();

        foreach (var order in ordersToCancel)
        {
            try
            {
                await CancelOrder(order.Symbol, order.OrderId);
                Console.WriteLine($"Cancelled order {order.OrderId} for {order.Symbol} as it's no longer in active trades.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to cancel order {order.OrderId} for {order.Symbol}: {ex.Message}");
            }
        }
    }

    public async Task CancelOrder(string symbol, long orderId)
    {
        try
        {
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var queryString = $"symbol={symbol}&orderId={orderId}&timestamp={timestamp}";
            var signature = GenerateSignature(queryString);
            
            var request = new RestRequest($"/fapi/v1/order?{queryString}&signature={signature}", Method.Delete);
            request.AddHeader("X-MBX-APIKEY", Environment.GetEnvironmentVariable("BINANCE_API_KEY"));
            
            var response = await _client.ExecuteAsync(request);

            if (response.IsSuccessful)
            {
                Console.WriteLine($"Successfully cancelled order {orderId} for {symbol}");
            }
            else
            {
                Console.WriteLine($"Failed to cancel order {orderId} for {symbol}: {response.ErrorMessage}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error cancelling order {orderId} for {symbol}: {ex.Message}");
        }
    }

    public async Task<List<PositionRisk>> GetActiveTradesFromBinance()
    {
        var positions = new List<PositionRisk>();

        try
        {
            string? apiKey = Environment.GetEnvironmentVariable("BINANCE_API_KEY");
            string? secretKey = Environment.GetEnvironmentVariable("BINANCE_API_SECRET");   

            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            
            // Step 1: Create query parameters
            var queryString = $"timestamp={timestamp}";
            
            // Step 2: Generate signature for the request (assuming you have a generateSignature function)
            var signature = GenerateSignature(queryString);
            
            // Step 3: Add signature to the query string
            queryString += $"&signature={signature}";

            // Step 4: Make the API call
            var request = new RestRequest($"/fapi/v2/positionRisk?{queryString}", Method.Get);
            request.AddHeader("Accept", "application/json");

            // Add API key to the request header
            request.AddHeader("X-MBX-APIKEY", apiKey);
            // Send the request
            var response = await _client.ExecuteAsync<List<PositionRisk>>(request);
            
            // Check if the response was successful
            if (response.IsSuccessful && response.Data != null)
            {
                // Filter out trades with zero position amount, as they aren't active
                positions = response.Data.Where(pos => pos.PositionAmt != 0).ToList();
            }
            else
            {
                Console.WriteLine($"Failed to fetch active trades: {response.ErrorMessage}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error retrieving active trades: {ex.Message}");
        }

        return positions;
    }
    
    public async Task<List<OpenOrder>> GetOpenOrdersFromBinance(string symbol = null)
    {
        var openOrders = new List<OpenOrder>();

        try
        {
            string? apiKey = Environment.GetEnvironmentVariable("BINANCE_API_KEY");
            string? secretKey = Environment.GetEnvironmentVariable("BINANCE_API_SECRET");

            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            
            // Step 1: Create query parameters
            var queryString = $"timestamp={timestamp}";
            if (!string.IsNullOrEmpty(symbol))
            {
                queryString += $"&symbol={symbol}";
            }
            
            // Step 2: Generate signature for the request
            var signature = GenerateSignature(queryString);
            
            // Step 3: Add signature to the query string
            queryString += $"&signature={signature}";

            // Step 4: Make the API call
            var request = new RestRequest($"/fapi/v1/openOrders?{queryString}", Method.Get);
            request.AddHeader("Accept", "application/json");

            // Add API key to the request header
            request.AddHeader("X-MBX-APIKEY", apiKey);
            
            // Send the request
            var response = await _client.ExecuteAsync<List<OpenOrder>>(request);

            // Check if the response was successful
            if (response.IsSuccessful && response.Data != null)
            {
                openOrders = response.Data;
            }
            else
            {
                Console.WriteLine($"Failed to fetch open orders: {response.ErrorMessage}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error retrieving open orders: {ex.Message}");
        }

        return openOrders;
    }

    private string GenerateSignature(string queryString)
    {
        string? apiSecret = Environment.GetEnvironmentVariable("BINANCE_API_SECRET");
        var keyBytes = Encoding.UTF8.GetBytes(apiSecret);
        var queryBytes = Encoding.UTF8.GetBytes(queryString);

        using (var hmac = new HMACSHA256(keyBytes))
        {
            var hash = hmac.ComputeHash(queryBytes);
            return BitConverter.ToString(hash).Replace("-", "").ToLower();
        }
    }

    // public async Task<Dictionary<string, decimal>> FetchCurrentPrices(List<string> symbols)
    // {
    //     // Fetch and return the current prices for the given symbols
    //     return await _client.FetchCurrentPricesAsync(symbols);
    // }
}
