using Umbraco.Community.SearchManager.Core.Explore;

namespace Umbraco.Community.SearchManager.Core.Tests.Explore;

public class NativeQueryLinterTests
{
    /// <summary>A query, then each warning as Code|Start|Length|Replacement ("" for no fix).</summary>
    public static TheoryData<string, string[]> Cases =>
        new()
        {
            { "level:3", ["needs_range|6|1|[3 TO 3]"] },
            { "+umbraco +level:3", ["needs_range|16|1|[3 TO 3]"] },
            { "updateDate:2026-01-01", ["needs_range|11|10|[2026-01-01 TO 2026-01-02}"] },
            { "updateDate:639080063400000000", ["needs_range|11|18|[2026-03-01 TO 2026-03-02}"] },
            { "level:three", ["needs_range|6|5|"] },
            { "umbraco and guide", ["lowercase_operator|8|3|AND"] },
            { "(a or b) not c", ["lowercase_operator|3|2|OR", "lowercase_operator|9|3|NOT"] },
            { "colour:red", ["unknown_field|0|6|"] },
            { "type:product", ["unknown_field|0|4|__NodeTypeAlias"] },
            { "*mber", ["leading_wildcard|0|5|mber"] },
            { "nodeName:*mber", ["leading_wildcard|9|5|mber"] },
            { "level:[3 TO 3]", [] },
            { "level:{3 TO *]", [] },
            { "updateDate:[2026-01-01 TO *]", [] },
            { "*:*", [] },
            { "nodeName:[* TO *]", [] },
            { "+__NodeTypeAlias:product +nodeName:umb*", [] },
            { "umbraco AND guide", [] },
            { "android notes", [] },
        };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Lint_Query_GivesExpectedWarnings(string query, string[] expected)
    {
        // Act
        IReadOnlyList<NativeWarning> warnings = NativeQueryLinter.Lint(query, TestSchema.Content);

        // Assert
        Assert.Equal(
            expected,
            warnings.Select(w => $"{w.Code}|{w.Start}|{w.Length}|{w.Replacement}")
        );
    }

    [Fact]
    public void Lint_Fix_ReplacesTheWarnedText()
    {
        // Arrange
        const string query = "+umbraco +level:3";
        NativeWarning warning = Assert.Single(NativeQueryLinter.Lint(query, TestSchema.Content));

        // Act
        string fixedQuery = string.Concat(
            query.AsSpan(0, warning.Start),
            warning.Replacement,
            query.AsSpan(warning.Start + warning.Length)
        );

        // Assert
        Assert.Equal("+umbraco +level:[3 TO 3]", fixedQuery);
    }
}
