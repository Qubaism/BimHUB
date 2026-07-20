using QBTools.BimHUB.Controls;

namespace QBTools.BimHUB.Tests;

public sealed class AdaptiveGridLayoutTests
{
    [Theory]
    [InlineData(112, 1)]
    [InlineData(231, 1)]
    [InlineData(232, 2)]
    [InlineData(351, 2)]
    [InlineData(352, 3)]
    public void CalculatesExpectedColumnCount(double width, int expectedColumns)
    {
        (int columns, double itemWidth) = AdaptiveGridLayout.CalculateColumns(width, 112, 8);

        Assert.Equal(expectedColumns, columns);
        Assert.True(itemWidth >= 112 || columns == 1);
    }
}
