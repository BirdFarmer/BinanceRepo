using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TradingAppDesktop.Views
{
    public class PaperWalletViewModel : INotifyPropertyChanged
    {
        private decimal _startingBalance;
        private decimal _balance;
        private decimal _equity;
        private decimal _realizedPnl;
        private decimal _unrealizedPnl;
        private decimal _usedMargin;
        private decimal _free;
        private int _activeTrades;
        private string _sessionStartText = string.Empty;

        public decimal StartingBalance
        {
            get => _startingBalance;
            private set { _startingBalance = value; OnPropertyChanged(); }
        }

        public decimal Balance
        {
            get => _balance;
            set { _balance = value; OnPropertyChanged(); }
        }

        public decimal Equity
        {
            get => _equity;
            set { _equity = value; OnPropertyChanged(); }
        }

        public decimal RealizedPnl
        {
            get => _realizedPnl;
            set { _realizedPnl = value; OnPropertyChanged(); }
        }

        public decimal UnrealizedPnl
        {
            get => _unrealizedPnl;
            set { _unrealizedPnl = value; OnPropertyChanged(); }
        }

        public decimal UsedMargin
        {
            get => _usedMargin;
            set { _usedMargin = value; OnPropertyChanged(); }
        }

        public decimal Free
        {
            get => _free;
            set { _free = value; OnPropertyChanged(); }
        }

        public int ActiveTrades
        {
            get => _activeTrades;
            set { _activeTrades = value; OnPropertyChanged(); }
        }

        public string SessionStartText
        {
            get => _sessionStartText;
            private set { _sessionStartText = value; OnPropertyChanged(); }
        }

        public void Reset(decimal startingEquity, DateTime? sessionStartUtc = null)
        {
            StartingBalance = startingEquity;
            Balance = startingEquity;
            Equity = startingEquity;
            RealizedPnl = 0m;
            UnrealizedPnl = 0m;
            UsedMargin = 0m;
            Free = startingEquity;
            ActiveTrades = 0;

            if (sessionStartUtc.HasValue)
            {
                // Example: "started 01 Nov 14:35 UTC"
                SessionStartText = $"started {sessionStartUtc.Value:dd MMM HH:mm} UTC";
            }
            else
            {
                SessionStartText = string.Empty;
            }
        }

        public void UpdateSnapshot(decimal walletBalance, decimal usedMargin, decimal unrealizedPnl, int activeTrades)
        {
            Balance = walletBalance;
            UsedMargin = usedMargin;
            UnrealizedPnl = unrealizedPnl;
            ActiveTrades = activeTrades;

            // Realized PnL within session (delta of (free + used) vs start)
            // Since wallet balance in this sim is free funds (used margin already deducted),
            // realized = (free + used) - starting
            RealizedPnl = (walletBalance + usedMargin) - StartingBalance;

            // Equity = free + used + unrealized
            Equity = walletBalance + usedMargin + unrealizedPnl;

            // Free funds = wallet balance (since used margin is already deducted in wallet in this sim)
            Free = walletBalance;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
