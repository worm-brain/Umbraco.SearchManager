using Umbraco.Community.SearchManager.Core.Explore;
using Umbraco.Community.SearchManager.UmbracoSearch.Explore;

namespace Umbraco.Community.SearchManager.UmbracoSearch.Tests.Explore;

public class UmbracoSearchCompilerTests
{
    private static readonly Guid BlogPost = new("8c5f6c9a-1d1b-4c43-9b0a-6f7bdbd0a1e2");
    private static readonly DateOnly Today = new(2026, 10, 12);

    private static readonly UmbracoSearchFields Fields = UmbracoSearchFields.FromPhysicalNames([
        "Field_title_texts",
        "Field_price_decimals",
        "Field_count_integers",
        "Field_colour_keywords",
    ]);

    public static TheoryData<string, string> Filters =>
        new()
        {
            {
                "Field|type|Is|blogPost|False",
                "new KeywordFilter(\"Umb_ContentTypeId\", [\"8c5f6c9a-1d1b-4c43-9b0a-6f7bdbd0a1e2\"], Negate: false) // blogPost"
            },
            {
                "Field|colour|Is|red|True",
                "new KeywordFilter(\"colour\", [\"red\"], Negate: true)"
            },
            {
                "Field|kind|Is|document|False",
                "new KeywordFilter(\"Umb_ObjectType\", [\"Document\"], Negate: false)"
            },
            {
                "Field|key|Is|8C5F6C9A-1D1B-4C43-9B0A-6F7BDBD0A1E2|False",
                "new KeywordFilter(\"Umb_Id\", [\"8c5f6c9a-1d1b-4c43-9b0a-6f7bdbd0a1e2\"], Negate: false)"
            },
            {
                "Field|title|Contains|summer|False",
                "new TextFilter(\"title\", [\"summer\"], Negate: false)"
            },
            {
                "Field|level|Equals|2|False",
                "new IntegerExactFilter(\"Umb_Level\", [2], Negate: false)"
            },
            {
                "Field|count|GreaterThan|2|False",
                "new IntegerRangeFilter(\"count\", [new IntegerRangeFilterRange(3, null)], Negate: false)"
            },
            {
                "Field|count|AtLeast|2|False",
                "new IntegerRangeFilter(\"count\", [new IntegerRangeFilterRange(2, null)], Negate: false)"
            },
            {
                "Field|count|LessThan|2|False",
                "new IntegerRangeFilter(\"count\", [new IntegerRangeFilterRange(null, 2)], Negate: false)"
            },
            {
                "Field|count|AtMost|2|True",
                "new IntegerRangeFilter(\"count\", [new IntegerRangeFilterRange(null, 3)], Negate: true)"
            },
            {
                "Field|price|Equals|9.5|False",
                "new DecimalExactFilter(\"price\", [9.5m], Negate: false)"
            },
            {
                "Field|price|AtLeast|9.5|False",
                "new DecimalRangeFilter(\"price\", [new DecimalRangeFilterRange(9.5m, null)], Negate: false)"
            },
            {
                "Field|updated|On|2026-09-01|False",
                "new DateTimeOffsetRangeFilter(\"Umb_UpdateDate\", [new DateTimeOffsetRangeFilterRange(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 9, 2, 0, 0, 0, TimeSpan.Zero))], Negate: false)"
            },
            {
                "Field|created|After|-7d|False",
                "new DateTimeOffsetRangeFilter(\"Umb_CreateDate\", [new DateTimeOffsetRangeFilterRange(new DateTimeOffset(2026, 10, 5, 0, 0, 0, TimeSpan.Zero), null)], Negate: false)"
            },
            {
                "Field|updated|Before|2026-09-01|True",
                "new DateTimeOffsetRangeFilter(\"Umb_UpdateDate\", [new DateTimeOffsetRangeFilterRange(null, new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero))], Negate: true)"
            },
        };

    [Theory]
    [MemberData(nameof(Filters))]
    public void Compile_FieldChip_CompilesToTheTypedFilter(string chip, string expected)
    {
        // Act
        UmbracoSearchCompilation compilation = Compile(Chip(chip));

        // Assert
        Assert.Equal(expected, Describe(Assert.Single(compilation.Filters)));
    }

    public static TheoryData<string, string> UnsupportedChips =>
        new()
        {
            { "Text||Phrase|summer sale|False", "cannot search for a phrase" },
            { "Text||Contains|summer|True", "cannot exclude free text" },
            { "Lucene||Contains|+title:x|False", "not raw Lucene" },
            { "Field|title|Phrase|summer sale|False", "only match words" },
            { "Field|title|StartsWith|sum|False", "only match words" },
            { "Field|title|Fuzzy|sumer|False", "only match words" },
            { "Field|colour|StartsWith|re|False", "only match whole values" },
            { "Field|level|Missing||False", "no \"is missing\" filter" },
            { "Field|updated|Missing||True", "no \"is missing\" filter" },
            { "Field|price|GreaterThan|9|False", "cannot run this comparison" },
            { "Field|price|AtMost|9|False", "cannot run this comparison" },
            { "Field|level|Equals|2.5|False", "not a whole number" },
            { "Field|updated|On|soon|False", "not a date" },
            { "Field|type|Is|nope|False", "no document, media or member type \"nope\"" },
            { "Field|nope|Is|x|False", "no field \"nope\"" },
        };

    [Theory]
    [MemberData(nameof(UnsupportedChips))]
    public void Compile_ChipUmbracoSearchCannotRun_IsSkippedWithTheReason(
        string chip,
        string reason
    )
    {
        // Act
        UmbracoSearchCompilation compilation = Compile(Chip(chip));

        // Assert
        Assert.Contains(
            reason,
            Assert.Single(compilation.Skipped).Reason,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void Compile_FreeTextChips_JoinIntoTheQuery()
    {
        // Act
        UmbracoSearchCompilation compilation = Compile(
            Chip("Text||Contains|summer|False"),
            Chip("Text||Contains| sale |False")
        );

        // Assert
        Assert.Equal("summer sale", compilation.Query);
    }

    [Fact]
    public void Compile_IncludeChipsOnOneField_OrTogetherInOneFilter()
    {
        // Act
        UmbracoSearchCompilation compilation = Compile(
            Chip("Field|colour|Is|red|False"),
            Chip("Field|colour|Is|blue|False")
        );

        // Assert
        Assert.Equal(
            "new KeywordFilter(\"colour\", [\"red\", \"blue\"], Negate: false)",
            Describe(Assert.Single(compilation.Filters))
        );
    }

    [Fact]
    public void Compile_TwoRangesOnOneField_StayTwoFilters()
    {
        // Act
        UmbracoSearchCompilation compilation = Compile(
            Chip("Field|count|AtLeast|2|False"),
            Chip("Field|count|AtMost|4|False")
        );

        // Assert
        Assert.Equal(2, compilation.Filters.Count);
    }

    [Fact]
    public void Compile_SkippedChip_KeepsItsPositionInTheList()
    {
        // Act
        UmbracoSearchCompilation compilation = Compile(
            Chip("Text||Contains|summer|False"),
            Chip("Field|level|Missing||False")
        );

        // Assert
        Assert.Equal(1, Assert.Single(compilation.Skipped).Index);
    }

    [Fact]
    public void Compile_NoChips_SendsNoQueryAndNoFilters()
    {
        // Act
        UmbracoSearchCompilation compilation = Compile();

        // Assert
        Assert.Equal((null, 0), (compilation.Query, compilation.Filters.Count));
    }

    private static UmbracoSearchCompilation Compile(params Filter[] filters) =>
        UmbracoSearchCompiler.Compile(
            filters,
            Fields,
            Today,
            alias => alias == "blogPost" ? [BlogPost] : []
        );

    private static string Describe(CompiledFilter filter) =>
        UmbracoSearchCSharp.Write(filter.Filter)
        + (filter.Comment is null ? string.Empty : $" // {filter.Comment}");

    private static Filter Chip(string chip)
    {
        string[] parts = chip.Split('|');
        return new Filter(
            Enum.Parse<FilterKind>(parts[0]),
            parts[1].Length == 0 ? null : parts[1],
            Enum.Parse<FilterOperator>(parts[2]),
            parts[3],
            bool.Parse(parts[4])
        );
    }
}
