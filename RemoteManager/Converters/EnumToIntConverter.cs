using System.Globalization;
using System.Windows.Data;

namespace RemoteManager.Converters;

public class EnumToIntConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is Enum e ? System.Convert.ToInt32(e) : 0;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int i && targetType.IsEnum)
            return System.Enum.ToObject(targetType, i);
        return System.Windows.Data.Binding.DoNothing;
    }
}
