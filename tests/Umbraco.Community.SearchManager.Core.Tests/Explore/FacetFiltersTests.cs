using Umbraco.Community.SearchManager.Core.Explore;

namespace Umbraco.Community.SearchManager.Core.Tests.Explore;

public class FacetFiltersTests
{
    private static readonly Filter Word = new(
        FilterKind.Text,
        null,
        FilterOperator.Contains,
        "umbraco"
    );
    private static readonly Filter TypeBlog = new(
        FilterKind.Field,
        "type",
        FilterOperator.Is,
        "blogPost"
    );
    private static readonly Filter NotProduct = new(
        FilterKind.Field,
        "__NodeTypeAlias",
        FilterOperator.Is,
        "product",
        Exclude: true
    );

    [Fact]
    public void Without_IncludeChipOnTheField_IsDropped()
    {
        // Act
        IReadOnlyList<Filter> kept = FacetFilters.Without(
            [Word, TypeBlog],
            "__NodeTypeAlias",
            TestSchema.Content,
            null
        );

        // Assert
        Assert.Equal([Word], kept);
    }

    [Fact]
    public void Without_ExcludeChipOnTheField_IsKept()
    {
        // Act
        IReadOnlyList<Filter> kept = FacetFilters.Without(
            [TypeBlog, NotProduct],
            "__NodeTypeAlias",
            TestSchema.Content,
            null
        );

        // Assert
        Assert.Equal([NotProduct], kept);
    }

    [Fact]
    public void Without_MissingChipOnTheField_IsKept()
    {
        // Arrange: "is missing" prohibits every value, so it is an exclusion too.
        var missing = new Filter(FilterKind.Field, "type", FilterOperator.Missing, "");

        // Act
        IReadOnlyList<Filter> kept = FacetFilters.Without(
            [missing],
            "__NodeTypeAlias",
            TestSchema.Content,
            null
        );

        // Assert
        Assert.Equal([missing], kept);
    }

    [Fact]
    public void Without_ChipOnTheCultureVariant_IsDroppedForThatCulture()
    {
        // Arrange
        var name = new Filter(FilterKind.Field, "name", FilterOperator.Contains, "home");

        // Act
        IReadOnlyList<Filter> kept = FacetFilters.Without(
            [name],
            "nodeName_da-dk",
            TestSchema.Content,
            "da-DK"
        );

        // Assert
        Assert.Empty(kept);
    }

    [Fact]
    public void Without_ChipOnAnotherField_IsKept()
    {
        // Act
        IReadOnlyList<Filter> kept = FacetFilters.Without(
            [TypeBlog],
            "__Published",
            TestSchema.Content,
            null
        );

        // Assert
        Assert.Equal([TypeBlog], kept);
    }

    [Fact]
    public void Without_UnknownChipField_IsKept()
    {
        // Arrange
        var unknown = new Filter(FilterKind.Field, "colour", FilterOperator.Is, "red");

        // Act
        IReadOnlyList<Filter> kept = FacetFilters.Without(
            [unknown],
            "__NodeTypeAlias",
            TestSchema.Content,
            null
        );

        // Assert
        Assert.Equal([unknown], kept);
    }
}
