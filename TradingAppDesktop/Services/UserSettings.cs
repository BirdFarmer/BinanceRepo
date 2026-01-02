using System;
using System.IO;
using System.Text.Json;

namespace TradingAppDesktop.Services
{
    public class UserSettings
    {
        // Current UI theme name (matches theme file name without extension, e.g. "dark.purple")
        public string Theme { get; set; } = "dark.purple";

        // Strategy UI: selected strategy key (e.g. "HarmonicPattern")
        public string SelectedStrategy { get; set; } = "HarmonicPattern";

        // London Session Volume Profile settings
        // Which candle field triggers a breakout check: Close | High | Low
        public string LondonBreakCheck { get; set; } = "Close";
        // Session start and end times (UTC) in HH:mm format
        public string LondonSessionStart { get; set; } = "08:00";
        public string LondonSessionEnd { get; set; } = "14:30";
        // How many hours after session end to scan for breaks (0.5 - 12)
        public decimal LondonScanDurationHours { get; set; } = 4.0m;
        // Value area percent used when computing VAH/VAL (50 - 90)
        public decimal LondonValueAreaPercent { get; set; } = 70m;
        // Number of buckets to use when computing FRVP from klines
        public int LondonBuckets { get; set; } = 120;
        // POC sanity margin percent relative to session range (if POC falls outside session range by more than this percent, clamp it)
        public decimal LondonPocSanityPercent { get; set; } = 2.0m;
        // Enable verbose debug logging for London Session strategy (true = verbose)
        public bool LondonEnableDebug { get; set; } = false;
        // Use order-book VAP when depth is available (works in Real and Paper if depth feed present)
        public bool LondonUseOrderBookVap { get; set; } = true;
        // Allow one trade per side per session (false = only one trade overall)
        public bool LondonAllowBothSides { get; set; } = false;
        // Limit order expiry in minutes (0 = no expiry)
        public int LondonLimitExpiryMinutes { get; set; } = 60;

        // Harmonic pattern toggles (default: enabled)
        public bool HarmonicEnableGartley { get; set; } = true;
        public bool HarmonicEnableButterfly { get; set; } = true;
        public bool HarmonicEnableBat { get; set; } = true;
        public bool HarmonicEnableCrab { get; set; } = true;
        public bool HarmonicEnableCypher { get; set; } = true;
        public bool HarmonicEnableShark { get; set; } = true;

        public string ExitMode { get; set; } = "TakeProfit"; // TakeProfit | TrailingStop
        public decimal TpAtrMultiplier { get; set; } = 2.5m;
        public decimal TrailingActivationAtrMultiplier { get; set; } = 3.0m;
        public decimal TrailingCallbackPercent { get; set; } = 1.0m;
        public bool UseClosedCandles { get; set; } = false; // Persisted UI toggle
        // Candle mode persisted as one of: "Forming", "Closed", "Aligned"
        // - "Forming": evaluate forming (last) candle
        // - "Closed": evaluate last closed candle (no alignment)
        // - "Aligned": evaluate last closed candle and align runner to timeframe boundaries
        public string CandleMode { get; set; } = "Forming";

        // Harmonic detector additional settings
        public int HarmonicValidationBars { get; set; } = 3;
        public bool HarmonicUseTrendFilter { get; set; } = false;

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
