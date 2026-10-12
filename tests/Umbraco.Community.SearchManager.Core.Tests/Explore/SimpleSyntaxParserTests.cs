using Umbraco.Community.SearchManager.Core.Explore;

namespace Umbraco.Community.SearchManager.Core.Tests.Explore;

public class SimpleSyntaxParserTests
{
    /// <summary>Input, then the chips it should become, written as Kind|Field|Op|Value|Exclude.</summary>
    public static TheoryData<string, string[]> Cases =>
        new()
        {
            { "umbraco", ["Text||Contains|umbraco|False"] },
            { "umbraco guide", ["Text||Contains|umbraco guide|False"] },
            { "\"getting started\"", ["Text||Phrase|getting started|False"] },
            { "-\"getting started\"", ["Text||Phrase|getting started|True"] },
            { "-draft", ["Text||Contains|draft|True"] },
            { "type:product", ["Field|__NodeTypeAlias|Is|product|False"] },
            { "-type:product", ["Field|__NodeTypeAlias|Is|product|True"] },
            { "TYPE:product", ["Field|__NodeTypeAlias|Is|product|False"] },
            { "__NodeTypeAlias:blogPost", ["Field|__NodeTypeAlias|Is|blogPost|False"] },
            { "type:prod*", ["Field|__NodeTypeAlias|StartsWith|prod|False"] },
            { "level:3", ["Field|level|Equals|3|False"] },
            { "level:>3", ["Field|level|GreaterThan|3|False"] },
            { "level:>=3", ["Field|level|AtLeast|3|False"] },
            { "level:<3", ["Field|level|LessThan|3|False"] },
            { "level:<=3", ["Field|level|AtMost|3|False"] },
            { "updated:2026-01-01", ["Field|updateDate|On|2026-01-01|False"] },
            { "updated:>=2026-01-01", ["Field|updateDate|After|2026-01-01|False"] },
            { "updated:>2026-01-01", ["Field|updateDate|After|2026-01-02|False"] },
            { "updated:<2026-01-01", ["Field|updateDate|Before|2026-01-01|False"] },
            { "updated:<=2026-01-31", ["Field|updateDate|Before|2026-02-01|False"] },
            { "updated:>-7d", ["Field|updateDate|After|-7d|False"] },
            { "name:umbraco", ["Field|nodeName|Contains|umbraco|False"] },
            { "name:\"getting started\"", ["Field|nodeName|Phrase|getting started|False"] },
            { "name:umb*", ["Field|nodeName|StartsWith|umb|False"] },
            { "name:umbraco~", ["Field|nodeName|Fuzzy|umbraco|False"] },
            { "name:umbraco~1", ["Field|nodeName|Fuzzy|umbraco|False"] },
            { "name:*", ["Field|nodeName|Missing||True"] },
            { "-name:*", ["Field|nodeName|Missing||False"] },
            { "colour:red", ["Field|colour|Is|red|False"] },
            { "ghost:boo", ["Field|ghost|Is|boo|False"] },
            { "type:\"two words\"", ["Field|__NodeTypeAlias|Is|two words|False"] },
            { "name:", ["Text||Contains|name:|False"] },
            { "\"\"", [] },
            { "   ", [] },
            {
                "umbraco type:product level:>2 guide",
                [
                    "Text||Contains|umbraco guide|False",
                    "Field|__NodeTypeAlias|Is|product|False",
                    "Field|level|GreaterThan|2|False",
                ]
            },
            {
                "type:product umbraco",
                ["Field|__NodeTypeAlias|Is|product|False", "Text||Contains|umbraco|False"]
            },
        };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Parse_Input_BecomesExpectedChips(string input, string[] expected)
    {
        // Act
        ParseResult result = SimpleSyntaxParser.Parse(input, TestSchema.Content);

        // Assert
        Assert.Equal(expected, result.Filters.Select(Describe));
    }

    [Fact]
    public void Parse_UnbalancedQuote_SearchesWholeInputAsTextWithFallback()
    {
        // Act
        ParseResult result = SimpleSyntaxParser.Parse(
            "type:product \"getting st",
            TestSchema.Content
        );

        // Assert
        Assert.Multiple(
            () =>
                Assert.Equal(
                    ["Text||Contains|type:product getting st|False"],
                    result.Filters.Select(Describe)
                ),
            () => Assert.Equal(SimpleSyntaxParser.UnbalancedQuoteCode, result.Fallback?.Code)
        );
    }

    [Fact]
    public void Parse_ValidInput_HasNoFallback()
    {
        // Act
        ParseResult result = SimpleSyntaxParser.Parse("type:product", TestSchema.Content);

        // Assert
        Assert.Null(result.Fallback);
    }

    private static string Describe(Filter filter) =>
        $"{filter.Kind}|{filter.Field}|{filter.Op}|{filter.Value}|{filter.Exclude}";
}
