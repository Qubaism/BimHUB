using QBTools.BimHUB.Models;
using QBTools.BimHUB.Compatibility;
using System.IO;
using System.Text.Json;

namespace QBTools.BimHUB.Services;

public sealed class FamilyCacheService
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };
    private readonly string _cacheDirectory;
    private readonly string _indexPath;
    private readonly Dictionary<string, FamilyCacheRecord> _records;
    private readonly object _recordsLock = new();

    public FamilyCacheService()
    {
        string applicationData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _cacheDirectory = Path.Combine(applicationData, "QBTools", "BimHUB", "cache");
        _indexPath = Path.Combine(_cacheDirectory, "index.json");
        _records = LoadRecords();
    }

    public bool TryApply(LibraryItem item)
    {
        string thumbnailPath = GetThumbnailPath(item.FullPath);
        FamilyCacheRecord? record;
        lock (_recordsLock)
        {
            _records.TryGetValue(item.FullPath, out record);
        }

        if (record is null
            || record.FileLength != item.FileLength
            || record.LastWriteTimeUtcTicks != item.LastWriteTimeUtcTicks
            || !File.Exists(thumbnailPath))
        {
            item.ThumbnailPath = null;
            item.IndexStatus = record is null ? null : "Thumbnail is out of date.";
            return false;
        }

        item.ThumbnailPath = thumbnailPath;
        item.Category = record.Category;
        item.TypeNames = record.TypeNames;
        item.IndexStatus = record.Status;
        return true;
    }

    public bool IsCurrent(LibraryItem item)
    {
        lock (_recordsLock)
        {
            return _records.TryGetValue(item.FullPath, out FamilyCacheRecord? record)
                && record.FileLength == item.FileLength
                && record.LastWriteTimeUtcTicks == item.LastWriteTimeUtcTicks
                && File.Exists(GetThumbnailPath(item.FullPath));
        }
    }

    public string GetThumbnailPath(string fullPath)
    {
        string familyDirectory = Path.GetDirectoryName(fullPath)
            ?? throw new ArgumentException("The family path has no directory.", nameof(fullPath));
        string familyName = Path.GetFileNameWithoutExtension(fullPath);
        return Path.Combine(familyDirectory, "_ThumbnailCache", $"{familyName}.png");
    }

    public void Reload()
    {
        Dictionary<string, FamilyCacheRecord> latestRecords = LoadRecords();
        lock (_recordsLock)
        {
            _records.Clear();
            foreach (KeyValuePair<string, FamilyCacheRecord> pair in latestRecords)
            {
                _records[pair.Key] = pair.Value;
            }
        }
    }

    public void Store(FamilyCacheRecord record)
    {
        using var mutex = new Mutex(false, "Local\\QBTools.BimHUB.FamilyCache");
        bool ownsMutex;
        try { ownsMutex = mutex.WaitOne(TimeSpan.FromSeconds(10)); }
        catch (AbandonedMutexException) { ownsMutex = true; }
        if (!ownsMutex) throw new IOException("The family cache is busy in another Revit process.");

        try
        {
            Dictionary<string, FamilyCacheRecord> latestRecords = LoadRecords();
            latestRecords[record.FullPath] = record;
            Directory.CreateDirectory(_cacheDirectory);
            string temporaryPath = $"{_indexPath}.{RuntimeCompatibility.ProcessId}.{Guid.NewGuid():N}.tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(latestRecords.Values, SerializerOptions));
            RuntimeCompatibility.MoveFileReplacing(temporaryPath, _indexPath);
            lock (_recordsLock)
            {
                _records.Clear();
                foreach (KeyValuePair<string, FamilyCacheRecord> pair in latestRecords)
                {
                    _records[pair.Key] = pair.Value;
                }
            }
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }

    private Dictionary<string, FamilyCacheRecord> LoadRecords()
    {
        try
        {
            if (!File.Exists(_indexPath))
            {
                return new Dictionary<string, FamilyCacheRecord>(StringComparer.OrdinalIgnoreCase);
            }

            List<FamilyCacheRecord> records = JsonSerializer.Deserialize<List<FamilyCacheRecord>>(
                File.ReadAllText(_indexPath), SerializerOptions) ?? [];
            return records
                .Where(record => !string.IsNullOrWhiteSpace(record.FullPath))
                .ToDictionary(record => record.FullPath, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return new Dictionary<string, FamilyCacheRecord>(StringComparer.OrdinalIgnoreCase);
        }
    }
}
