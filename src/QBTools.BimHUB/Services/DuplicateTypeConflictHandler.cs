using Autodesk.Revit.DB;

namespace QBTools.BimHUB.Services;

public sealed class DuplicateTypeConflictHandler : IDuplicateTypeNamesHandler
{
    private bool _conflictDetected;

    public bool ConflictDetected => _conflictDetected;

    public DuplicateTypeAction OnDuplicateTypeNamesFound(DuplicateTypeNamesHandlerArgs args)
    {
        _conflictDetected = true;
        return DuplicateTypeAction.UseDestinationTypes;
    }

    public string GetUserMessage()
    {
        return _conflictDetected
            ? " Matching type names were found; their existing active-project definitions were used."
            : string.Empty;
    }
}
