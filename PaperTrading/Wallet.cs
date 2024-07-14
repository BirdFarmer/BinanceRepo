using System;

public class Wallet
{
    public decimal Balance { get; private set; }

    public Wallet(decimal initialBalance)
    {
        Balance = initialBalance;
    }

    public bool CanPlaceTrade(Trade trade)
    {
        decimal requiredBalance = (trade.Quantity * trade.EntryPrice) / trade.Leverage;
        Console.WriteLine($"Wallet: {Balance:F2}, Required: {requiredBalance:F1}, Quantity: {trade.Quantity:F2}, EntryPrice: {trade.EntryPrice}");

        return Balance >= requiredBalance;
    }

    public bool PlaceTrade(Trade trade)
    {
        if (CanPlaceTrade(trade))
        {
            Balance -= trade.Quantity * trade.EntryPrice / trade.Leverage;
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
        Console.Beep();
        Balance += amount;
        Console.WriteLine($"Funds added: {amount:F2}. New Balance: {Balance:F2}");
    }

    public decimal GetBalance()
    {
        return Balance;
    }
}
