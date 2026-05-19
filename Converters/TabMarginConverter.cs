using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace RemoteManager.Converters;

public class TabMarginConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool isSelected && isSelected)
            return new Thickness(0);

        // Move off-screen to hide without destroying the HWND
        return new Thickness(-30000, -30000, 0, 0);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
