using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BinanceTestnet.Config
{
    public class UserSettingsDto
    {
        public string SelectedStrategy { get; set; } = "HarmonicPattern";

        public bool HarmonicEnableGartley { get; set; } = true;
        public bool HarmonicEnableButterfly { get; set; } = true;
        public bool HarmonicEnableBat { get; set; } = true;
        public bool HarmonicEnableCrab { get; set; } = true;
        public bool HarmonicEnableCypher { get; set; } = true;
        public bool HarmonicEnableShark { get; set; } = true;
        public int HarmonicValidationBars { get; set; } = 3;
        public bool HarmonicUseTrendFilter { get; set; } = false;
        // London session settings (read from TradingAppDesktop user.settings.json)
        public string LondonBreakCheck { get; set; } = "Close";
        public string LondonSessionStart { get; set; } = "08:00";
        public string LondonSessionEnd { get; set; } = "14:30";
        public decimal LondonScanDurationHours { get; set; } = 4.0m;
        public decimal LondonValueAreaPercent { get; set; } = 70m;
        public bool LondonUseOrderBookVap { get; set; } = true;
        public bool LondonAllowBothSides { get; set; } = false;
        public int LondonLimitExpiryMinutes { get; set; } = 60;
        // FRVP related settings
        public int LondonBuckets { get; set; } = 120;
        // Percent of session range used as sanity margin for POC (e.g., 2.0 means 2%)
        public decimal LondonPocSanityPercent { get; set; } = 2.0m;
        // Enable verbose debug logging for London Session strategy (true = verbose)
        public bool LondonEnableDebug { get; set; } = false;
        // Maximum allowed entries per side (LONG/SHORT) per session. Default=1 (single entry).
        public int LondonMaxEntriesPerSidePerSession { get; set; } = 1;
        // When true, use session POC as the explicit stop-loss for London entries. If false, global/main-window risk settings are used.
        public bool LondonUsePocAsStop { get; set; } = true;
        // When using POC as stop, multiply the stop->entry distance by this ratio to compute TP (TP = entry +/- (entry-POC)*LondonPocRiskRatio).
        public decimal LondonPocRiskRatio { get; set; } = 2.0m;
        // When false, do not allow entries triggered after sessionEnd + LondonScanDurationHours.
        // When true (default), existing watchers may still execute after the scan window.
        public bool LondonAllowEntriesAfterScanWindow { get; set; } = true;
    }

    public static class UserSettingsReader
    {
        private const string FileName = "user.settings.json";

        public static UserSettingsDto Load()
        {
            try
            {
                var path = FindSettingsFile();
                if (path == null) return new UserSettingsDto();
                var json = File.ReadAllText(path);
                var dto = JsonSerializer.Deserialize<UserSettingsDto>(json);
                return dto ?? new UserSettingsDto();
            }
            catch
            {
                return new UserSettingsDto();
            }
        }

        private static string? FindSettingsFile()
        {
            try
            {
                var dir = AppDomain.CurrentDomain.BaseDirectory;
                for (int i = 0; i < 6; i++)
                {
                    var candidate = Path.Combine(dir, FileName);
                    if (File.Exists(candidate)) return candidate;
                    var parent = Directory.GetParent(dir);
                    if (parent == null) break;
                    dir = parent.FullName;
                }
            }
            catch { }
            return null;
        }
    }
}
