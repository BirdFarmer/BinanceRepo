using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Globalization;
using BinanceTestnet.Enums;

namespace TradingAppDesktop.Controls
{
    public partial class StrategySelectionControl : UserControl, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

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
            SetupSelectionTracking();
            UpdateSelection();
        }

        public void SetStrategyEnabled(SelectedTradingStrategy strategy, bool enabled, string? tooltipIfDisabled = null)
        {
            var item = _strategies.FirstOrDefault(x => x.Strategy.Equals(strategy));
            if (item == null) return;

            item.IsEnabled = enabled;
            if (!enabled)
            {
                // Deselect if currently selected
                if (item.IsSelected)
                {
                    item.IsSelected = false;
                }
                if (!string.IsNullOrWhiteSpace(tooltipIfDisabled))
                {
                    item.ToolTipText = tooltipIfDisabled;
                }
            }
            else
            {
                // Restore default tooltip
                item.ToolTipText = item.Description;
            }

            UpdateSelection();
        }

        // Set a short insight/tooltip for a strategy by enum
        public void SetStrategyInsight(SelectedTradingStrategy strategy, string tooltip)
        {
            var item = _strategies.FirstOrDefault(x => x.Strategy.Equals(strategy));
            if (item == null) return;
            item.ToolTipText = tooltip;
        }

        // Set a short insight/tooltip for a strategy by display name
        public void SetStrategyInsightByName(string name, string tooltip)
        {
            var item = _strategies.FirstOrDefault(x => string.Equals(x.Name, name, System.StringComparison.OrdinalIgnoreCase));
            if (item == null) return;
            item.ToolTipText = tooltip;
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

    // Converter to format the tooltip string into multi-line text (namespace scope so XAML can find it)
    public class TooltipFormatterConverter : IValueConverter
    {
        public object Convert(object value, System.Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null) return string.Empty;
            var s = value.ToString() ?? string.Empty;
            // Replace common separators with new lines for readability
            s = s.Replace("; ", "\n").Replace(";", "\n");
            s = s.Replace("|", "\n");
            return s;
        }

        public object ConvertBack(object value, System.Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }

    public class StrategyItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public SelectedTradingStrategy Strategy { get; }
        public string Name { get; }
        public string Description { get; }
        private string _toolTipText;
        public string ToolTipText
        {
            get => _toolTipText;
            set
            {
                if (_toolTipText != value)
                {
                    _toolTipText = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ToolTipText)));
                }
            }
        }
        private bool _isEnabled = true;
        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                if (_isEnabled != value)
                {
                    _isEnabled = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEnabled)));
                }
            }
        }
        
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
            _toolTipText = description;
        }
    }
}