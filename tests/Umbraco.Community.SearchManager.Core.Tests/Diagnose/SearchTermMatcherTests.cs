using Umbraco.Community.SearchManager.Core.Diagnose;

namespace Umbraco.Community.SearchManager.Core.Tests.Diagnose;

public class SearchTermMatcherTests
{
    private static readonly AnalysisStage Final = new(
        "StopFilter",
        null,
        [
            Token("the", TokenState.Removed),
            Token("cafe", TokenState.Kept),
            Token("opening", TokenState.Kept),
            Token("2026", TokenState.Kept),
        ]
    );

    public static TheoryData<SearchTermPartKind, string, string, TokenMatch> Cases =>
        new()
        {
            { SearchTermPartKind.Word, "cafe", "cafe", TokenMatch.Exact },
            { SearchTermPartKind.Prefix, "open", "opening", TokenMatch.Prefix },
            { SearchTermPartKind.Prefix, "cafe", "cafe", TokenMatch.Exact },
            { SearchTermPartKind.Wildcard, "op?n*", "opening", TokenMatch.Pattern },
            { SearchTermPartKind.Phrase, "opening 2026", "2026", TokenMatch.Exact },
        };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Highlight_Part_MarksTheTokensItLooksFor(
        SearchTermPartKind kind,
        string text,
        string token,
        TokenMatch expected
    )
    {
        // Act
        AnalysisStage stage = SearchTermMatcher.Highlight(Final, [new(kind, text, true, 0)]);

        // Assert
        Assert.Equal(expected, stage.Tokens.Single(t => t.Text == token).Match);
    }

    [Theory]
    [InlineData(SearchTermPartKind.Fuzzy, "cafe")]
    [InlineData(SearchTermPartKind.Other, "cafe")]
    [InlineData(SearchTermPartKind.Word, "Cafe")]
    public void Highlight_FuzzyOtherOrDifferentCase_MarksNothing(
        SearchTermPartKind kind,
        string text
    )
    {
        // Act
        AnalysisStage stage = SearchTermMatcher.Highlight(Final, [new(kind, text, true, 0)]);

        // Assert
        Assert.All(stage.Tokens, token => Assert.Equal(TokenMatch.None, token.Match));
    }

    [Fact]
    public void Highlight_RemovedToken_IsNotMarked()
    {
        // Act
        AnalysisStage stage = SearchTermMatcher.Highlight(
            Final,
            [new(SearchTermPartKind.Word, "the", true, 0)]
        );

        // Assert
        Assert.Equal(TokenMatch.None, stage.Tokens.Single(t => t.Text == "the").Match);
    }

    private static AnalysisToken Token(string text, TokenState state) =>
        new(text, 0, 0, 0, state, TokenMatch.None);
}
