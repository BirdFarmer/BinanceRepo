using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace TradingAppDesktop.Converters
{
    public class DecimalSignToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is decimal d)
            {
                if (d > 0) return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00B894")); // green
                if (d < 0) return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF7675")); // red
                return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#B0B0B0")); // neutral
            }
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#B0B0B0"));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }
}
