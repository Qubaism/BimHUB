using QBTools.BimHUB.Models;
using QBTools.BimHUB.Compatibility;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace QBTools.BimHUB.Services;

public sealed class DraftingViewManifestService
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };
    private readonly string _cacheRoot;

    public DraftingViewManifestService(string? cacheRoot = null)
    {
        _cacheRoot = cacheRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "QBTools",
            "BimHUB",
            "details");
    }

    public IReadOnlyList<string> DiscoverProjects(IEnumerable<string> roots)
    {
        var projects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string root in roots.Where(Directory.Exists))
        {
            try
            {
                foreach (string path in RuntimeCompatibility.EnumerateFilesRecursively(root, "*.rvt"))
                {
                    projects.Add(Path.GetFullPath(path));
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }

        return projects.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public IReadOnlyList<DraftingViewManifest> LoadCachedForRoots(
        IEnumerable<string> roots,
        string revitVersion)
    {
        if (!Directory.Exists(_cacheRoot)) return [];

        string[] normalizedRoots = roots
            .Where(root => !string.IsNullOrWhiteSpace(root))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (normalizedRoots.Length == 0) return [];

        var candidates = new List<(DraftingViewManifest Manifest, DateTime LastWriteTimeUtc)>();
        try
        {
            foreach (string manifestPath in Directory.EnumerateFiles(
                         _cacheRoot,
                         "*.BimHUB.details.json",
                         SearchOption.AllDirectories))
            {
                DraftingViewManifest? manifest = TryLoad(manifestPath);
                if (manifest is null
                    || !string.IsNullOrWhiteSpace(manifest.Status)
                    || !string.Equals(manifest.RevitFormat, revitVersion, StringComparison.Ordinal)
                    || (!manifest.CatalogCompleted && manifest.Views.Count == 0 && manifest.Sheets.Count == 0)
                    || !IsUnderAnyRoot(manifest.SourceProjectPath, normalizedRoots))
                {
                    continue;
                }

                candidates.Add((manifest, File.GetLastWriteTimeUtc(manifestPath)));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }

        return candidates
            .GroupBy(item => item.Manifest.SourceProjectPath, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(item => item.Manifest.CacheVersion)
                .ThenByDescending(item => item.Manifest.CatalogCompleted)
                .ThenByDescending(item => item.LastWriteTimeUtc)
                .First()
                .Manifest)
            .OrderBy(manifest => manifest.SourceProjectPath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public DraftingViewManifest? LoadCurrent(string sourcePath, string revitVersion)
    {
        try
        {
            var file = new FileInfo(sourcePath);
            if (!file.Exists) return null;
            DraftingViewManifest? manifest = LoadLatestCached(sourcePath, revitVersion);
            return manifest is not null
                && manifest.FileLength == file.Length
                && manifest.LastWriteTimeUtcTicks == file.LastWriteTimeUtc.Ticks
                    ? manifest
                    : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public DraftingViewManifest? LoadLatestCached(string sourcePath, string revitVersion)
    {
        string[] candidates =
        [
            GetManifestPath(sourcePath, revitVersion),
            GetLegacyManifestPath(sourcePath)
        ];
        return candidates
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(File.Exists)
            .Select(TryLoad)
            .OfType<DraftingViewManifest>()
            .Where(manifest => string.IsNullOrWhiteSpace(manifest.Status)
                && string.Equals(manifest.SourceProjectPath, sourcePath, StringComparison.OrdinalIgnoreCase)
                && string.Equals(manifest.RevitFormat, revitVersion, StringComparison.Ordinal))
            .OrderByDescending(manifest => manifest.CacheVersion)
            .FirstOrDefault();
    }

    public void Save(DraftingViewManifest manifest)
    {
        if (string.IsNullOrWhiteSpace(manifest.RevitFormat))
        {
            throw new InvalidOperationException("A Revit version is required before saving a details catalog.");
        }

        string path = GetManifestPath(manifest.SourceProjectPath, manifest.RevitFormat);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporaryPath = $"{path}.{RuntimeCompatibility.ProcessId}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(manifest, SerializerOptions));
        RuntimeCompatibility.MoveFileReplacing(temporaryPath, path);
    }

    public string GetManifestPath(string sourcePath, string revitVersion)
    {
        string projectName = Path.GetFileNameWithoutExtension(sourcePath);
        return Path.Combine(
            GetCacheDirectory(sourcePath),
            $"{projectName}.Revit{revitVersion}.BimHUB.details.json");
    }

    public string GetLegacyManifestPath(string sourcePath)
    {
        string projectName = Path.GetFileNameWithoutExtension(sourcePath);
        return Path.Combine(GetCacheDirectory(sourcePath), $"{projectName}.BimHUB.details.json");
    }

    public string GetCacheDirectory(string sourcePath)
    {
        string normalizedPath = Path.GetFullPath(sourcePath).ToUpperInvariant();
        string hash = RuntimeCompatibility.GetSha256Prefix(normalizedPath, 16);
        return Path.Combine(_cacheRoot, hash);
    }

    public static IReadOnlyList<DraftingViewItem> ToViewItems(DraftingViewManifest manifest)
    {
        string projectName = Path.GetFileNameWithoutExtension(manifest.SourceProjectPath);
        if (!string.IsNullOrWhiteSpace(manifest.Status))
        {
            return [];
        }

        return manifest.Views.Select(view => new DraftingViewItem
        {
            Name = view.Name,
            SourceProjectPath = manifest.SourceProjectPath,
            SourceProjectName = projectName,
            ViewUniqueId = view.ViewUniqueId,
            Scale = view.Scale,
            Category = view.Category,
            ThumbnailPath = File.Exists(view.ThumbnailPath) ? view.ThumbnailPath : null
        }).ToList();
    }

    public static IReadOnlyList<DraftingSheetItem> ToSheetItems(DraftingViewManifest manifest)
    {
        string projectName = Path.GetFileNameWithoutExtension(manifest.SourceProjectPath);
        if (!string.IsNullOrWhiteSpace(manifest.Status))
        {
            return [];
        }

        return manifest.Sheets
            .Where(sheet => sheet.PlacedDraftingViewCount > 0)
            .Select(sheet => new DraftingSheetItem
            {
                Name = string.IsNullOrWhiteSpace(sheet.SheetNumber)
                ? sheet.Name
                : $"{sheet.SheetNumber} - {sheet.Name}",
                SheetName = sheet.Name,
                SheetNumber = sheet.SheetNumber,
                SourceProjectPath = manifest.SourceProjectPath,
                SourceProjectName = projectName,
                SheetUniqueId = sheet.SheetUniqueId,
                PlacedDraftingViewCount = sheet.PlacedDraftingViewCount,
                Category = sheet.Category,
                CategoryFilters = DetailCategoryService.Normalize(
                    sheet.Categories.Count > 0 ? sheet.Categories : [sheet.Category]),
                ThumbnailPath = File.Exists(sheet.ThumbnailPath) ? sheet.ThumbnailPath : null
            }).ToList();
    }

    private static DraftingViewManifest? TryLoad(string manifestPath)
    {
        try
        {
            return JsonSerializer.Deserialize<DraftingViewManifest>(
                File.ReadAllText(manifestPath),
                SerializerOptions);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static bool IsUnderAnyRoot(string sourcePath, IEnumerable<string> roots)
    {
        if (string.IsNullOrWhiteSpace(sourcePath)) return false;

        string normalizedPath;
        try
        {
            normalizedPath = Path.GetFullPath(sourcePath).TrimEnd(Path.DirectorySeparatorChar);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return false;
        }

        foreach (string root in roots)
        {
            string normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar);
            if (string.Equals(normalizedPath, normalizedRoot, StringComparison.OrdinalIgnoreCase)
                || normalizedPath.StartsWith(
                    normalizedRoot + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
