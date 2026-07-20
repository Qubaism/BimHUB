using Autodesk.Revit.DB;

namespace QBTools.BimHUB.Services;

public static class RevitSourceDocumentService
{
    public static Document Open(Autodesk.Revit.ApplicationServices.Application application, string sourcePath, bool isWorkshared)
    {
        if (!isWorkshared)
        {
            return application.OpenDocumentFile(sourcePath);
        }

        var openOptions = new OpenOptions
        {
            DetachFromCentralOption = DetachFromCentralOption.DetachAndPreserveWorksets
        };
        // Detail libraries must be opened with their content available. Closing all
        // worksets can make otherwise valid drafting content appear empty.
        var worksetConfiguration = new WorksetConfiguration(WorksetConfigurationOption.OpenAllWorksets);
        openOptions.SetOpenWorksetsConfiguration(worksetConfiguration);
        ModelPath modelPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(sourcePath);
        return application.OpenDocumentFile(modelPath, openOptions);
    }
}
