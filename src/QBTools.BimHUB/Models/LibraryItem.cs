using System.IO;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using QBTools.BimHUB.Compatibility;

namespace QBTools.BimHUB.Models;

public sealed class LibraryItem : INotifyPropertyChanged
{
    public LibraryItem(FileInfo file, string libraryRoot)
    {
        Name = Path.GetFileNameWithoutExtension(file.Name);
        FullPath = file.FullName;
        SourcePath = libraryRoot;
        SourceName = new DirectoryInfo(libraryRoot).Name;
        RelativeFolder = GetRelativeFolder(file.DirectoryName, libraryRoot);
        FolderCategory = file.Directory?.Name ?? SourceName;
        _category = FolderCategory;
        FileSize = FormatFileSize(file.Length);
        LastModified = file.LastWriteTime.ToString("yyyy-MM-dd");
        FileLength = file.Length;
        LastWriteTimeUtcTicks = file.LastWriteTimeUtc.Ticks;
    }

    private string _category;
    private string? _thumbnailPath;
    private bool _isFavorite;
    private string? _indexStatus;
    private IReadOnlyList<string> _typeNames = [];

    public string Name { get; }

    public string FullPath { get; }

    public string SourcePath { get; }

    public string SourceName { get; }

    public string RelativeFolder { get; }

    public string FolderCategory { get; }

    public string FileSize { get; }

    public string MetadataLabel => "Size";

    public string FallbackLabel => "RFA";

    public string LastModified { get; }

    public long FileLength { get; }

    public long LastWriteTimeUtcTicks { get; }

    public string Category
    {
        get => _category;
        set
        {
            SetField(ref _category, ResolveCategory(value));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayCategory)));
        }
    }

    public string DisplayCategory => _category;

    public string? ThumbnailPath
    {
        get => _thumbnailPath;
        set => SetField(ref _thumbnailPath, value);
    }

    public bool IsFavorite
    {
        get => _isFavorite;
        set => SetField(ref _isFavorite, value);
    }

    public string? IndexStatus
    {
        get => _indexStatus;
        set => SetField(ref _indexStatus, value);
    }

    public IReadOnlyList<string> TypeNames
    {
        get => _typeNames;
        set
        {
            SetField(ref _typeNames, value);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasTypes)));
        }
    }

    public bool HasTypes => TypeNames.Count > 0;

    public bool SupportsTypes => true;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private static string GetRelativeFolder(string? directory, string libraryRoot)
    {
        if (directory is null)
        {
            return "Library root";
        }

        string relativeFolder = RuntimeCompatibility.GetRelativePath(libraryRoot, directory);
        return relativeFolder == "." ? "Library root" : relativeFolder;
    }

    private static string FormatFileSize(long bytes)
    {
        const double bytesPerMegabyte = 1024d * 1024d;
        const double bytesPerKilobyte = 1024d;

        if (bytes >= bytesPerMegabyte)
        {
            return $"{bytes / bytesPerMegabyte:0.0} MB";
        }

        return $"{Math.Max(1, bytes / bytesPerKilobyte):0} KB";
    }

    private string ResolveCategory(string? category)
    {
        if (string.IsNullOrWhiteSpace(category)) return FolderCategory;

        string value = category!.Trim();
        return value.Equals("Not indexed", StringComparison.OrdinalIgnoreCase)
            || value.Equals("Indexing...", StringComparison.OrdinalIgnoreCase)
            || value.Equals("Unavailable", StringComparison.OrdinalIgnoreCase)
            || value.Equals("Uncategorized", StringComparison.OrdinalIgnoreCase)
            || value.Equals("Unknown", StringComparison.OrdinalIgnoreCase)
            ? FolderCategory
            : value;
    }
}
