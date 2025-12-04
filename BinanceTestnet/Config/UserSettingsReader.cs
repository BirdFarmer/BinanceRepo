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
