using QBTools.BimHUB.Models;
using QBTools.BimHUB.Compatibility;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace QBTools.BimHUB.Services;

public static class ThumbnailWorkerJobService
{
    public const string JobEnvironmentVariable = "QBTOOLS_BIMHUB_THUMBNAIL_JOB";
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static ThumbnailWorkerLaunch Start(
        string revitVersion,
        IReadOnlyList<ThumbnailWorkerJobItem> items)
    {
        string revitDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "Autodesk",
            $"Revit {revitVersion}");
        string executablePath = Path.Combine(revitDirectory, "Revit.exe");
        if (!File.Exists(executablePath))
        {
            throw new FileNotFoundException($"Revit {revitVersion} was not found.", executablePath);
        }

        string jobDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "QBTools",
            "BimHUB",
            "jobs");
        Directory.CreateDirectory(jobDirectory);
        string id = Guid.NewGuid().ToString("N");
        string jobPath = Path.Combine(jobDirectory, $"thumbnail-{id}.json");
        string cancelPath = $"{jobPath}.cancel";
        var job = new ThumbnailWorkerJob
        {
            Id = id,
            RevitVersion = revitVersion,
            Items = items.ToList()
        };
        Save(jobPath, job);

        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = revitDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        startInfo.Environment[JobEnvironmentVariable] = jobPath;
        Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("The background Revit worker could not be started.");
        return new ThumbnailWorkerLaunch(jobPath, cancelPath, process);
    }

    public static ThumbnailWorkerJob? TryLoad(string jobPath)
    {
        try
        {
            if (!File.Exists(jobPath)) return null;
            return JsonSerializer.Deserialize<ThumbnailWorkerJob>(
                File.ReadAllText(jobPath),
                SerializerOptions);
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            return null;
        }
    }

    public static void Save(string jobPath, ThumbnailWorkerJob job)
    {
        job.UpdatedAtUtc = DateTime.UtcNow;
        string temporaryPath = $"{jobPath}.{RuntimeCompatibility.ProcessId}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(job, SerializerOptions));
        RuntimeCompatibility.MoveFileReplacing(temporaryPath, jobPath);
    }

    public static void RequestCancellation(string cancelPath)
    {
        File.WriteAllText(cancelPath, DateTime.UtcNow.ToString("O"));
    }
}

public sealed record ThumbnailWorkerLaunch(
    string JobPath,
    string CancelPath,
    Process Process);
