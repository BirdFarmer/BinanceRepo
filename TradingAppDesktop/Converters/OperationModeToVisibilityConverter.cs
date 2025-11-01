using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using BinanceTestnet.Enums;

namespace TradingAppDesktop.Converters
{
    public class OperationModeToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // Show only in paper mode
            if (value is OperationMode mode)
            {
                return mode == OperationMode.LivePaperTrading ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }
}
