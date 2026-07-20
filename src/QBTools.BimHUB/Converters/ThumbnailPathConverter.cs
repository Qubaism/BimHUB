using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace QBTools.BimHUB.Converters;

public sealed class ThumbnailPathConverter : IValueConverter
{
    private const int MaximumCachedCards = 160;
    private static readonly object CacheLock = new();
    private static readonly Dictionary<string, BitmapSource> CardCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Queue<string> CardCacheOrder = [];

    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            int decodeWidth = int.TryParse(parameter?.ToString(), out int requestedWidth)
                ? Math.Max(0, requestedWidth)
                : 0;
            string? cacheKey = decodeWidth > 0 ? $"{path}|{decodeWidth}" : null;
            if (cacheKey is not null)
            {
                lock (CacheLock)
                {
                    if (CardCache.TryGetValue(cacheKey, out BitmapSource? cached)) return cached;
                }
            }

            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            if (decodeWidth > 0) image.DecodePixelWidth = decodeWidth;
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.EndInit();
            image.Freeze();

            if (cacheKey is not null)
            {
                lock (CacheLock)
                {
                    if (!CardCache.ContainsKey(cacheKey))
                    {
                        CardCache[cacheKey] = image;
                        CardCacheOrder.Enqueue(cacheKey);
                    }

                    while (CardCacheOrder.Count > MaximumCachedCards)
                    {
                        CardCache.Remove(CardCacheOrder.Dequeue());
                    }
                }
            }
            return image;
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or ArgumentException
            or UriFormatException)
        {
            return null;
        }
    }

    public static void Invalidate(string path)
    {
        lock (CacheLock)
        {
            foreach (string key in CardCache.Keys
                         .Where(key => key.StartsWith($"{path}|", StringComparison.OrdinalIgnoreCase))
                         .ToList())
            {
                CardCache.Remove(key);
            }
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
