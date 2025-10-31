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