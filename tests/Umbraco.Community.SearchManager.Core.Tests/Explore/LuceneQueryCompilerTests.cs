using Umbraco.Community.SearchManager.Core.Explore;

namespace Umbraco.Community.SearchManager.Core.Tests.Explore;

public class LuceneQueryCompilerTests
{
    private static readonly DateOnly Today = new(2026, 10, 12);

    /// <summary>A name for the case, the chips (Kind|Field|Op|Value|Exclude) and the Lucene they compile to.</summary>
    public static TheoryData<string, string[], string> Cases =>
        new()
        {
            { "no chips", [], "*:*" },
            { "one word", ["Text||Contains|umbraco|False"], "+umbraco" },
            { "two words", ["Text||Contains|umbraco guide|False"], "+(+umbraco +guide)" },
            { "word with prefix wildcard", ["Text||Contains|umbr*|False"], "+umbr*" },
            { "special characters escaped", ["Text||Contains|c#(x)|False"], "+c#\\(x\\)" },
            { "operator word quoted", ["Text||Contains|OR|False"], "+\"OR\"" },
            { "phrase", ["Text||Phrase|getting started|False"], "+\"getting started\"" },
            { "excluded word alone", ["Text||Contains|draft|True"], "*:* -draft" },
            { "keyword", ["Field|__NodeTypeAlias|Is|product|False"], "+__NodeTypeAlias:product" },
            { "keyword by alias", ["Field|type|Is|product|False"], "+__NodeTypeAlias:product" },
            {
                "keyword with a space",
                ["Field|__NodeTypeAlias|Is|two words|False"],
                "+__NodeTypeAlias:\"two words\""
            },
            {
                "keyword starts with",
                ["Field|__NodeTypeAlias|StartsWith|prod|False"],
                "+__NodeTypeAlias:prod*"
            },
            {
                "two include keywords on one field OR together",
                ["Field|__NodeTypeAlias|Is|product|False", "Field|type|Is|blogPost|False"],
                "+(__NodeTypeAlias:product __NodeTypeAlias:blogPost)"
            },
            {
                "include and exclude on one field stay apart",
                ["Field|__NodeTypeAlias|Is|product|False", "Field|type|Is|blogPost|True"],
                "+__NodeTypeAlias:product -__NodeTypeAlias:blogPost"
            },
            { "number equals is a range", ["Field|level|Equals|3|False"], "+level:[3 TO 3]" },
            { "number greater than", ["Field|level|GreaterThan|3|False"], "+level:{3 TO *]" },
            { "number at least", ["Field|level|AtLeast|3|False"], "+level:[3 TO *]" },
            { "number less than", ["Field|level|LessThan|3|False"], "+level:[* TO 3}" },
            { "number at most", ["Field|level|AtMost|3|False"], "+level:[* TO 3]" },
            { "decimal", ["Field|price|AtLeast|9.5|False"], "+price:[9.5 TO *]" },
            {
                "range bounds AND",
                ["Field|level|AtLeast|2|False", "Field|level|AtMost|4|False"],
                "+level:[2 TO *] +level:[* TO 4]"
            },
            {
                "date on is the whole day",
                ["Field|updateDate|On|2026-01-01|False"],
                "+updateDate:[2026-01-01 TO 2026-01-02}"
            },
            {
                "date after",
                ["Field|updated|After|2026-01-01|False"],
                "+updateDate:[2026-01-01 TO *]"
            },
            {
                "date before",
                ["Field|updateDate|Before|2026-01-01|False"],
                "+updateDate:[* TO 2026-01-01}"
            },
            {
                "relative date",
                ["Field|updateDate|After|-7d|False"],
                "+updateDate:[2026-10-05 TO *]"
            },
            {
                "today",
                ["Field|updateDate|On|today|False"],
                "+updateDate:[2026-10-12 TO 2026-10-13}"
            },
            { "text contains", ["Field|nodeName|Contains|umbraco|False"], "+nodeName:umbraco" },
            {
                "text contains two words",
                ["Field|nodeName|Contains|umbraco guide|False"],
                "+nodeName:(+umbraco +guide)"
            },
            {
                "text phrase",
                ["Field|nodeName|Phrase|getting started|False"],
                "+nodeName:\"getting started\""
            },
            { "text starts with", ["Field|nodeName|StartsWith|umb|False"], "+nodeName:umb*" },
            { "text fuzzy", ["Field|nodeName|Fuzzy|umbraco|False"], "+nodeName:umbraco~" },
            { "missing", ["Field|nodeName|Missing||False"], "*:* -nodeName:[* TO *]" },
            { "has a value", ["Field|nodeName|Missing||True"], "+nodeName:[* TO *]" },
            { "raw lucene", ["Lucene||Contains|a OR b|False"], "+(a OR b)" },
            { "excluded raw lucene", ["Lucene||Contains|a OR b|True"], "*:* -(a OR b)" },
            {
                "mixed",
                [
                    "Text||Contains|umbraco|False",
                    "Field|type|Is|product|False",
                    "Field|level|Equals|3|True",
                ],
                "+umbraco +__NodeTypeAlias:product -level:[3 TO 3]"
            },
        };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Compile_Chips_GiveExpectedLucene(string name, string[] chips, string expected)
    {
        // Act
        LuceneCompilation result = LuceneQueryCompiler.Compile(
            [.. chips.Select(Parse)],
            TestSchema.Content,
            culture: null,
            Today
        );

        // Assert
        Assert.True(expected == result.Query, $"{name}: expected {expected}, got {result.Query}");
    }

    [Theory]
    [InlineData("en-US", "+nodeName_en-us:umbraco")]
    [InlineData("da-DK", "+nodeName_da-dk:umbraco")]
    [InlineData("fr-FR", "+nodeName:umbraco")]
    public void Compile_Culture_QueriesTheCultureVariantWhenItExists(
        string culture,
        string expected
    )
    {
        // Act
        LuceneCompilation result = LuceneQueryCompiler.Compile(
            [Parse("Field|name|Contains|umbraco|False")],
            TestSchema.Content,
            culture,
            Today
        );

        // Assert
        Assert.Equal(expected, result.Query);
    }

    /// <summary>A name for the case, one chip and the start of the reason it is skipped for.</summary>
    public static TheoryData<string, string, string> SkippedCases =>
        new()
        {
            { "unknown field", "Field|colour|Is|red|False", "There is no field \"colour\"" },
            { "alias of a missing field", "Field|ghost|Is|x|False", "There is no field \"ghost\"" },
            {
                "operator for another type",
                "Field|level|Contains|3|False",
                "Contains does not apply"
            },
            { "not a number", "Field|level|Equals|three|False", "\"three\" is not a number" },
            { "not a date", "Field|updateDate|On|soon|False", "\"soon\" is not a date" },
            { "empty value", "Field|nodeName|Contains| |False", "The filter has no value" },
            { "empty text", "Text||Contains||False", "There is no text" },
        };

    [Theory]
    [MemberData(nameof(SkippedCases))]
    public void Compile_FilterTheIndexCannotRun_IsSkippedWithReason(
        string name,
        string chip,
        string reason
    )
    {
        // Act
        LuceneCompilation result = LuceneQueryCompiler.Compile(
            [Parse("Text||Contains|kept|False"), Parse(chip)],
            TestSchema.Content,
            culture: null,
            Today
        );

        // Assert
        Assert.Multiple(
            () => Assert.Equal("+kept", result.Query),
            () =>
            {
                SkippedFilter skipped = Assert.Single(result.Skipped);
                Assert.Equal(1, skipped.Index);
                Assert.True(
                    skipped.Reason.StartsWith(reason, StringComparison.Ordinal),
                    $"{name}: {skipped.Reason}"
                );
            }
        );
    }

    [Fact]
    public void Compile_RawLuceneWhenNotAllowed_IsSkipped()
    {
        // Act
        LuceneCompilation result = LuceneQueryCompiler.Compile(
            [Parse("Lucene||Contains|a OR b|False")],
            TestSchema.Content,
            culture: null,
            Today,
            allowLucene: false
        );

        // Assert
        Assert.Equal(("*:*", 1), (result.Query, result.Skipped.Count));
    }

    [Theory]
    [InlineData("Text||Contains|umbraco|False", true)]
    [InlineData("Field|nodeName|Contains|umbraco|False", true)]
    [InlineData("Field|type|Is|product|False", false)]
    [InlineData("Field|level|Equals|3|False", false)]
    public void Compile_HasScoringClause_OnlyForTextClauses(string chip, bool scoring)
    {
        // Act
        LuceneCompilation result = LuceneQueryCompiler.Compile(
            [Parse(chip)],
            TestSchema.Content,
            culture: null,
            Today
        );

        // Assert
        Assert.Equal(scoring, result.HasScoringClause);
    }

    [Theory]
    [InlineData("a+b", "a\\+b")]
    [InlineData("path:/x", "path\\:\\/x")]
    [InlineData("two words", "two\\ words")]
    [InlineData("[1 TO 2]", "\\[1\\ TO\\ 2\\]")]
    public void Escape_SpecialCharacters_AreBackslashed(string value, string expected)
    {
        // Act
        string escaped = LuceneQueryCompiler.Escape(value);

        // Assert
        Assert.Equal(expected, escaped);
    }

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
