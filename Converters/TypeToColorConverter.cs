using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using RemoteManager.Models;

namespace RemoteManager.Converters;

public class TypeToColorConverter : IValueConverter
{
    private static readonly SolidColorBrush RdpBrush = new(Color.FromRgb(0, 120, 215));
    private static readonly SolidColorBrush SshBrush = new(Color.FromRgb(139, 92, 246));
    private static readonly SolidColorBrush WebBrush = new(Color.FromRgb(245, 158, 11));
    private static readonly SolidColorBrush DefaultBrush = new(Colors.Gray);

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is ConnectionType type)
        {
            return type switch
            {
                ConnectionType.RDP => RdpBrush,
                ConnectionType.SSH => SshBrush,
                ConnectionType.Web => WebBrush,
                _ => DefaultBrush
            };
        }
        return DefaultBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
