using Umbraco.Community.SearchManager.Core.Explore;

namespace Umbraco.Community.SearchManager.Core.Tests.Explore;

public class ExamineCSharpTests
{
    private static readonly DateOnly Today = new(2026, 10, 12);

    [Fact]
    public void For_FieldFilters_WritesTheFluentQuery()
    {
        // Act
        CSharpQuery code = For(
            new Filter(FilterKind.Field, "type", FilterOperator.Is, "product"),
            new Filter(FilterKind.Field, "level", FilterOperator.AtLeast, "3"),
            new Filter(FilterKind.Field, "updated", FilterOperator.Before, "2026-01-01", true)
        );

        // Assert
        Assert.Equal(
            """
            var results = searcher.CreateQuery()
                .Field("__NodeTypeAlias", "product")
                .And().RangeQuery<int>(new[] { "level" }, 3, null)
                .Not().RangeQuery<DateTime>(new[] { "updateDate" }, null, new DateTime(2026, 1, 1), maxInclusive: false)
                .Execute(QueryOptions.SkipTake(0, 20));
            """.ReplaceLineEndings("\n"),
            code.Code
        );
    }

    [Fact]
    public void For_IncludeChipsOnOneField_WritesGroupedOr()
    {
        // Act
        CSharpQuery code = For(
            new Filter(FilterKind.Field, "type", FilterOperator.Is, "product"),
            new Filter(FilterKind.Field, "type", FilterOperator.Is, "blogPost")
        );

        // Assert
        Assert.Contains(
            ".GroupedOr(new[] { \"__NodeTypeAlias\" }, \"product\", \"blogPost\")",
            code.Code,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void For_FirstFilterExcluded_StartsFromAll()
    {
        // Act
        CSharpQuery code = For(
            new Filter(FilterKind.Field, "type", FilterOperator.Is, "product", true)
        );

        // Assert
        Assert.StartsWith(
            "var results = searcher.CreateQuery()\n    .All()\n    .Not().Field(",
            code.Code,
            StringComparison.Ordinal
        );
    }

    [Theory]
    [InlineData(FilterKind.Text, null, FilterOperator.Contains, "umbraco")]
    [InlineData(FilterKind.Lucene, null, FilterOperator.Contains, "a OR b")]
    [InlineData(FilterKind.Field, "nodeName", FilterOperator.Missing, "")]
    public void For_FilterWithoutFluentForm_FallsBackToNativeQuery(
        FilterKind kind,
        string? field,
        FilterOperator op,
        string value
    )
    {
        // Act
        CSharpQuery code = ExamineCSharp.For(
            [new Filter(kind, field, op, value)],
            TestSchema.Content,
            null,
            Today,
            "+\"quoted\" +a\\:b"
        );

        // Assert
        Assert.Equal(
            (
                false,
                "var results = searcher.CreateQuery()\n    .NativeQuery(\"+\\\"quoted\\\" +a\\\\:b\")\n    .Execute(QueryOptions.SkipTake(0, 20));"
            ),
            (code.Fluent, code.Code)
        );
    }

    private static CSharpQuery For(params Filter[] filters) =>
        ExamineCSharp.For(filters, TestSchema.Content, null, Today, "unused");
}
