using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using BinanceTestnet.Enums;

namespace TradingAppDesktop.Converters
{
    public class OperationModeStatusConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // parameter can be "text" or "brush"
            var kind = (parameter as string)?.ToLowerInvariant();

            // If value is missing or not an OperationMode, show IDLE with a neutral brush
            if (value is not OperationMode)
            {
                if (kind == "text") return "IDLE";
                if (kind == "brush") return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#808080"));
                return "IDLE";
            }

            var mode = (OperationMode)value;

            string text = mode switch
            {
                OperationMode.LiveRealTrading => "REAL",
                OperationMode.Backtest => "BACKTEST",
                OperationMode.LivePaperTrading => "PAPER",
                _ => "IDLE"
            };

            // Colors aligned loosely with theme palette (avoid accent purple for status)
            // REAL -> Success green, PAPER -> Warning yellow, BACKTEST -> Calm blue, IDLE -> Neutral gray
            var brush = mode switch
            {
                OperationMode.LiveRealTrading => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00B894")),
                OperationMode.Backtest => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3498DB")),
                OperationMode.LivePaperTrading => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FDCB6E")),
                _ => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#808080"))
            };

            if (kind == "text") return text;
            if (kind == "brush") return brush;

            // Default: return text
            return text;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
