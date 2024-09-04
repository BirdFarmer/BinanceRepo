using System;

namespace BinanceTestnet.Trading
{
    public class Wallet
    {
        public decimal Balance { get; set; }

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
                var direction = trade.IsLong ? "Long" : "Short";
                Console.WriteLine($"Successfully placed {direction} trade for {trade.Symbol}. Entry: {trade.EntryPrice}  TP: {trade.TakeProfitPrice}  SL: {trade.StopLossPrice}" );
                if(trade.IsLong)
                    Console.WriteLine($"TP -> {trade.TakeProfitPrice - trade.EntryPrice} - Entry - {trade.EntryPrice - trade.StopLossPrice} -> SL" );
                else
                    Console.WriteLine($"SL -> {trade.StopLossPrice - trade.EntryPrice} - Entry - {trade.EntryPrice - trade.TakeProfitPrice} -> TP" );
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
}
