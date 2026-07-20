using QBTools.BimHUB.Models;
using QBTools.BimHUB.Compatibility;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace QBTools.BimHUB.Services;

public static class DraftingIndexWorkerJobService
{
    public const string JobEnvironmentVariable = "QBTOOLS_BIMHUB_DETAIL_INDEX_JOB";
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static DraftingIndexWorkerLaunch Start(
        string revitVersion,
        string categoryParameterName,
        IReadOnlyList<string> sourcePaths)
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
        string jobPath = Path.Combine(jobDirectory, $"details-{id}.json");
        string cancelPath = $"{jobPath}.cancel";
        var job = new DraftingIndexWorkerJob
        {
            Id = id,
            RevitVersion = revitVersion,
            CategoryParameterName = categoryParameterName,
            Items = sourcePaths.Select(path => new DraftingIndexWorkerJobItem { SourcePath = path }).ToList()
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
            ?? throw new InvalidOperationException("The background details worker could not be started.");
        return new DraftingIndexWorkerLaunch(jobPath, cancelPath, process);
    }

    public static DraftingIndexWorkerJob? TryLoad(string jobPath)
    {
        try
        {
            if (!File.Exists(jobPath)) return null;
            return JsonSerializer.Deserialize<DraftingIndexWorkerJob>(
                File.ReadAllText(jobPath),
                SerializerOptions);
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            return null;
        }
    }

    public static void Save(string jobPath, DraftingIndexWorkerJob job)
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

public sealed record DraftingIndexWorkerLaunch(
    string JobPath,
    string CancelPath,
    Process Process);
