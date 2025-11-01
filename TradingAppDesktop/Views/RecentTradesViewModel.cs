using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using TradingAppDesktop.Models;

namespace TradingAppDesktop.Views
{
    public class RecentTradesViewModel : INotifyPropertyChanged
    {
        private readonly ObservableCollection<TradeEntry> _recentTrades;

        public RecentTradesViewModel()
        {
            _recentTrades = new ObservableCollection<TradeEntry>();
            RecentTrades = new ReadOnlyObservableCollection<TradeEntry>(_recentTrades);
        }

        public ReadOnlyObservableCollection<TradeEntry> RecentTrades { get; }

        public void AddTradeEntry(TradeEntry trade)
        {
            // UI thread invocation
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                _recentTrades.Insert(0, trade);
                // Cap list to last 50 entries to avoid UI bloat
                while (_recentTrades.Count > 50)
                {
                    _recentTrades.RemoveAt(_recentTrades.Count - 1);
                }
            });
        }

        public void Clear()
        {
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                _recentTrades.Clear();
            });
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

}