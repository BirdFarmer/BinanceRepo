using Microsoft.Extensions.Configuration;
using TradingAPI.Models; // Ensure this matches the namespace of TradingSettings

namespace TradingAPI.Services
{
    public class SettingsService : ISettingsService
    {
        private readonly IConfiguration _configuration;

        public SettingsService(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public TradingSettings GetSettings()
        {
            // Fetch settings from configuration or other sources
            var settings = new TradingSettings
            {
                Interval = _configuration.GetValue<string>("Interval"),
                Leverage = _configuration.GetValue<decimal>("Leverage"),
                Strategies = _configuration.GetValue<string[]>("Strategies"),
                TakeProfit = _configuration.GetValue<decimal>("TakeProfit"),
                CoinPairs = _configuration.GetValue<string[]>("CoinPairs"),
                // Add additional fields if necessary
                OperationMode = _configuration.GetValue<string>("OperationMode"),
                TradeDirection = _configuration.GetValue<string>("TradeDirection"),
                Strategy = _configuration.GetValue<string>("TradingStrategy")
            };
            return settings;
        }
    }
}
