using Umbraco.Community.SearchManager.Core.Diagnose;

namespace Umbraco.Community.SearchManager.Core.Tests.Diagnose;

public class AnalysisStagesTests
{
    // "The Café" as StandardAnalyzer's stages emit it.
    private static readonly RawStage Tokenizer = new(
        "StandardTokenizer",
        null,
        [new RawToken("The", 1, 0, 3), new RawToken("Café", 1, 4, 8)]
    );

    private static readonly RawStage Lower = new(
        "LowerCaseFilter",
        null,
        [new RawToken("the", 1, 0, 3), new RawToken("café", 1, 4, 8)]
    );

    private static readonly RawStage Stop = new(
        "StopFilter",
        "removes 33 common English words",
        [new RawToken("café", 2, 4, 8)]
    );

    [Fact]
    public void Mark_FirstStage_KeepsEveryToken()
    {
        // Act
        IReadOnlyList<AnalysisStage> stages = AnalysisStages.Mark([Tokenizer]);

        // Assert
        Assert.All(stages[0].Tokens, token => Assert.Equal(TokenState.Kept, token.State));
    }

    [Fact]
    public void Mark_LowerCasedToken_IsChanged()
    {
        // Act
        IReadOnlyList<AnalysisStage> stages = AnalysisStages.Mark([Tokenizer, Lower]);

        // Assert
        Assert.Equal(
            [TokenState.Changed, TokenState.Changed],
            stages[1].Tokens.Select(token => token.State)
        );
    }

    [Fact]
    public void Mark_StopWord_IsRemovedInTheStageThatDroppedIt()
    {
        // Act
        IReadOnlyList<AnalysisStage> stages = AnalysisStages.Mark([Tokenizer, Lower, Stop]);

        // Assert
        Assert.Equal(
            ["the:Removed", "café:Kept"],
            stages[2].Tokens.Select(token => $"{token.Text}:{token.State}")
        );
    }

    [Fact]
    public void Mark_RemovedToken_IsNotCarriedIntoLaterStages()
    {
        // Arrange
        var after = new RawStage("ASCIIFoldingFilter", null, [new RawToken("cafe", 2, 4, 8)]);

        // Act
        IReadOnlyList<AnalysisStage> stages = AnalysisStages.Mark([Tokenizer, Lower, Stop, after]);

        // Assert
        Assert.Equal(["cafe:Changed"], stages[3].Tokens.Select(t => $"{t.Text}:{t.State}"));
    }

    [Fact]
    public void Mark_PositionIncrements_BecomePositionsWithGaps()
    {
        // Act
        IReadOnlyList<AnalysisStage> stages = AnalysisStages.Mark([Tokenizer, Lower, Stop]);

        // Assert
        Assert.Equal([0, 1], stages[2].Tokens.Select(token => token.Position));
    }

    [Fact]
    public void Mark_TokenWhereThePreviousStageHadNone_IsAdded()
    {
        // Arrange
        var synonyms = new RawStage(
            "SynonymFilter",
            null,
            [new RawToken("the", 1, 0, 3), new RawToken("café", 1, 4, 8), new("bistro", 0, 9, 9)]
        );

        // Act
        IReadOnlyList<AnalysisStage> stages = AnalysisStages.Mark([Lower, synonyms]);

        // Assert
        Assert.Equal(TokenState.Added, stages[1].Tokens.Single(t => t.Text == "bistro").State);
    }

    [Fact]
    public void Mark_NoStages_ReturnsNone()
    {
        // Act
        IReadOnlyList<AnalysisStage> stages = AnalysisStages.Mark([]);

        // Assert
        Assert.Empty(stages);
    }
}
