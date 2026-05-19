using System.Globalization;
using System.Windows;
using System.Windows.Data;
using RemoteManager.Models;

namespace RemoteManager.Converters;

public class SshAuthTypeToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is SshAuthType authType)
            return authType == SshAuthType.Key ? Visibility.Visible : Visibility.Collapsed;
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
