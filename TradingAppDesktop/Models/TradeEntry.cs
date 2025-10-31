using System;
using System.Windows.Media;

namespace TradingAppDesktop.Models
{
    public class TradeEntry
    {
        public string Action { get; set; } = "ENTER";
        public bool IsLong { get; set; }
        public string Symbol { get; set; }
        public string Strategy { get; set; }
        public decimal EntryPrice { get; set; }
        public DateTime Timestamp { get; set; }

        public string DisplayText =>
            $"ENTER {(IsLong ? "LONG" : "SHORT")} {Symbol} {Strategy} @{EntryPrice:F4} {Timestamp:dddd HH:mm} UTC";

        public SolidColorBrush Color => IsLong 
            ? Brushes.LightGreen
            : Brushes.LightCoral;

        
    }
}