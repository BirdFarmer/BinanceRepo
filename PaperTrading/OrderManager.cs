using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;

public class OrderManager
{
    private readonly Wallet _wallet;
    private readonly Dictionary<int, Trade> _activeTrades = new Dictionary<int, Trade>();
    private readonly decimal _leverage;
    private readonly ExcelWriter _excelWriter;
    public decimal noOfTrades = 0;
    public decimal profitOfClosed = 0;
    public decimal longs = 0;
    public decimal shorts = 0;
    private int _nextTradeId = 1;

    public OrderManager(Wallet wallet, decimal leverage, ExcelWriter excelWriter)
    {
        _wallet = wallet;
        _leverage = leverage;
        _excelWriter = excelWriter;
    }

    private decimal CalculateQuantity(decimal price)
    {
        decimal marginPerTrade = 20; // Fixed margin per trade in USDT
        decimal quantity = (marginPerTrade * _leverage) / price;
        return quantity;
    }

    public void PlaceLongOrder(string symbol, decimal price, string signal)
    {
        PlaceOrder(symbol, price, true, signal);
    }

    public void PlaceShortOrder(string symbol, decimal price, string signal)
    {
        PlaceOrder(symbol, price, false, signal);
    }

    private void PlaceOrder(string symbol, decimal price, bool isLong, string signal)
    {
        decimal quantity = CalculateQuantity(price);

        if (_activeTrades.Count >= 25)
        {
            return;
        }

        if (_activeTrades.Values.Any(t => t.Symbol == symbol && t.IsInTrade))
        {
            return;
        }

        decimal takeProfitPrice;
        decimal stopLossPrice;

        if (isLong)
        {
            takeProfitPrice = price * 1.015m; // 1.5% take profit above entry price
            stopLossPrice = price * 0.9925m; // 0.75% stop loss below entry price
        }
        else
        {
            takeProfitPrice = price * 0.9925m; // 0.75% take profit below entry price
            stopLossPrice = price * 1.015m; // 1.5% stop loss above entry price
        }

        var trade = new Trade(_nextTradeId++, symbol, price, takeProfitPrice, stopLossPrice, quantity, isLong, _leverage, signal);

        if (_wallet.PlaceTrade(trade))
        {
            if (trade.IsLong) longs++;
            else shorts++;
            noOfTrades++;
            _activeTrades[trade.Id] = trade;
        }
    }

    public void CheckAndCloseTrades(Dictionary<string, decimal> currentPrices)
    {
        foreach (var trade in _activeTrades.Values.ToList())
        {
            if (currentPrices != null && trade != null &&
                currentPrices.ContainsKey(trade.Symbol) &&
                ShouldCloseTrade(trade, currentPrices[trade.Symbol]))
            {
                var closingPrice = currentPrices[trade.Symbol];
                trade.CloseTrade(closingPrice); // Mark trade as closed and calculate profit

                var profit = trade.IsLong
                    ? (trade.Quantity * (closingPrice - trade.EntryPrice)) + trade.InitialMargin
                    : (trade.Quantity * (trade.EntryPrice - closingPrice)) + trade.InitialMargin;

                _wallet.AddFunds(profit);
                profitOfClosed += profit;
                _activeTrades.Remove(trade.Id);

                Console.WriteLine($"Trade for {trade.Symbol} closed.");
                Console.WriteLine($"Realized Return for {trade.Symbol}: {trade.Profit:P2}");

                _excelWriter.WriteClosedTradeToExcel(trade); // Write the closed trade to the Excel file
            }
        }
    }

    private bool ShouldCloseTrade(Trade trade, decimal currentPrice)
    {
        return trade.IsLong ? 
            (currentPrice >= trade.TakeProfitPrice || currentPrice <= trade.StopLossPrice) :
            (currentPrice <= trade.TakeProfitPrice || currentPrice >= trade.StopLossPrice);
    }

    public void PrintActiveTrades(Dictionary<string, decimal> currentPrices)
    {
        Console.WriteLine("Active Trades:");
        string logOutput = "Active Trades:\n";

        foreach (var trade in _activeTrades.Values)
        {
            if (trade.Symbol != null && currentPrices.ContainsKey(trade.Symbol))
            {
                decimal leverageMultiplier = trade.Leverage;
                decimal initialValue = trade.InitialMargin * leverageMultiplier;
                decimal currentValue = trade.Quantity * currentPrices[trade.Symbol];
                decimal realizedReturn = ((currentValue - initialValue) / initialValue) * 100;

                Console.WriteLine($"{trade.Symbol}: Initial Margin: {trade.InitialMargin:F2} USDT, Current Value: {currentValue:F2} USDT, Entry Price: {trade.EntryPrice:F5}, Current Price: {currentPrices[trade.Symbol]:F5}, Take Profit: {trade.TakeProfitPrice:F5}, Stop Loss: {trade.StopLossPrice:F5}, Leverage: {trade.Leverage:F1}, Realized Return: {realizedReturn:F2}%");
                logOutput += $"{trade.Symbol}: Initial Margin: {trade.InitialMargin:F2} USDT, Current Value: {currentValue:F2} USDT, Entry Price: {trade.EntryPrice:F5}, Current Price: {currentPrices[trade.Symbol]:F5}, Take Profit: {trade.TakeProfitPrice:F5}, Stop Loss: {trade.StopLossPrice:F5}, Leverage: {trade.Leverage:F1}, Realized Return: {realizedReturn:F2}%\n";
            }
        }

        Log.Information(logOutput);
    }

    public void PrintTradeSummary()
    {
        Log.Information($"No. of Trades: {noOfTrades}");
        Log.Information($"Longs: {longs}");
        Log.Information($"Shorts: {shorts}");
    }

    public void PrintWalletBalance()
    {
        Console.WriteLine($"Wallet Balance: {_wallet.GetBalance():F2} USDT");
    }
}
