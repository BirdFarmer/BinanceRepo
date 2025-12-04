using System.Collections.ObjectModel;
using System.ComponentModel;
using TradingAppDesktop.Services;

namespace TradingAppDesktop.ViewModels
{
    public class SettingsViewModel : INotifyPropertyChanged
    {
        private readonly ISettingsService _settingsService;

        public ObservableCollection<string> Sections { get; } = new ObservableCollection<string>
        {
            "General",
            "Strategies",
            "Risk",
            "Integrations",
            "Notifications",
            "Advanced"
        };

        private string _selectedSection = "General";
        public string SelectedSection
        {
            get => _selectedSection;
            set
            {
                if (_selectedSection == value) return;
                _selectedSection = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedSection)));
            }
        }

        public UserSettings Settings => _settingsService.Settings;

        public SettingsViewModel(ISettingsService settingsService)
        {
            _settingsService = settingsService;
        }

        public void Save() => _settingsService.Save();

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
