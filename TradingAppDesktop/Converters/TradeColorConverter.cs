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
                // Prefer theme-provided brushes if available
                try
                {
                    var app = System.Windows.Application.Current;
                    if (app != null)
                    {
                        if (tradeEntry.IsLong && app.Resources.Contains("SuccessColor"))
                            return app.Resources["SuccessColor"] as Brush ?? Brushes.Green;
                        if (!tradeEntry.IsLong && app.Resources.Contains("DangerColor"))
                            return app.Resources["DangerColor"] as Brush ?? Brushes.Red;
                    }
                }
                catch { /* ignore and fallback */ }

                // Fallback to darker green/red (better on light backgrounds)
                return tradeEntry.IsLong ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0F9D58")) : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#B91C1C"));
            }
            return Brushes.White;
        }


        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}