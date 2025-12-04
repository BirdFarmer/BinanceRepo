using System;
using System.ComponentModel;

namespace TradingAppDesktop.Services
{
    public class SettingsService : ISettingsService, INotifyPropertyChanged
    {
        private UserSettings _settings;
        public UserSettings Settings
        {
            get => _settings;
            private set
            {
                _settings = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Settings)));
            }
        }

        public SettingsService()
        {
            Settings = UserSettings.Load();
        }

        public void Save()
        {
            Settings.Save();
        }

        public void Reload()
        {
            Settings = UserSettings.Load();
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
