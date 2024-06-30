public class OrderManager
{
    private readonly Wallet _wallet;
    private readonly Dictionary<string, Trade> _activeTrades = new Dictionary<string, Trade>();

    public OrderManager(Wallet wallet)
    {
        _wallet = wallet;
    }

    public void PlaceLongOrder(string symbol, decimal price)
    {
        PlaceOrder(symbol, price, true);
    }

    public void PlaceShortOrder(string symbol, decimal price)
    {
        PlaceOrder(symbol, price, false);
    }

    private void PlaceOrder(string symbol, decimal price, bool isLong)
    {
        decimal marginPerTrade = 20; // Margin per trade in USDT
        decimal quantity = marginPerTrade / price;

        if (_activeTrades.Count >= 9)
        {
            Console.WriteLine("Cannot place more than 9 trades at a time.");
            return;
        }

        if (_activeTrades.ContainsKey(symbol))
        {
            Console.WriteLine($"Coin {symbol} is already in an active trade. Will not add to it.");
            return;
        }

        var trade = new Trade(symbol, price, 1, quantity, isLong);

        if (_wallet.PlaceTrade(trade))
        {
            _activeTrades[symbol] = trade;
        }
    }

    public void CheckAndCloseTrades(Dictionary<string, decimal> currentPrices)
    {
        foreach (var trade in _activeTrades.Values.ToList())
        {
            if (trade.IsTakeProfitHit(currentPrices[trade.Symbol]) || trade.IsStoppedOut(currentPrices[trade.Symbol]))
            {
                var closingPrice = currentPrices[trade.Symbol];
                var realizedReturn = trade.CalculateRealizedReturn(closingPrice);

                _wallet.AddFunds(trade.Quantity * closingPrice);
                _activeTrades.Remove(trade.Symbol);

                Console.WriteLine($"Trade for {trade.Symbol} closed.");
                Console.WriteLine($"Realized Return for {trade.Symbol}: {realizedReturn:P2}"); // P2 formats the number as a percentage with 2 decimal places
            }
        }
    }

    public void PrintActiveTrades(Dictionary<string, decimal> currentPrices)
    {
        Console.WriteLine("Active Trades:");
        decimal totalValue = 0;

        foreach (var trade in _activeTrades.Values)
        {
            decimal currentValue = trade.Quantity * trade.CurrentValue(currentPrices[trade.Symbol]);
            totalValue += trade.CurrentValue(currentPrices[trade.Symbol]);//currentValue;
            Console.WriteLine($"\n{trade.Symbol}: Initial Margin: {trade.InitialMargin:F2}, \nEntry Price: {trade.EntryPrice:F8}, Current Price: {currentPrices[trade.Symbol]:F8}, \nTake Profit: {trade.TakeProfitPrice:F8}, Stop Loss: {trade.StopLossPrice:F8}, \nValue Of Trade: {trade.CurrentValue(currentPrices[trade.Symbol]):F2} USDT, Realized Return: {trade.CalculateRealizedReturn(currentPrices[trade.Symbol]):F2}%, \nDirection: {(trade.IsLong ? "Long" : "Short")}\n");
        }

        Console.WriteLine($"Total Value of Active Trades: {totalValue:F2} USDT");
    }

    public void PrintWalletBalance()
    {
        Console.WriteLine($"Wallet Balance: {_wallet.Balance:F2} USDT");
    }
}
