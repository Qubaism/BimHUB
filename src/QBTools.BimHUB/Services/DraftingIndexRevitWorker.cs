using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using QBTools.BimHUB.Compatibility;
using QBTools.BimHUB.ExternalEvents;
using QBTools.BimHUB.Models;
using System.Diagnostics;
using System.IO;

namespace QBTools.BimHUB.Services;

public sealed class DraftingIndexRevitWorker
{
    private const int CatalogCheckpointInterval = 100;
    private readonly string _jobPath;
    private readonly string _cancelPath;
    private readonly DraftingViewManifestService _manifestService = new();
    private bool _isProcessing;
    private bool _exitRequested;

    public DraftingIndexRevitWorker(string jobPath)
    {
        _jobPath = jobPath;
        _cancelPath = $"{jobPath}.cancel";
    }

    public static bool TryGetJobPath(out string jobPath)
    {
        jobPath = Environment.GetEnvironmentVariable(
            DraftingIndexWorkerJobService.JobEnvironmentVariable) ?? string.Empty;
        return !string.IsNullOrWhiteSpace(jobPath) && File.Exists(jobPath);
    }

    public void OnIdling(object? sender, IdlingEventArgs e)
    {
        if (_isProcessing || _exitRequested || sender is not UIApplication uiApplication) return;

        _isProcessing = true;
        try
        {
            DraftingIndexWorkerJob? job = DraftingIndexWorkerJobService.TryLoad(_jobPath);
            if (job is null)
            {
                RequestExit(uiApplication);
                return;
            }

            job.WorkerProcessId = RuntimeCompatibility.ProcessId;
            if (File.Exists(_cancelPath))
            {
                job.State = "Cancelled";
                DraftingIndexWorkerJobService.Save(_jobPath, job);
                RequestExit(uiApplication);
                return;
            }

            DraftingIndexWorkerJobItem? pending = job.Items.FirstOrDefault(item => item.State == "Pending");
            if (pending is null)
            {
                job.State = "Completed";
                DraftingIndexWorkerJobService.Save(_jobPath, job);
                RequestExit(uiApplication);
                return;
            }

            job.State = "Running";
            pending.State = "Running";
            DraftingIndexWorkerJobService.Save(_jobPath, job);
            int lastCatalogCheckpoint = -1;
            DraftingViewManifest manifest = DraftingViewIndexer.IndexProject(
                uiApplication.Application,
                pending.SourcePath,
                job.CategoryParameterName,
                progress =>
                {
                    bool shouldCheckpointCatalog = !pending.CatalogAvailable
                        || progress.PreviewGenerationCompleted
                        || progress.PreviewCount - lastCatalogCheckpoint >= CatalogCheckpointInterval;
                    if (shouldCheckpointCatalog)
                    {
                        _manifestService.Save(progress);
                        lastCatalogCheckpoint = progress.PreviewCount;
                    }

                    pending.CatalogAvailable = progress.CatalogCompleted;
                    pending.PreviewTotal = progress.Views.Count + progress.Sheets.Count;
                    pending.PreviewCount = progress.PreviewCount;
                    pending.PreviewFailureCount = progress.PreviewFailureCount;
                    DraftingIndexWorkerJobService.Save(_jobPath, job);
                },
                () => File.Exists(_cancelPath));
            pending.PreviewCount = manifest.PreviewCount;
            pending.PreviewFailureCount = manifest.PreviewFailureCount;
            pending.Error = manifest.Status;
            if (File.Exists(_cancelPath))
            {
                pending.State = "Cancelled";
                job.State = "Cancelled";
                DraftingIndexWorkerJobService.Save(_jobPath, job);
                RequestExit(uiApplication);
                return;
            }

            pending.State = string.IsNullOrWhiteSpace(manifest.Status) ? "Completed" : "Skipped";
            DraftingIndexWorkerJobService.Save(_jobPath, job);
            e.SetRaiseWithoutDelay();
        }
        catch (Exception exception)
        {
            DraftingIndexWorkerJob? job = DraftingIndexWorkerJobService.TryLoad(_jobPath);
            if (job is not null)
            {
                DraftingIndexWorkerJobItem? running = job.Items.FirstOrDefault(item => item.State == "Running");
                if (running is not null)
                {
                    running.State = "Failed";
                    running.Error = exception.Message;
                }
                job.State = "Failed";
                job.Error = exception.Message;
                DraftingIndexWorkerJobService.Save(_jobPath, job);
            }

            RequestExit(uiApplication);
        }
        finally
        {
            _isProcessing = false;
        }
    }

    private void RequestExit(UIApplication uiApplication)
    {
        if (_exitRequested) return;
        _exitRequested = true;
        ThumbnailRevitWorker.HideWindow(Process.GetCurrentProcess().MainWindowHandle);
        RevitCommandId exitCommand = RevitCommandId.LookupCommandId("ID_APP_EXIT");
        if (uiApplication.CanPostCommand(exitCommand)) uiApplication.PostCommand(exitCommand);
        else Process.GetCurrentProcess().CloseMainWindow();
    }
}
