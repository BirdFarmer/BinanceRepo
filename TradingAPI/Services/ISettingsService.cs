using TradingAPI.Models; // Ensure this matches the namespace of TradingSettings

namespace TradingAPI.Services
{
    public interface ISettingsService
    {
        TradingSettings GetSettings();
    }
}
