using System.Linq;
using System.Windows;
using System.Windows.Controls;
using TradingAppDesktop.Services;
using TradingAppDesktop.ViewModels;

namespace TradingAppDesktop.Views
{
    public partial class SettingsWindow : Window
    {
        private readonly SettingsService _settingsService;
        public SettingsWindow()
        {
            InitializeComponent();

            // Settings service + viewmodel
            var settingsService = new SettingsService();
            _settingsService = settingsService;
            var viewModel = new SettingsViewModel(settingsService);
            DataContext = viewModel;

            // Populate left navigation
            NavList.ItemsSource = viewModel.Sections;
            NavList.SelectedItem = viewModel.SelectedSection;
            UpdateSectionVisibility(viewModel.SelectedSection);
            NavList.SelectionChanged += (s, ev) =>
            {
                if (NavList.SelectedItem is string sec)
                {
                    viewModel.SelectedSection = sec;
                    UpdateSectionVisibility(sec);
                }
            };

            void UpdateSectionVisibility(string sec)
            {
                // Show only the selected section in the right content area
                if (sec == "Strategies")
                {
                    // Hide theme, show strategies full-width
                    ThemePanel.Visibility = Visibility.Collapsed;
                    StrategiesContent.Visibility = Visibility.Visible;
                    Grid.SetColumn(StrategiesContent, 0);
                    Grid.SetColumnSpan(StrategiesContent, 2);
                    PlaceholderText.Visibility = Visibility.Collapsed;
                }
                else if (sec == "General")
                {
                    // Show theme full-width, hide strategies
                    ThemePanel.Visibility = Visibility.Visible;
                    Grid.SetColumn(ThemePanel, 0);
                    Grid.SetColumnSpan(ThemePanel, 2);
                    StrategiesContent.Visibility = Visibility.Collapsed;
                    PlaceholderText.Visibility = Visibility.Collapsed;
                }
                else
                {
                    // Other sections: show placeholder
                    ThemePanel.Visibility = Visibility.Collapsed;
                    StrategiesContent.Visibility = Visibility.Collapsed;
                    PlaceholderText.Visibility = Visibility.Visible;
                    PlaceholderText.Text = $"{sec} settings coming soon. This is a placeholder for future settings sections.";
                }
            }

            // Populate available themes
            var themes = ThemeManager.GetAvailableThemes().Select(t => new { Key = t.Key, Source = t.Source }).ToList();
            ThemeList.ItemsSource = themes;
            ThemeList.DisplayMemberPath = "Key";

            // Select current theme
            var current = ThemeManager.GetCurrentThemeKey();
            if (!string.IsNullOrEmpty(current))
            {
                var sel = themes.FirstOrDefault(t => t.Key == current);
                if (sel != null) ThemeList.SelectedItem = sel;
            }

            ThemeList.SelectionChanged += ThemeList_SelectionChanged;

            // Load persisted user settings (if any) and wire strategy controls
            var settings = settingsService.Settings;

            // Strategy selector
            if (!string.IsNullOrEmpty(settings.SelectedStrategy))
            {
                foreach (var item in StrategySelector.Items)
                {
                    if (item is ComboBoxItem cbi && cbi.Content as string == settings.SelectedStrategy)
                    {
                        StrategySelector.SelectedItem = item; break;
                    }
                }
            }
            StrategySelector.SelectionChanged += StrategySelector_SelectionChanged;

            // Harmonic toggles
            ChkGartley.IsChecked = settings.HarmonicEnableGartley;
            ChkButterfly.IsChecked = settings.HarmonicEnableButterfly;
            ChkBat.IsChecked = settings.HarmonicEnableBat;
            ChkCrab.IsChecked = settings.HarmonicEnableCrab;
            ChkCypher.IsChecked = settings.HarmonicEnableCypher;
            ChkShark.IsChecked = settings.HarmonicEnableShark;

            // Validation bars and trend filter
            TxtValidationBars.Text = settings.HarmonicValidationBars.ToString();
            ChkUseTrendFilter.IsChecked = settings.HarmonicUseTrendFilter;

            TxtValidationBars.LostFocus += (s, ev) => {
                if (int.TryParse(TxtValidationBars.Text, out var v) && v >= 1 && v <= 30)
                {
                    settings.HarmonicValidationBars = v;
                    settingsService.Save();
                }
                else
                {
                    TxtValidationBars.Text = settings.HarmonicValidationBars.ToString();
                }
            };

            ChkUseTrendFilter.Checked += (s, ev) => { settings.HarmonicUseTrendFilter = true; settingsService.Save(); };
            ChkUseTrendFilter.Unchecked += (s, ev) => { settings.HarmonicUseTrendFilter = false; settingsService.Save(); };

            ChkGartley.Checked += HarmonicToggle_Changed; ChkGartley.Unchecked += HarmonicToggle_Changed;
            ChkButterfly.Checked += HarmonicToggle_Changed; ChkButterfly.Unchecked += HarmonicToggle_Changed;
            ChkBat.Checked += HarmonicToggle_Changed; ChkBat.Unchecked += HarmonicToggle_Changed;
            ChkCrab.Checked += HarmonicToggle_Changed; ChkCrab.Unchecked += HarmonicToggle_Changed;
            ChkCypher.Checked += HarmonicToggle_Changed; ChkCypher.Unchecked += HarmonicToggle_Changed;
            ChkShark.Checked += HarmonicToggle_Changed; ChkShark.Unchecked += HarmonicToggle_Changed;

            // Show harmonic panel if HarmonicPattern selected
            var selItem = StrategySelector.SelectedItem as ComboBoxItem;
            if (selItem != null && (selItem.Content as string) == "HarmonicPattern")
            {
                HarmonicPanel.Visibility = Visibility.Visible;
            }
        }

        private void StrategySelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var settings = _settingsService.Settings;
            var sel = StrategySelector.SelectedItem as ComboBoxItem;
            if (sel != null)
            {
                settings.SelectedStrategy = sel.Content as string ?? settings.SelectedStrategy;
                _settingsService.Save();
                HarmonicPanel.Visibility = (settings.SelectedStrategy == "HarmonicPattern") ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void HarmonicToggle_Changed(object sender, RoutedEventArgs e)
        {
            var settings = _settingsService.Settings;
            settings.HarmonicEnableGartley = ChkGartley.IsChecked ?? true;
            settings.HarmonicEnableButterfly = ChkButterfly.IsChecked ?? true;
            settings.HarmonicEnableBat = ChkBat.IsChecked ?? true;
            settings.HarmonicEnableCrab = ChkCrab.IsChecked ?? true;
            settings.HarmonicEnableCypher = ChkCypher.IsChecked ?? true;
            settings.HarmonicEnableShark = ChkShark.IsChecked ?? true;
            _settingsService.Save();
        }

        private void ThemeList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ThemeList.SelectedItem == null) return;
            var item = ThemeList.SelectedItem;
            var keyProp = item.GetType().GetProperty("Key");
            if (keyProp == null) return;
            var key = keyProp.GetValue(item) as string;
            if (string.IsNullOrEmpty(key)) return;

            ThemeManager.ApplyTheme(key);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
