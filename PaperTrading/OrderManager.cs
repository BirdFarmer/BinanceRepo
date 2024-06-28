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
        decimal quantity = _wallet.Balance * 0.10m / price;

        if (_activeTrades.Count >= 5)
        {
            Console.WriteLine("Cannot place more than 5 trades at a time.");
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
            foreach (var trade in _activeTrades.Values)
            {
                var currentPrice = currentPrices[trade.Symbol];
                var direction = trade.IsLong ? "LONG" : "SHORT";
                Console.WriteLine($"{trade.Symbol}: Entry Price: {trade.EntryPrice}, Current Price: {currentPrice}, Take Profit: {trade.TakeProfitPrice}, Stop Loss: {trade.StopLossPrice}, Direction: {direction}");
            }
        }
    public void PrintWalletBalance()
    {
        Console.WriteLine($"Wallet Balance: {_wallet.Balance}");
    }
}
