using System;
using System.Globalization;
using System.Windows.Media;

namespace TradingAppDesktop.Models
{
    public class TradeEntry
    {
        public string Action { get; set; } = "ENTER";
        public bool IsLong { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public string Strategy { get; set; } = string.Empty;
        public decimal EntryPrice { get; set; }
        public DateTime Timestamp { get; set; }

        public string DisplayText =>
            $"ENTER {(IsLong ? "LONG" : "SHORT")} {Symbol} {Strategy} @{EntryPrice.ToString("F4", CultureInfo.InvariantCulture)} {Timestamp.ToString("dddd HH:mm", new CultureInfo("en-US"))} UTC";

        public SolidColorBrush Color
        {
            get
            {
                try
                {
                    var app = System.Windows.Application.Current;
                    if (app != null)
                    {
                        if (IsLong && app.Resources.Contains("SuccessColor"))
                            return app.Resources["SuccessColor"] as SolidColorBrush ?? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0F9D58"));
                        if (!IsLong && app.Resources.Contains("DangerColor"))
                            return app.Resources["DangerColor"] as SolidColorBrush ?? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#B91C1C"));
                    }
                }
                catch { }

                return IsLong ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0F9D58")) : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#B91C1C"));
            }
        }

        
    }
}