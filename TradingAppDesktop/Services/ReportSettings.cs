namespace TradingAppDesktop.Services
{
    public class ReportSettings
    {
        // Default values if settings file doesn't exist
        public string OutputPath { get; set; } = "Reports";
        public bool AutoOpen { get; set; } = true;
    }
}