using System.Globalization;
using System.IO;
using System.Windows.Data;
using QBTools.BimHUB.Compatibility;

namespace QBTools.BimHUB.Converters;

public sealed class FolderNameConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path))
        {
            return "Library folder";
        }

        string trimmedPath = RuntimeCompatibility.TrimEndingDirectorySeparator(path);
        string? folderName = Path.GetFileName(trimmedPath);
        return string.IsNullOrWhiteSpace(folderName) ? trimmedPath : folderName;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
