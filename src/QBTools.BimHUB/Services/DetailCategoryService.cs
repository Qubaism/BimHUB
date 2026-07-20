namespace QBTools.BimHUB.Services;

public static class DetailCategoryService
{
    public const string Uncategorized = "Uncategorized";

    public static IReadOnlyList<string> Normalize(IEnumerable<string?> categories)
    {
        List<string> normalized = categories
            .Where(category => !string.IsNullOrWhiteSpace(category))
            .Select(category => category!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(category => category, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (normalized.Any(category => !string.Equals(category, Uncategorized, StringComparison.OrdinalIgnoreCase)))
        {
            normalized.RemoveAll(category => string.Equals(category, Uncategorized, StringComparison.OrdinalIgnoreCase));
        }

        return normalized.Count > 0 ? normalized : [Uncategorized];
    }

    public static string BuildDisplayText(IEnumerable<string?> categories)
    {
        IReadOnlyList<string> normalized = Normalize(categories);
        return normalized.Count <= 2
            ? string.Join(", ", normalized)
            : $"{normalized[0]}, {normalized[1]} +{normalized.Count - 2}";
    }

    public static bool Matches(IEnumerable<string?> categories, string selectedCategory)
    {
        return Normalize(categories).Contains(selectedCategory, StringComparer.OrdinalIgnoreCase);
    }
}
