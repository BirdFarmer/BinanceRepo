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

        public SolidColorBrush Color => IsLong 
            ? Brushes.LightGreen
            : Brushes.LightCoral;

        
    }
}