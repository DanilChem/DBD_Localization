using System;
using System.Globalization;
using System.Windows.Controls;
using System.Windows.Data;

namespace DBD_Trans.Converters
{
    public class BoolToScrollBarVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool isFocus = value is bool b && b;
            // Если режим фокуса включен -> скрываем скроллбар, иначе -> показываем (Auto)
            return isFocus ? ScrollBarVisibility.Hidden : ScrollBarVisibility.Auto;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}