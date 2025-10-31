using System;
using BinanceTestnet.Trading;

namespace TradingAppDesktop.Services
{
    public static class TradeEventSystem
    {
        public static event Action<Trade> TradeEntered;

        public static void NotifyTradeEntered(Trade trade)
        {
            TradeEntered?.Invoke(trade);
        }
    }
}