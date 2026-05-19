using System.Globalization;
using System.Windows;
using System.Windows.Data;
using RemoteManager.Models;

namespace RemoteManager.Converters;

public class RadioBoolToConnectionTypeRdpConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is ConnectionType type && type == ConnectionType.RDP;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is bool isChecked && isChecked ? ConnectionType.RDP : ConnectionType.SSH;
    }
}

public class RadioBoolToConnectionTypeSshConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is ConnectionType type && type == ConnectionType.SSH;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is bool isChecked && isChecked ? ConnectionType.SSH : ConnectionType.RDP;
    }
}

public class TypeToRdpVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is ConnectionType type && type == ConnectionType.RDP
            ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class TypeToSshVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is ConnectionType type && type == ConnectionType.SSH
            ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
