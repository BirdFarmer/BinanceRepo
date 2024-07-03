using System;
using System.Collections.Generic;
using System.Linq;
using Serilog;

public class OrderManager
{
    private readonly Wallet _wallet;
    private readonly Dictionary<string, Trade> _activeTrades = new Dictionary<string, Trade>();
    private readonly decimal _leverage;
    public decimal noOfTrades = 0;
    public decimal profitOfClosed = 0;
    public decimal longs = 0;
    public decimal shorts = 0;
    string signal = "";

    public OrderManager(Wallet wallet, decimal leverage)
    {
        _wallet = wallet;
        _leverage = leverage;
    }

    public void PlaceLongOrder(string symbol, decimal price, string signalIn)
    {
        PlaceOrder(symbol, price, true, signalIn);
    }

    public void PlaceShortOrder(string symbol, decimal price, string signalIn)
    {
        PlaceOrder(symbol, price, false, signalIn);
    }

    private void PlaceOrder(string symbol, decimal price, bool isLong, string signalIn)
    {
        decimal marginPerTrade = 20; // Margin per trade in USDT
        decimal quantity = (marginPerTrade * _leverage) / price;

        if (_activeTrades.Count >= 25)
        {
            Log.Warning("Cannot place more than 25 trades at a time.");
            return;
        }

        if (_activeTrades.ContainsKey(symbol))
        {
            Log.Warning($"Coin {symbol} is already in an active trade. Will not add to it.");
            return;
        }

        decimal takeProfitPrice;
        decimal stopLossPrice;

        if (isLong)
        {
            takeProfitPrice = price * 1.005m; // 0.5% take profit above entry price
            stopLossPrice = price * 0.9975m; // 0.25% stop loss below entry price
        }
        else
        {
            takeProfitPrice = price * 0.9975m; // 0.25% take profit below entry price
            stopLossPrice = price * 1.005m; // 0.5% stop loss above entry price
        }

        var trade = new Trade(symbol, price, takeProfitPrice, stopLossPrice, quantity, isLong, _leverage);

        if (_wallet.PlaceTrade(trade))
        {
            if (trade.IsLong) longs++;
            else shorts++;
            noOfTrades++;
            LogTradePlacement(symbol, price, isLong, signalIn);
            _activeTrades[symbol] = trade;
        }
    }

    private void LogTradePlacement(string symbol, decimal price, bool isLong, string signalIn)
    {
        string direction = isLong ? "Long" : "Short";
        Log.Information("Placed {Direction} Order: Symbol: {Symbol}, Price: {Price}, Signal: {Signal}",
                        direction, symbol, price, signalIn);
    }

    public void CheckAndCloseTrades(Dictionary<string, decimal> currentPrices)
    {
        foreach (var trade in _activeTrades.Values.ToList())
        {
            if (currentPrices != null && trade != null &&
                currentPrices.ContainsKey(trade.Symbol) &&
                (trade.IsTakeProfitHit(currentPrices[trade.Symbol]) || trade.IsStoppedOut(currentPrices[trade.Symbol])))
            {
                var closingPrice = currentPrices[trade.Symbol];
                var realizedReturn = trade.CalculateRealizedReturn(closingPrice);
                Log.Information("Trade for {Symbol} closed at price {Price}. Realized return: {Return:P2}", 
                                trade.Symbol, closingPrice, realizedReturn);

                // Update wallet balance based on leveraged return
                if (trade.IsLong)
                {
                    _wallet.AddFunds((trade.Quantity * (closingPrice - trade.EntryPrice)) + trade.InitialMargin);
                }
                else
                {
                    _wallet.AddFunds((trade.Quantity * (trade.EntryPrice - closingPrice)) + trade.InitialMargin);
                }

                _activeTrades.Remove(trade.Symbol);
                Console.WriteLine($"Trade for {trade.Symbol} closed.");
                Console.WriteLine($"Realized Return for {trade.Symbol}: {realizedReturn:P2}");
            }
        }
    }

    public void PrintActiveTrades(Dictionary<string, decimal> currentPrices)
    {
        Console.WriteLine("Active Trades:");

        foreach (var trade in _activeTrades.Values)
        {
            decimal leverageMultiplier = trade.Leverage;
            decimal initialValue = trade.InitialMargin * leverageMultiplier;

            decimal currentValue = trade.Quantity * currentPrices[trade.Symbol];
            decimal entryValue = trade.Quantity * trade.EntryPrice;

            decimal realizedReturn = ((currentValue - initialValue) / initialValue) * 100;

            Console.WriteLine($"{trade.Symbol}: Initial Margin: {trade.InitialMargin:F2} USDT, Quantity: {trade.Quantity:F8},");
            Console.WriteLine($"Entry Price: {trade.EntryPrice:F8}, Current Price: {currentPrices[trade.Symbol]:F8},");
            Console.WriteLine($"Take Profit: {trade.TakeProfitPrice:F8}, Stop Loss: {trade.StopLossPrice:F8},");

            if (trade.IsLong)
            {
                decimal valueOfTrade = currentValue - entryValue;
                Console.WriteLine($"Value Of Trade: {valueOfTrade:F2} USDT, Realized Return: {realizedReturn:F2}%,");
            }
            else // SHORT trade
            {
                decimal valueOfTrade = entryValue - currentValue;
                // Switch it around for SHORT
                realizedReturn = ((initialValue - currentValue) / initialValue) * 100;
                Console.WriteLine($"Value Of Trade: {valueOfTrade:F2} USDT, Realized Return: {realizedReturn:F2}%,");
            }

            Console.WriteLine($"Direction: {(trade.IsLong ? "Long" : "Short")}, Leverage: {leverageMultiplier}x\n");
        }

        decimal totalValue = _activeTrades.Values.Sum(trade =>
        {
            decimal leverageMultiplier = trade.Leverage;
            decimal initialValue = trade.InitialMargin * leverageMultiplier;
            decimal currentValue = trade.Quantity * currentPrices[trade.Symbol];
            decimal entryValue = trade.Quantity * trade.EntryPrice;

            if (trade.IsLong)
            {
                return (currentValue - initialValue) - (entryValue - initialValue);
            }
            else // SHORT trade
            {
                return (entryValue - initialValue) - (currentValue - initialValue);
            }
        });

        // Adding the active trades initial
        decimal totalActiveValue = totalValue + (_activeTrades.Count * 20);
        Log.Information("Total Value of Active Trades: {TotalValue:F2} USDT, Profit of Closed Trades: {ProfitOfClosed}, Longs: {Longs}, Shorts: {Shorts}, Number of Trades: {NoOfTrades}",
                        totalActiveValue, profitOfClosed, longs, shorts, noOfTrades);

        Console.WriteLine($"Total Value of Active Trades: {totalActiveValue:F2} USDT");
    }

    public void PrintWalletBalance()
    {
        Console.WriteLine($"Wallet Balance: {_wallet.Balance:F2}");
        Log.Information("Wallet Balance: {Balance:F2}", _wallet.Balance);
    }
}
