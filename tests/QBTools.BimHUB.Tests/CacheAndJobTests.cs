using QBTools.BimHUB.Models;
using QBTools.BimHUB.Services;

namespace QBTools.BimHUB.Tests;

public sealed class CacheAndJobTests
{
    [Fact]
    public void DetailCacheIsLocalAndStableForSourcePath()
    {
        var service = new DraftingViewManifestService();
        string source = Path.Combine(Path.GetTempPath(), "ReadonlyLibrary", "Details.rvt");

        string first = service.GetCacheDirectory(source);
        string second = service.GetCacheDirectory(source);

        Assert.Equal(first, second);
        Assert.StartsWith(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            first,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ReadonlyLibrary", first, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CachedCatalogLoadsWithoutAccessingTheSourceFile()
    {
        string cacheRoot = CreateTempDirectory("BimHUB-details-cache-");
        string libraryRoot = Path.Combine(Path.GetTempPath(), $"OfflineLibrary-{Guid.NewGuid():N}");
        string sourcePath = Path.Combine(libraryRoot, "Details.rvt");
        try
        {
            var service = new DraftingViewManifestService(cacheRoot);
            service.Save(CreateManifest(sourcePath, "2026", "A Detail"));

            IReadOnlyList<DraftingViewManifest> loaded = service.LoadCachedForRoots(
                [libraryRoot],
                "2026");

            DraftingViewManifest manifest = Assert.Single(loaded);
            Assert.Equal("A Detail", Assert.Single(manifest.Views).Name);
        }
        finally
        {
            Directory.Delete(cacheRoot, true);
        }
    }

    [Fact]
    public void RevitVersionsUseSeparateCatalogFiles()
    {
        string cacheRoot = CreateTempDirectory("BimHUB-version-cache-");
        string libraryRoot = Path.Combine(Path.GetTempPath(), $"VersionedLibrary-{Guid.NewGuid():N}");
        string sourcePath = Path.Combine(libraryRoot, "Details.rvt");
        try
        {
            var service = new DraftingViewManifestService(cacheRoot);
            service.Save(CreateManifest(sourcePath, "2025", "2025 Detail"));
            service.Save(CreateManifest(sourcePath, "2026", "2026 Detail"));

            DraftingViewManifest catalog2025 = Assert.Single(
                service.LoadCachedForRoots([libraryRoot], "2025"));
            DraftingViewManifest catalog2026 = Assert.Single(
                service.LoadCachedForRoots([libraryRoot], "2026"));

            Assert.Equal("2025 Detail", Assert.Single(catalog2025.Views).Name);
            Assert.Equal("2026 Detail", Assert.Single(catalog2026.Views).Name);
            Assert.NotEqual(
                service.GetManifestPath(sourcePath, "2025"),
                service.GetManifestPath(sourcePath, "2026"));
        }
        finally
        {
            Directory.Delete(cacheRoot, true);
        }
    }

    [Fact]
    public void ThumbnailJobRoundTripsAtomically()
    {
        string directory = CreateTempDirectory("BimHUB-job-");
        string path = Path.Combine(directory, "job.json");
        try
        {
            var job = new ThumbnailWorkerJob { Id = "test", State = "Running" };
            ThumbnailWorkerJobService.Save(path, job);

            ThumbnailWorkerJob? loaded = ThumbnailWorkerJobService.TryLoad(path);

            Assert.NotNull(loaded);
            Assert.Equal("test", loaded.Id);
            Assert.Equal("Running", loaded.State);
            Assert.True(loaded.UpdatedAtUtc > DateTime.MinValue);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static DraftingViewManifest CreateManifest(
        string sourcePath,
        string revitVersion,
        string viewName)
    {
        return new DraftingViewManifest
        {
            SourceProjectPath = sourcePath,
            RevitFormat = revitVersion,
            CatalogCompleted = true,
            PreviewGenerationCompleted = true,
            Views =
            [
                new DraftingViewManifestItem
                {
                    Name = viewName,
                    ViewUniqueId = Guid.NewGuid().ToString("N")
                }
            ]
        };
    }

    private static string CreateTempDirectory(string prefix)
    {
        string path = Path.Combine(Path.GetTempPath(), $"{prefix}{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
