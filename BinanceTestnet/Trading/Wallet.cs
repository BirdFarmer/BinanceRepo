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
                if (trade.TrailingEnabled)
                {
                    // Show trailing parameters instead of TP when trailing is enabled (paper/backtest)
                    Console.WriteLine($"At {trade.EntryTime} Successfully placed {direction} trade for {trade.Symbol}. Entry: {trade.EntryPrice}  Trailing: Act={trade.TrailingActivationPercent:F1}% Cb={trade.TrailingCallbackPercent:F1}%  SL: {trade.StopLoss}");
                }
                else
                {
                    Console.WriteLine($"At {trade.EntryTime} Successfully placed {direction} trade for {trade.Symbol}. Entry: {trade.EntryPrice}  TP: {trade.TakeProfit}  SL: {trade.StopLoss}");
                    if(trade.IsLong)
                        Console.WriteLine($"TP -> {trade.TakeProfit - trade.EntryPrice} - Entry - {trade.EntryPrice - trade.StopLoss} -> SL" );
                    else
                        Console.WriteLine($"SL -> {trade.StopLoss - trade.EntryPrice} - Entry - {trade.EntryPrice - trade.TakeProfit} -> TP" );
                }
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
            //Console.Beep();
            Balance += amount;
            Console.WriteLine($"Funds added: {amount:F2}. New Balance: {Balance:F2}");
        }

        public decimal GetBalance()
        {
            return Balance;
        }
    }
}
