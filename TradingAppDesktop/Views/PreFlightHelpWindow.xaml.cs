using System;
using System.IO;
using System.Windows;

namespace TradingAppDesktop.Views
{
    public partial class PreFlightHelpWindow : Window
    {
        public PreFlightHelpWindow()
        {
            InitializeComponent();
            LoadHelpFile();
        }

        private void LoadHelpFile()
        {
            try
            {
                // Try to locate a user guide (preferred) or the developer doc by walking up from the app base directory
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var dir = new DirectoryInfo(baseDir);
                string? helpPath = null;
                var candidates = new[] { "PreFlight-User-Guide.md", "PreFlight-Market-Check.md" };
                for (int i = 0; i < 8 && dir != null; i++)
                {
                    foreach (var name in candidates)
                    {
                        var candidate = Path.Combine(dir.FullName, "docs", name);
                        if (File.Exists(candidate))
                        {
                            helpPath = candidate;
                            break;
                        }
                    }
                    if (helpPath != null) break;
                    dir = dir.Parent;
                }

                if (helpPath == null)
                {
                    HelpTextBox.Text = "Help file not found in repository (docs/PreFlight-Market-Check.md)." +
                        "\n\nLooked up from: " + baseDir;
                    return;
                }

                HelpTextBox.Text = File.ReadAllText(helpPath);
            }
            catch (Exception ex)
            {
                HelpTextBox.Text = "Failed to load help file: " + ex.Message;
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
