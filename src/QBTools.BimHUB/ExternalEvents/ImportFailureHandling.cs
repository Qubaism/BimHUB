using Autodesk.Revit.DB;

namespace QBTools.BimHUB.ExternalEvents;

internal static class ImportFailureHandling
{
    public static void ApplyQuietRollback(Transaction transaction)
    {
        FailureHandlingOptions options = transaction.GetFailureHandlingOptions();
        options.SetFailuresPreprocessor(QuietPasteFailures.Instance);
        options.SetClearAfterRollback(true);
        options.SetForcedModalHandling(false);
        transaction.SetFailureHandlingOptions(options);
    }

    private sealed class QuietPasteFailures : IFailuresPreprocessor
    {
        public static readonly QuietPasteFailures Instance = new();

        public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
        {
            IList<FailureMessageAccessor> messages = failuresAccessor.GetFailureMessages();
            if (messages.Count == 0) return FailureProcessingResult.Continue;

            bool hasError = false;
            foreach (FailureMessageAccessor message in messages)
            {
                if (message.GetSeverity() == FailureSeverity.Warning)
                {
                    failuresAccessor.DeleteWarning(message);
                }
                else if (message.GetSeverity() == FailureSeverity.Error)
                {
                    hasError = true;
                }
            }

            return hasError
                ? FailureProcessingResult.ProceedWithRollBack
                : FailureProcessingResult.Continue;
        }
    }
}
