using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

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
            return connected ? "Connected" : "Disconnected";
        return "";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class ConnectionTypeIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string typeStr)
        {
            return typeStr.ToLower() == "rdp" ? "🖥️" : "💻";
        }
        return "⊞";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
