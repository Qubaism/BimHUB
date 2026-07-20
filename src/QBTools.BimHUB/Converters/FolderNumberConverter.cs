using System.Globalization;
using System.Windows.Data;

namespace QBTools.BimHUB.Converters;

public sealed class FolderNumberConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is int index ? $"{index + 1}." : string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
