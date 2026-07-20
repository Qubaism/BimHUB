using QBTools.BimHUB.Services;

namespace QBTools.BimHUB.Tests;

public sealed class LibraryScannerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"BimHUB-tests-{Guid.NewGuid():N}");

    [Fact]
    public void FindsFamiliesRecursivelyAndDeduplicatesOverlappingRoots()
    {
        string doors = Directory.CreateDirectory(Path.Combine(_root, "Doors")).FullName;
        File.WriteAllBytes(Path.Combine(doors, "Single Door.rfa"), [1, 2, 3]);

        var items = LibraryScanner.ScanFamilies([_root, doors]);

        var item = Assert.Single(items);
        Assert.Equal("Single Door", item.Name);
        Assert.Equal("Doors", item.FolderCategory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
