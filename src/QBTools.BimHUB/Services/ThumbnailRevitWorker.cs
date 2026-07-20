using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using QBTools.BimHUB.Compatibility;
using QBTools.BimHUB.ExternalEvents;
using QBTools.BimHUB.Models;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace QBTools.BimHUB.Services;

public sealed class ThumbnailRevitWorker
{
    private const int HideWindowCommand = 0;
    private readonly string _jobPath;
    private readonly string _cancelPath;
    private readonly FamilyCacheService _cacheService = new();
    private bool _isProcessing;
    private bool _exitRequested;

    public ThumbnailRevitWorker(string jobPath)
    {
        _jobPath = jobPath;
        _cancelPath = $"{jobPath}.cancel";
    }

    public static bool TryGetJobPath(out string jobPath)
    {
        jobPath = Environment.GetEnvironmentVariable(
            ThumbnailWorkerJobService.JobEnvironmentVariable) ?? string.Empty;
        return !string.IsNullOrWhiteSpace(jobPath) && File.Exists(jobPath);
    }

    public static void HideWindow(IntPtr windowHandle)
    {
        if (windowHandle != IntPtr.Zero)
        {
            ShowWindow(windowHandle, HideWindowCommand);
        }
    }

    public void OnIdling(object? sender, IdlingEventArgs e)
    {
        if (_isProcessing || _exitRequested || sender is not UIApplication uiApplication)
        {
            return;
        }

        _isProcessing = true;
        try
        {
            ThumbnailWorkerJob? job = ThumbnailWorkerJobService.TryLoad(_jobPath);
            if (job is null)
            {
                RequestExit(uiApplication);
                return;
            }

            job.WorkerProcessId = RuntimeCompatibility.ProcessId;
            if (File.Exists(_cancelPath))
            {
                job.State = "Cancelled";
                ThumbnailWorkerJobService.Save(_jobPath, job);
                RequestExit(uiApplication);
                return;
            }

            ThumbnailWorkerJobItem? pendingItem = job.Items.FirstOrDefault(item => item.State == "Pending");
            if (pendingItem is null)
            {
                job.State = "Completed";
                ThumbnailWorkerJobService.Save(_jobPath, job);
                RequestExit(uiApplication);
                return;
            }

            job.State = "Running";
            pendingItem.State = "Running";
            ThumbnailWorkerJobService.Save(_jobPath, job);
            ProcessItem(uiApplication, pendingItem);
            ThumbnailWorkerJobService.Save(_jobPath, job);
            e.SetRaiseWithoutDelay();
        }
        catch (Exception exception)
        {
            ThumbnailWorkerJob? job = ThumbnailWorkerJobService.TryLoad(_jobPath);
            if (job is not null)
            {
                job.State = "Failed";
                job.Error = exception.Message;
                ThumbnailWorkerJobService.Save(_jobPath, job);
            }

            RequestExit(uiApplication);
        }
        finally
        {
            _isProcessing = false;
        }
    }

    private void ProcessItem(UIApplication uiApplication, ThumbnailWorkerJobItem jobItem)
    {
        var file = new FileInfo(jobItem.FullPath);
        if (!file.Exists)
        {
            jobItem.State = "Failed";
            jobItem.Error = "Family file is unavailable.";
            return;
        }

        var libraryItem = new LibraryItem(file, file.DirectoryName!);
        FamilyCacheRecord? record = null;
        var handler = new ThumbnailExternalEventHandler();
        handler.ThumbnailCreated += (_, eventArgs) => record = eventArgs.Record;
        handler.RequestThumbnail(libraryItem, jobItem.ThumbnailPath);
        handler.Execute(uiApplication);

        if (record is null)
        {
            jobItem.State = "Failed";
            jobItem.Error = "The worker did not return a thumbnail result.";
            return;
        }

        jobItem.Category = record.Category;
        jobItem.TypeNames = record.TypeNames;
        jobItem.Error = record.Status;
        jobItem.State = string.IsNullOrWhiteSpace(record.Status) ? "Completed" : "Failed";
        try
        {
            _cacheService.Store(record);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            if (jobItem.State == "Completed")
            {
                jobItem.Error = $"Thumbnail created; metadata cache failed: {exception.Message}";
            }
        }
    }

    private void RequestExit(UIApplication uiApplication)
    {
        if (_exitRequested) return;
        _exitRequested = true;
        HideWindow(Process.GetCurrentProcess().MainWindowHandle);
        RevitCommandId exitCommand = RevitCommandId.LookupCommandId("ID_APP_EXIT");
        if (uiApplication.CanPostCommand(exitCommand))
        {
            uiApplication.PostCommand(exitCommand);
        }
        else
        {
            Process.GetCurrentProcess().CloseMainWindow();
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr windowHandle, int command);
}
