using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using BinanceTestnet.Enums;
using BinanceLive.Models;

public class OrderManager
{
    private readonly Wallet _wallet;
    private readonly Dictionary<int, Trade> _activeTrades = new Dictionary<int, Trade>();
    public decimal _leverage;
    public string _interval;
    private readonly ExcelWriter _excelWriter;
    private readonly OperationMode _operationMode;
    public decimal noOfTrades = 0;
    public decimal profitOfClosed = 0;
    public decimal longs = 0;
    public decimal shorts = 0;
    private int _nextTradeId = 1;
    private readonly decimal _takeProfit; 
    private readonly SelectedTradeDirection _tradeDirection;
    private SelectedTradingStrategy _tradingStrategy;
    
    public OrderManager(Wallet wallet, decimal leverage, ExcelWriter excelWriter, OperationMode operationMode, 
                        string interval, string fileName, decimal takeProfit, 
                        SelectedTradeDirection tradeDirection, SelectedTradingStrategy tradingStrategy)
    {
        _wallet = wallet;
        _leverage = leverage;
        _excelWriter = excelWriter;
        _operationMode = operationMode;
        _interval = interval;
        _takeProfit = takeProfit; 
        _excelWriter.Initialize(fileName);
        _tradeDirection = tradeDirection;
        _tradingStrategy = tradingStrategy;
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
        // Check if the trade direction matches the user's choice
        if ((_tradeDirection == SelectedTradeDirection.OnlyLongs && !isLong) ||
            (_tradeDirection == SelectedTradeDirection.OnlyShorts && isLong))
        {
            //Console.WriteLine($"Skipping trade for {symbol} because it does not match the trade direction preference.");
            return;
        }

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
            takeProfitPrice = price * (1 + (_takeProfit / 100)); // Use dynamic take profit
            stopLossPrice = price * (1 - (_takeProfit / 200)); // Stop loss is half of take profit
        }
        else
        {
            takeProfitPrice = price * (1 - (_takeProfit / 100)); // Use dynamic take profit
            stopLossPrice = price * (1 + (_takeProfit / 200)); // Stop loss is half of take profit
        }

        var trade = new Trade(_nextTradeId++, symbol, price, takeProfitPrice, stopLossPrice, quantity, isLong, _leverage, signal, _interval);

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

                Console.WriteLine($"Trade for {trade.Symbol} closed.");
                Console.WriteLine($"Realized Return for {trade.Symbol}: {trade.Profit:P2}");
                _wallet.AddFunds(profit);
                profitOfClosed += profit;
                _activeTrades.Remove(trade.Id);

                _excelWriter.WriteClosedTradeToExcel(trade);
                
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

    // Method to run backtesting with historical data
    public void RunBacktest(List<Kline> historicalData)
    {
        if (_operationMode != OperationMode.Backtest) return;

        var currentPrices = new Dictionary<string, decimal>();

        foreach (var kline in historicalData)
        {
            currentPrices["SYMBOL"] = kline.Close; // Replace "SYMBOL" with actual symbol if necessary

            // Here you would call your strategy methods to place orders based on historical data
            // For example: strategy.CheckForTradeOpportunities(currentPrices);

            CheckAndCloseTrades(currentPrices);
        }

        PrintTradeSummary();
        PrintWalletBalance();
    }
    
    public void CloseAllActiveTrades(decimal closePrice)
    {
        foreach (var trade in _activeTrades.Values.ToList())
        {
            if (!trade.IsClosed)
            {
                trade.CloseTrade(closePrice); // Mark trade as closed and calculate profit
                var profit = trade.IsLong
                    ? (trade.Quantity * (closePrice - trade.EntryPrice)) + trade.InitialMargin
                    : (trade.Quantity * (trade.EntryPrice - closePrice)) + trade.InitialMargin;

                Console.WriteLine($"Trade for {trade.Symbol} closed.");
                Console.WriteLine($"Realized Return for {trade.Symbol}: {trade.Profit:P2}");
                _wallet.AddFunds(profit);
                profitOfClosed += profit;
                _activeTrades.Remove(trade.Id);

                _excelWriter.WriteClosedTradeToExcel(trade);
            }
        }
    }
}
