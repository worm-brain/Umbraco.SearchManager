using Umbraco.Community.SearchManager.Core.Explore;

namespace Umbraco.Community.SearchManager.Core.Tests.Explore;

public class LuceneToChipsTests
{
    /// <summary>Lucene, then the chips as Kind|Field|Op|Value|Exclude.</summary>
    public static TheoryData<string, string[]> Cases =>
        new()
        {
            { "", [] },
            { "*:*", [] },
            { "umbraco", ["Text||Contains|umbraco|False"] },
            { "+umbraco", ["Text||Contains|umbraco|False"] },
            { "+(+umbraco +guide)", ["Text||Contains|umbraco guide|False"] },
            { "+\"getting started\"", ["Text||Phrase|getting started|False"] },
            { "*:* -draft", ["Text||Contains|draft|True"] },
            { "+__NodeTypeAlias:product", ["Field|__NodeTypeAlias|Is|product|False"] },
            { "+__NodeTypeAlias:\"two words\"", ["Field|__NodeTypeAlias|Is|two words|False"] },
            { "+__NodeTypeAlias:prod*", ["Field|__NodeTypeAlias|StartsWith|prod|False"] },
            {
                "+(__NodeTypeAlias:product __NodeTypeAlias:blogPost)",
                [
                    "Field|__NodeTypeAlias|Is|product|False",
                    "Field|__NodeTypeAlias|Is|blogPost|False",
                ]
            },
            { "+level:[3 TO 3]", ["Field|level|Equals|3|False"] },
            { "+level:{3 TO *]", ["Field|level|GreaterThan|3|False"] },
            { "+level:[3 TO *]", ["Field|level|AtLeast|3|False"] },
            { "+level:[* TO 3}", ["Field|level|LessThan|3|False"] },
            { "-level:[* TO 3]", ["Field|level|AtMost|3|True"] },
            { "+updateDate:[2026-01-01 TO 2026-01-02}", ["Field|updateDate|On|2026-01-01|False"] },
            { "+updateDate:[2026-01-01 TO *]", ["Field|updateDate|After|2026-01-01|False"] },
            { "+updateDate:[* TO 2026-01-01}", ["Field|updateDate|Before|2026-01-01|False"] },
            { "+nodeName:umbraco", ["Field|nodeName|Contains|umbraco|False"] },
            { "+nodeName:(+umbraco +guide)", ["Field|nodeName|Contains|umbraco guide|False"] },
            { "+nodeName:\"a b\"", ["Field|nodeName|Phrase|a b|False"] },
            { "+nodeName:umbr*", ["Field|nodeName|StartsWith|umbr|False"] },
            { "+nodeName:umbraco~", ["Field|nodeName|Fuzzy|umbraco|False"] },
            { "*:* -nodeName:[* TO *]", ["Field|nodeName|Missing||False"] },
            { "+nodeName:[* TO *]", ["Field|nodeName|Missing||True"] },
            {
                "+umbraco +level:[2 TO 4]",
                ["Text||Contains|umbraco|False", "Lucene||Contains|level:[2 TO 4]|False"]
            },
            { "+colour:red", ["Lucene||Contains|colour:red|False"] },
            { "+level:3", ["Lucene||Contains|level:3|False"] },
            { "a b", ["Lucene||Contains|a b|False"] },
            { "+a AND +b", ["Lucene||Contains|+a AND +b|False"] },
            { "nodeName:(a", ["Lucene||Contains|nodeName:(a|False"] },
        };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Read_Lucene_GivesExpectedChips(string lucene, string[] expected)
    {
        // Act
        IReadOnlyList<Filter> chips = LuceneToChips.Read(lucene, TestSchema.Content);

        // Assert
        Assert.Equal(expected, chips.Select(Describe));
    }

    /// <summary>Chips the compiler writes come back as the same chips.</summary>
    [Theory]
    [InlineData("Field|__NodeTypeAlias|Is|product|False", "Field|level|AtLeast|3|True")]
    [InlineData("Text||Contains|umbraco guide|False", "Field|updateDate|On|2026-01-01|False")]
    [InlineData("Field|nodeName|Missing||False", "Text||Phrase|a b|True")]
    public void Read_CompiledChips_RoundTrip(string first, string second)
    {
        // Arrange
        Filter[] chips = [Parse(first), Parse(second)];
        string lucene = LuceneQueryCompiler
            .Compile(chips, TestSchema.Content, null, new DateOnly(2026, 10, 12))
            .Query;

        // Act
        IReadOnlyList<Filter> read = LuceneToChips.Read(lucene, TestSchema.Content);

        // Assert
        Assert.Equal(chips.Select(Describe), read.Select(Describe));
    }

    private static string Describe(Filter filter) =>
        $"{filter.Kind}|{filter.Field}|{filter.Op}|{filter.Value}|{filter.Exclude}";

    private static Filter Parse(string chip)
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
