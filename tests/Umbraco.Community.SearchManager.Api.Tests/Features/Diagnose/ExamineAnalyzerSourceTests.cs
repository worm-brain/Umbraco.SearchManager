using Examine;
using Examine.Lucene.Analyzers;
using Lucene.Net.Analysis.Standard;
using NSubstitute;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Community.SearchManager.Api.Tests.Features.Explore;
using Umbraco.Community.SearchManager.Core.Diagnose;
using Umbraco.Community.SearchManager.Core.Explore;
using Umbraco.Community.SearchManager.Features.Diagnose.Analysis;

namespace Umbraco.Community.SearchManager.Api.Tests.Features.Diagnose;

/// <summary>
/// The analyzer view over real in-memory indexes set up with the analyzers Umbraco gives
/// ExternalIndex (StandardAnalyzer) and InternalIndex (CultureInvariantWhitespaceAnalyzer).
/// </summary>
public sealed class ExamineAnalyzerSourceTests : IDisposable
{
    private const string Text = "Café menu 2026: the opening hours";

    private static readonly ExploreTarget External = Target("ExternalIndex");
    private static readonly ExploreTarget Internal = Target("InternalIndex");

    private readonly TestLuceneIndex _external = new(
        "ExternalIndex",
        new StandardAnalyzer(Examine.LuceneInfo.CurrentVersion),
        Page()
    );

    private readonly TestLuceneIndex _internal = new(
        "InternalIndex",
        new CultureInvariantWhitespaceAnalyzer(),
        Page()
    );

    private readonly IExamineManager _examineManager = Substitute.For<IExamineManager>();
    private readonly ExamineAnalyzerSource _source;

    public ExamineAnalyzerSourceTests()
    {
        Register("ExternalIndex", _external.Index);
        Register("InternalIndex", _internal.Index);
        Register("ElasticIndex", Substitute.For<IIndex>());
        ILanguage english = Substitute.For<ILanguage>();
        english.IsoCode.Returns("en-US");
        ILanguageService languages = Substitute.For<ILanguageService>();
        languages.GetAllAsync().Returns([english]);
        _source = new ExamineAnalyzerSource(_examineManager, languages);
    }

    [Fact]
    public async Task GetFieldsAsync_ContentIndex_DefaultsToTheNodeName()
    {
        // Act
        AnalyzerFieldsResult result = await _source.GetFieldsAsync(External, Cancel);

        // Assert
        Assert.Equal("nodeName", result.DefaultField);
    }

    [Fact]
    public async Task GetFieldsAsync_ContentIndex_LeavesOutNumbersAndDates()
    {
        // Act
        AnalyzerFieldsResult result = await _source.GetFieldsAsync(External, Cancel);

        // Assert
        Assert.DoesNotContain(result.Fields, field => field.Name is "level" or "updateDate");
    }

    [Fact]
    public async Task AnalyzeAsync_ExternalIndex_ShowsTheStopFilterRemovingThe()
    {
        // Act
        AnalysisResult result = await AnalyzeAsync(External, null);

        // Assert
        AnalysisStage stop = result.Stages.Single(stage => stage.Name == "StopFilter");
        Assert.Equal(TokenState.Removed, stop.Tokens.Single(token => token.Text == "the").State);
    }

    [Fact]
    public async Task AnalyzeAsync_ExternalIndex_NamesTheAnalyzerAndShowsEveryStage()
    {
        // Act
        AnalysisResult result = await AnalyzeAsync(External, null);

        // Assert
        Assert.Equal(
            ("StandardAnalyzer", true, 4),
            (result.Analyzer, result.StagesShown, result.Stages.Count)
        );
    }

    [Fact]
    public async Task AnalyzeAsync_UnaccentedSearchOnExternalIndex_DoesNotMatchTheAccentedWord()
    {
        // Act
        AnalysisResult result = await AnalyzeAsync(External, "cafe");

        // Assert
        Assert.False(result.Search!.Matches);
    }

    [Fact]
    public async Task AnalyzeAsync_UnaccentedSearchOnInternalIndex_MatchesTheFoldedWord()
    {
        // Act
        AnalysisResult result = await AnalyzeAsync(Internal, "cafe");

        // Assert
        Assert.Equal(
            (true, TokenMatch.Exact),
            (result.Search!.Matches, result.Stages[^1].Tokens.Single(t => t.Text == "cafe").Match)
        );
    }

    [Theory]
    [InlineData("ExternalIndex")]
    [InlineData("InternalIndex")]
    public async Task AnalyzeAsync_Number_MatchesOnBothIndexes(string index)
    {
        // Act
        AnalysisResult result = await AnalyzeAsync(Target(index), "2026");

        // Assert
        Assert.True(result.Search!.Matches);
    }

    [Fact]
    public async Task AnalyzeAsync_StartOfAWord_MatchesByPrefix()
    {
        // Act
        AnalysisResult result = await AnalyzeAsync(External, "open");

        // Assert
        Assert.Equal(
            (true, TokenMatch.Prefix),
            (
                result.Search!.Matches,
                result.Stages[^1].Tokens.Single(t => t.Text == "opening").Match
            )
        );
    }

    [Fact]
    public async Task AnalyzeAsync_ShortStopWord_DoesNotMatchAndSaysShortTermsAreNotAnalysed()
    {
        // Act
        AnalysisResult result = await AnalyzeAsync(External, "the");

        // Assert
        Assert.Equal(
            (false, true),
            (
                result.Search!.Matches,
                result.Search.Notes.Any(note => note.Contains("shorter than 4"))
            )
        );
    }

    [Fact]
    public async Task AnalyzeAsync_ShortTermInAnotherCase_DoesNotMatch()
    {
        // Act
        AnalysisResult result = await AnalyzeAsync(Internal, "Caf");

        // Assert
        Assert.False(result.Search!.Matches);
    }

    [Fact]
    public async Task AnalyzeAsync_OnlyStopWords_FindsNothingAndSaysWhy()
    {
        // Act
        AnalysisResult result = await AnalyzeAsync(External, "the and");

        // Assert
        Assert.Equal(
            (false, true),
            (
                result.Search!.Matches,
                result.Search.Notes.Contains(LuceneTextAnalysis.NothingLeftNote)
            )
        );
    }

    [Fact]
    public async Task AnalyzeAsync_NumberField_IsUnavailableWithTheReason()
    {
        // Act
        AnalysisResult result = await _source.AnalyzeAsync(
            External,
            new AnalyzeRequest("level", "2", null),
            Cancel
        );

        // Assert
        Assert.Equal(
            (false, "level holds numbers, which are written as numeric terms and not analysed."),
            (result.Available, result.UnavailableReason)
        );
    }

    [Fact]
    public async Task AnalyzeAsync_UnknownField_IsUnavailable()
    {
        // Act
        AnalysisResult result = await _source.AnalyzeAsync(
            External,
            new AnalyzeRequest("noSuchField", Text, null),
            Cancel
        );

        // Assert
        Assert.False(result.Available);
    }

    [Fact]
    public async Task GetFieldsAsync_Searcher_IsUnavailableWithTheReason()
    {
        // Act
        AnalyzerFieldsResult result = await _source.GetFieldsAsync(
            External with
            {
                Kind = ExploreTargetKind.Searcher,
            },
            Cancel
        );

        // Assert
        Assert.Contains("is a searcher", result.UnavailableReason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetFieldsAsync_NotALuceneIndex_IsUnavailableWithTheReason()
    {
        // Act
        AnalyzerFieldsResult result = await _source.GetFieldsAsync(Target("ElasticIndex"), Cancel);

        // Assert
        Assert.Contains(
            "is not a Lucene index",
            result.UnavailableReason,
            StringComparison.Ordinal
        );
    }

    public void Dispose()
    {
        _external.Dispose();
        _internal.Dispose();
    }

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private Task<AnalysisResult> AnalyzeAsync(ExploreTarget target, string? term) =>
        _source.AnalyzeAsync(target, new AnalyzeRequest("nodeName", Text, term), Cancel);

    private void Register(string name, IIndex index) =>
        _examineManager
            .TryGetIndex(name, out Arg.Any<IIndex>()!)
            .Returns(call =>
            {
                call[1] = index;
                return true;
            });

    private static ValueSet Page() =>
        TestLuceneIndex.Content(1, "contentPage", "Opening hours", 2, new DateTime(2026, 1, 15));

    private static ExploreTarget Target(string name) =>
        new(
            name,
            "examine",
            ExploreTargetKind.Index,
            null,
            false,
            new ExploreCapabilities(true, true)
        );
}
