using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace DBD_Trans.Converters
{
    /// <summary>
    /// Visible, если число больше нуля — используется для бейджа счётчика
    /// непросмотренных изменений на кнопке "История изменений".
    /// </summary>
    public class IntToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return (value is int i && i > 0) ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
