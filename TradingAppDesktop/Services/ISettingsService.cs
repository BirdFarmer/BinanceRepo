using TradingAppDesktop.Services;

namespace TradingAppDesktop.Services
{
    public interface ISettingsService
    {
        UserSettings Settings { get; }
        void Save();
        void Reload();
    }
}
