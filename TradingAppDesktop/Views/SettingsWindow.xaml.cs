using System.Linq;
using System.Windows;
using System.Windows.Controls;
using TradingAppDesktop.Services;

namespace TradingAppDesktop.Views
{
    public partial class SettingsWindow : Window
    {
        public SettingsWindow()
        {
            InitializeComponent();

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
