using QBTools.BimHUB.Services;

namespace QBTools.BimHUB.Tests;

public sealed class DetailCategoryServiceTests
{
    [Fact]
    public void NormalizeRemovesDuplicatesAndUncategorizedWhenRealCategoriesExist()
    {
        IReadOnlyList<string> result = DetailCategoryService.Normalize(
            ["Uncategorized", "Doors", " doors ", "Partitions"]);

        Assert.Equal(["Doors", "Partitions"], result);
    }

    [Fact]
    public void NormalizeUsesUncategorizedWhenNoCategoryExists()
    {
        IReadOnlyList<string> result = DetailCategoryService.Normalize([null, "", "  "]);

        Assert.Equal([DetailCategoryService.Uncategorized], result);
    }

    [Fact]
    public void BuildDisplayTextCompactsMoreThanTwoCategories()
    {
        string result = DetailCategoryService.BuildDisplayText(["Walls", "Doors", "Furniture"]);

        Assert.Equal("Doors, Furniture +1", result);
    }

    [Fact]
    public void MatchesUsesEveryCategoryAlias()
    {
        bool result = DetailCategoryService.Matches(["Doors", "Partitions"], "partitions");

        Assert.True(result);
    }
}
