using QBTools.BimHUB.Models;
using QBTools.BimHUB.Compatibility;
using System.IO;

namespace QBTools.BimHUB.Services;

public static class LibraryScanner
{
    public static IReadOnlyList<LibraryItem> ScanFamilies(IEnumerable<string> libraryRoots)
    {
        var itemsByPath = new Dictionary<string, LibraryItem>(StringComparer.OrdinalIgnoreCase);

        foreach (string libraryRoot in libraryRoots.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            try
            {
                string fullLibraryRoot = Path.GetFullPath(libraryRoot);
                if (!Directory.Exists(fullLibraryRoot))
                {
                    continue;
                }

                foreach (string filePath in RuntimeCompatibility.EnumerateFilesRecursively(fullLibraryRoot, "*.rfa"))
                {
                    string fullPath = Path.GetFullPath(filePath);
                    if (!itemsByPath.ContainsKey(fullPath))
                    {
                        itemsByPath.Add(fullPath, new LibraryItem(new FileInfo(fullPath), fullLibraryRoot));
                    }
                }
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
                // Keep other configured sources usable when this source is offline or inaccessible.
            }
        }

        return itemsByPath.Values
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.RelativeFolder, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
