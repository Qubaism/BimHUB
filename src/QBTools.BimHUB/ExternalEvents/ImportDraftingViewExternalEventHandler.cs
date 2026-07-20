using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using QBTools.BimHUB.Models;
using QBTools.BimHUB.Services;
using System.Collections.Concurrent;
using System.IO;

namespace QBTools.BimHUB.ExternalEvents;

public sealed class ImportDraftingViewExternalEventHandler : IExternalEventHandler
{
    private readonly ConcurrentQueue<DraftingViewItem> _pendingItems = new();

    public event EventHandler<DraftingViewImportCompletedEventArgs>? ImportCompleted;

    public bool HasPendingRequests => !_pendingItems.IsEmpty;

    public void RequestImport(DraftingViewItem item)
    {
        _pendingItems.Enqueue(item);
    }

    public void Execute(UIApplication application)
    {
        if (!_pendingItems.TryDequeue(out DraftingViewItem? item)) return;

        UIDocument? targetUiDocument = application.ActiveUIDocument;
        if (targetUiDocument is null || targetUiDocument.Document.IsFamilyDocument)
        {
            Complete(item, false, "Open a Revit project before importing a drafting view.");
            return;
        }

        Document? sourceDocument = null;
        try
        {
            using BasicFileInfo info = BasicFileInfo.Extract(item.SourceProjectPath);
            if (!string.Equals(info.Format, application.Application.VersionNumber, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"The source requires Revit {info.Format}.");
            }

            sourceDocument = RevitSourceDocumentService.Open(
                application.Application,
                item.SourceProjectPath,
                info.IsWorkshared);
            ViewDrafting? sourceView = sourceDocument.GetElement(item.ViewUniqueId) as ViewDrafting;
            sourceView ??= new FilteredElementCollector(sourceDocument)
                .OfClass(typeof(ViewDrafting))
                .Cast<ViewDrafting>()
                .FirstOrDefault(view => !view.IsTemplate
                    && string.Equals(view.Name, item.Name, StringComparison.OrdinalIgnoreCase));
            if (sourceView is null)
            {
                throw new InvalidOperationException("The cached drafting view is no longer available. Refresh the library.");
            }

            Document targetDocument = targetUiDocument.Document;
            string destinationName = GetUniqueViewName(targetDocument, sourceView.Name);
            ViewDrafting destinationView;
            using (var transaction = new Transaction(targetDocument, "Import drafting view from BimHUB"))
            {
                transaction.Start();
                var conflictHandler = new DuplicateTypeConflictHandler();
                var copyOptions = new CopyPasteOptions();
                copyOptions.SetDuplicateTypeNamesHandler(conflictHandler);
                destinationView = DraftingViewCopyService.Copy(
                    sourceDocument,
                    sourceView,
                    targetDocument,
                    destinationName,
                    copyOptions);
                transaction.Commit();
                destinationName += conflictHandler.GetUserMessage();
            }

            targetUiDocument.ActiveView = destinationView;
            Complete(item, true, $"Imported {destinationName}");
        }
        catch (Exception exception)
        {
            Complete(item, false, exception.Message);
        }
        finally
        {
            sourceDocument?.Close(false);
        }
    }

    public string GetName() => "Import a same-version drafting view from BimHUB";

    private static string GetUniqueViewName(Document document, string requestedName)
    {
        var names = new HashSet<string>(
            new FilteredElementCollector(document).OfClass(typeof(View)).Cast<View>().Select(view => view.Name),
            StringComparer.OrdinalIgnoreCase);
        if (!names.Contains(requestedName)) return requestedName;

        int suffix = 2;
        while (names.Contains($"{requestedName}_{suffix}")) suffix++;
        return $"{requestedName}_{suffix}";
    }

    private void Complete(DraftingViewItem item, bool succeeded, string message)
    {
        ImportCompleted?.Invoke(this, new DraftingViewImportCompletedEventArgs(item, succeeded, message));
    }

}

public sealed record DraftingViewImportCompletedEventArgs(
    DraftingViewItem Item,
    bool Succeeded,
    string Message);
