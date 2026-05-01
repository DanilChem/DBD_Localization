using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace DBD_Trans.Converters
{
    public class BoolToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b && b)
            {
                var colorString = parameter?.ToString() ?? "#CA5100";
                return new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorString));
            }
            return new SolidColorBrush(Colors.Transparent);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}