using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace TradingAppDesktop.Controls
{
    public partial class StrategySelectionControl : UserControl, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        
        private ObservableCollection<StrategyItem> _strategies = new();
        private int _selectedCount;
        
        public StrategySelectionControl()
        {
            InitializeComponent();
            StrategiesContainer.ItemsSource = _strategies;
            this.Loaded += (s, e) => UpdateCount();
        }

        private void SetupSelectionTracking()
        {
            foreach (var item in _strategies)
            {
                item.PropertyChanged += (s, e) => 
                {
                    if (e.PropertyName == nameof(StrategyItem.IsSelected))
                    {
                        UpdateSelection();
                    }
                };
            }
        }

        public ObservableCollection<SelectedTradingStrategy> SelectedStrategies { get; } = new();

        public int SelectedCount
        {
            get => _selectedCount;
            private set
            {
                if (_selectedCount != value)
                {
                    _selectedCount = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedCount)));
                    UpdateCount();
                }
            }
        }

        public void SetAvailableStrategies(IEnumerable<StrategyItem> strategies)
        {
            _strategies.Clear();
            foreach (var s in strategies)
            {
                var item = new StrategyItem(s.Strategy, s.Name, s.Description);
                _strategies.Add(item);
            }
            SetupSelectionTracking(); // Add this line
            UpdateSelection(); // Initialize count
        }

        private void StrategyItemChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(StrategyItem.IsSelected))
            {
                UpdateSelection();
            }
        }

        private void UpdateSelection()
        {
            var selected = _strategies
                .Where(x => x.IsSelected)
                .Take(5)
                .Select(x => x.Strategy)
                .ToList();

            SelectedStrategies.Clear();
            foreach (var strategy in selected)
            {
                SelectedStrategies.Add(strategy);
            }

            SelectedCount = selected.Count;
        }

        private void UpdateCount()
        {
            SelectionCountText.Text = $"{SelectedCount}/5 strategies selected";
        }
    }

    public class StrategyItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        public SelectedTradingStrategy Strategy { get; }
        public string Name { get; }
        public string Description { get; }
        
        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
                }
            }
        }

        public StrategyItem(SelectedTradingStrategy strategy, string name, string description)
        {
            Strategy = strategy;
            Name = name;
            Description = description;
        }
    }
}