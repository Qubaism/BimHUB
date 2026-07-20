namespace QBTools.BimHUB.Controls;

public static class AdaptiveGridLayout
{
    public static (int Columns, double ItemWidth) CalculateColumns(
        double availableWidth,
        double minimumItemWidth,
        double spacing)
    {
        double width = Math.Max(1, availableWidth);
        double minimumWidth = Math.Max(1, minimumItemWidth);
        double normalizedSpacing = Math.Max(0, spacing);
        int columns = Math.Max(
            1,
            (int)Math.Floor((width + normalizedSpacing) / (minimumWidth + normalizedSpacing)));
        double itemWidth = Math.Max(
            1,
            (width - normalizedSpacing * (columns - 1)) / columns);
        return (columns, itemWidth);
    }
}
