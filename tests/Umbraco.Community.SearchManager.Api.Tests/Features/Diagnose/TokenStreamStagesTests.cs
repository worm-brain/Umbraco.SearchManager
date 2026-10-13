using Examine;
using Examine.Lucene.Analyzers;
using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Core;
using Lucene.Net.Analysis.Standard;
using Umbraco.Community.SearchManager.Core.Diagnose;
using Umbraco.Community.SearchManager.Features.Diagnose.Analysis;

namespace Umbraco.Community.SearchManager.Api.Tests.Features.Diagnose;

/// <summary>
/// Reads the stages of the analyzers Umbraco's indexes use. Pins the private <c>m_input</c> link
/// the walk relies on: a Lucene.NET upgrade that renames it fails here, not on a site.
/// </summary>
public sealed class TokenStreamStagesTests : IDisposable
{
    private const string Text = "Café menu 2026: the opening hours";

    private readonly StandardAnalyzer _standard = new(LuceneInfo.CurrentVersion);
    private readonly CultureInvariantWhitespaceAnalyzer _whitespace = new();

    [Fact]
    public void TryRead_StandardAnalyzer_ReadsEveryStageInOrder()
    {
        // Act
        IReadOnlyList<RawStage>? stages = TokenStreamStages.TryRead(
            _standard,
            "nodeName",
            Text,
            500,
            out _
        );

        // Assert
        Assert.Equal(
            ["StandardTokenizer", "StandardFilter", "LowerCaseFilter", "StopFilter"],
            stages!.Select(stage => stage.Name)
        );
    }

    [Fact]
    public void TryRead_StandardAnalyzer_LowerCasesButKeepsTheAccent()
    {
        // Act
        IReadOnlyList<RawStage>? stages = TokenStreamStages.TryRead(
            _standard,
            "nodeName",
            Text,
            500,
            out _
        );

        // Assert
        Assert.Equal("café", stages![2].Tokens[0].Text);
    }

    [Fact]
    public void TryRead_StandardAnalyzer_StopFilterDropsTheAndSkipsAPosition()
    {
        // Act
        IReadOnlyList<RawStage>? stages = TokenStreamStages.TryRead(
            _standard,
            "nodeName",
            Text,
            500,
            out _
        );

        // Assert
        Assert.Equal(
            ["café+1", "menu+1", "2026+1", "opening+2", "hours+1"],
            stages![3].Tokens.Select(token => $"{token.Text}+{token.PositionIncrement}")
        );
    }

    [Fact]
    public void TryRead_WhitespaceAnalyzer_SplitsOnNonLettersAndFoldsAccents()
    {
        // Act
        IReadOnlyList<RawStage>? stages = TokenStreamStages.TryRead(
            _whitespace,
            "nodeName",
            Text,
            500,
            out _
        );

        // Assert
        Assert.Equal(
            (
                "LetterOrDigitTokenizer|LowerCaseFilter|ASCIIFoldingFilter",
                "cafe menu 2026 the opening hours"
            ),
            (
                string.Join("|", stages!.Select(stage => stage.Name)),
                string.Join(" ", stages![^1].Tokens.Select(token => token.Text))
            )
        );
    }

    [Fact]
    public void TryRead_MoreTokensThanTheLimit_StopsAndSaysSo()
    {
        // Act
        IReadOnlyList<RawStage>? stages = TokenStreamStages.TryRead(
            _whitespace,
            "nodeName",
            Text,
            2,
            out bool truncated
        );

        // Assert
        Assert.Equal((2, true), (stages![0].Tokens.Count, truncated));
    }

    [Fact]
    public void TryRead_LinkThatIsNotAFilterOrTokenizer_ReturnsNull()
    {
        // Arrange
        using var analyzer = new OpaqueAnalyzer();

        // Act
        IReadOnlyList<RawStage>? stages = TokenStreamStages.TryRead(
            analyzer,
            "nodeName",
            Text,
            500,
            out _
        );

        // Assert
        Assert.Null(stages);
    }

    [Fact]
    public void ReadOutput_AnyAnalyzer_ReadsTheFinalTokensAsOneStage()
    {
        // Arrange
        using var analyzer = new OpaqueAnalyzer();

        // Act
        RawStage stage = TokenStreamStages.ReadOutput(analyzer, "nodeName", "a b", 500, out _);

        // Assert
        Assert.Equal(["a", "b"], stage.Tokens.Select(token => token.Text));
    }

    [Fact]
    public void TryRead_AfterTheWalk_TheAnalyzerStillWorks()
    {
        // Arrange
        TokenStreamStages.TryRead(_standard, "nodeName", Text, 500, out _);

        // Act
        RawStage stage = TokenStreamStages.ReadOutput(
            _standard,
            "nodeName",
            "Hello World",
            500,
            out _
        );

        // Assert
        Assert.Equal(["hello", "world"], stage.Tokens.Select(token => token.Text));
    }

    public void Dispose()
    {
        _standard.Dispose();
        _whitespace.Dispose();
    }

    /// <summary>An analyzer whose last stage wraps a stream the walk cannot see through.</summary>
    private sealed class OpaqueAnalyzer : Analyzer
    {
        protected override TokenStreamComponents CreateComponents(
            string fieldName,
            TextReader reader
        )
        {
            var tokenizer = new WhitespaceTokenizer(LuceneInfo.CurrentVersion, reader);
            return new TokenStreamComponents(tokenizer, new Opaque(tokenizer));
        }

        /// <summary>Passes tokens through, but is neither a filter nor a tokenizer.</summary>
        private sealed class Opaque(TokenStream input) : TokenStream(input)
        {
            public override bool IncrementToken() => input.IncrementToken();

            public override void Reset()
            {
                base.Reset();
                input.Reset();
            }

            public override void End()
            {
                base.End();
                input.End();
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    input.Dispose();
                }

                base.Dispose(disposing);
            }
        }
    }
}
