using System;
using System.IO;
using System.Text.Json;

namespace TradingAppDesktop.Services
{
    public class UserSettings
    {
        // Current UI theme name (matches theme file name without extension, e.g. "dark.purple")
        public string Theme { get; set; } = "dark.purple";

        public string ExitMode { get; set; } = "TakeProfit"; // TakeProfit | TrailingStop
        public decimal TpAtrMultiplier { get; set; } = 2.5m;
        public decimal TrailingActivationAtrMultiplier { get; set; } = 3.0m;
        public decimal TrailingCallbackPercent { get; set; } = 1.0m;
        public bool UseClosedCandles { get; set; } = false; // Persisted UI toggle

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
