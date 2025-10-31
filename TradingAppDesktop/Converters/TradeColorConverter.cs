using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using TradingAppDesktop.Models;

namespace TradingAppDesktop.Converters
{
    public class TradeColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is TradeEntry tradeEntry)  // Use TradeEntry instead of Trade
            {
                return tradeEntry.IsLong 
                    ? Brushes.LightGreen
                    : Brushes.LightCoral;
            }
            return Brushes.White;
        }


        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}