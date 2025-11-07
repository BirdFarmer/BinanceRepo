using System;
using System.IO;
using System.Text.Json;

namespace TradingAppDesktop.Services
{
    public class UserSettings
    {
        public string ExitMode { get; set; } = "TakeProfit"; // TakeProfit | TrailingStop
        public decimal TpAtrMultiplier { get; set; } = 2.5m;
        public decimal TrailingActivationAtrMultiplier { get; set; } = 3.0m;
        public decimal TrailingCallbackPercent { get; set; } = 1.0m;

        private static readonly string SettingsFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "user.settings.json");

        public static UserSettings Load()
        {
            try
            {
                if (File.Exists(SettingsFile))
                {
                    string json = File.ReadAllText(SettingsFile);
                    var settings = JsonSerializer.Deserialize<UserSettings>(json);
                    if (settings != null)
                        return settings;
                }
            }
            catch { /* ignore and return defaults */ }

            return new UserSettings();
        }

        public void Save()
        {
            try
            {
                var json = JsonSerializer.Serialize(this, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                File.WriteAllText(SettingsFile, json);
            }
            catch
            {
                // non-fatal
            }
        }
    }
}
