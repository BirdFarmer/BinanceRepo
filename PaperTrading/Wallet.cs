
public class Wallet
{
    public decimal Balance { get; private set; }

    public Wallet(decimal initialBalance)
    {
        Balance = initialBalance;
    }

    public bool CanPlaceTrade(Trade trade)
    {
        if (Balance >= trade.Quantity * trade.EntryPrice)
        {
            return true;
        }
        else
        {
            Console.WriteLine($"Insufficient balance to place trade for {trade.Symbol}.");
            return false;
        }
    }

    public bool PlaceTrade(Trade trade)
    {
        if (CanPlaceTrade(trade))
        {
            Balance -= trade.Quantity * trade.EntryPrice;
            Console.WriteLine($"Successfully placed trade for {trade.Symbol}.");
            return true;
        }
        else
        {
            Console.WriteLine($"Failed to place trade for {trade.Symbol} due to insufficient balance.");
            return false;
        }
    }

    public void AddFunds(decimal amount)
    {
        Balance += amount;
        Console.WriteLine($"Funds added: {amount}. New Balance: {Balance}");
    }
}
