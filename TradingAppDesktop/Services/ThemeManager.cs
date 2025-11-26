using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;

namespace TradingAppDesktop.Services
{
    public static class ThemeManager
    {
        private static readonly string ThemesFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Themes");

        // Returns available theme keys and relative resource URIs
        public static IEnumerable<(string Key, string Source)> GetAvailableThemes()
        {
            if (!Directory.Exists(ThemesFolder)) yield break;

            foreach (var f in Directory.GetFiles(ThemesFolder, "*.xaml"))
            {
                var fileName = Path.GetFileName(f);
                var key = Path.GetFileNameWithoutExtension(f);
                yield return (key, $"Themes/{fileName}");
            }
        }

        // Apply theme by key (file name without extension). Returns true on success.
        public static bool ApplyTheme(string themeKey)
        {
            try
            {
                var theme = GetAvailableThemes().FirstOrDefault(t => string.Equals(t.Key, themeKey, StringComparison.OrdinalIgnoreCase));
                if (string.IsNullOrEmpty(theme.Source)) return false;

                var rd = new ResourceDictionary { Source = new Uri(theme.Source, UriKind.Relative) };

                var app = Application.Current;
                if (app == null) return false;

                // Merge or replace the first merged dictionary (default theme slot)
                var merged = app.Resources.MergedDictionaries;
                if (merged.Count == 0)
                {
                    merged.Add(rd);
                }
                else
                {
                    // Replace the merged dictionary reference so future lookups use the new dictionary
                    merged[0] = rd;
                }

                // Update existing application-level resources in-place where possible.
                // This ensures controls that use StaticResource (resolved to the brush object) will update visually
                try
                {
                    foreach (var key in rd.Keys)
                    {
                        var newVal = rd[key];
                        if (app.Resources.Contains(key))
                        {
                            var existing = app.Resources[key];
                            // If both are SolidColorBrush, update color on the existing brush so StaticResource references update
                            if (existing is System.Windows.Media.SolidColorBrush existingBrush && newVal is System.Windows.Media.SolidColorBrush newBrush)
                            {
                                existingBrush.Color = newBrush.Color;
                            }
                            else
                            {
                                // Replace the resource entry with the new value
                                app.Resources[key] = newVal;
                            }
                        }
                        else
                        {
                            app.Resources.Add(key, newVal);
                        }
                    }
                }
                catch
                {
                    // non-fatal: fall back to merged dictionary replacement already done above
                }

                // Persist selection to user settings (if available)
                try
                {
                    var settings = UserSettings.Load();
                    settings.Theme = themeKey;
                    settings.Save();
                }
                catch { }

                return true;
            }
            catch
            {
                return false;
            }
        }

        public static string GetCurrentThemeKey()
        {
            try
            {
                var app = Application.Current;
                if (app == null) return string.Empty;
                if (app.Resources.MergedDictionaries.Count == 0) return string.Empty;
                var src = app.Resources.MergedDictionaries[0].Source?.OriginalString ?? string.Empty;
                if (string.IsNullOrEmpty(src)) return string.Empty;
                var file = Path.GetFileNameWithoutExtension(src);
                return file ?? string.Empty;
            }
            catch { return string.Empty; }
        }
    }
}
