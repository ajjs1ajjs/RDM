using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using RemoteManager.Helpers;

namespace RemoteManager.Converters;

public class StatusDotConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool connected)
            return connected ? System.Windows.Media.Brushes.Green : System.Windows.Media.Brushes.Gray;
        return System.Windows.Media.Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class StatusTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool connected)
            return connected ? L.Status_ConnectedText : L.Status_Disconnected;
        return "";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
