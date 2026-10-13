using Examine;
using Examine.Lucene.Indexing;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Umbraco.Community.SearchManager.Api.Tests.Features.Explore;
using Umbraco.Community.SearchManager.Core.Diagnose;
using Umbraco.Community.SearchManager.Features.Diagnose.Analysis;

namespace Umbraco.Community.SearchManager.Api.Tests.Features.Diagnose;

public sealed class SearchTermProbeTests : IDisposable
{
    private readonly StandardAnalyzer _analyzer = new(LuceneInfo.CurrentVersion);

    [Fact]
    public void Parts_ExamineFullTextQuery_PhraseRanksAndEachWordIsRequiredAsWordOrPrefix()
    {
        // Arrange
        Query query = FullTextType.GenerateQuery("nodeName", "opening hours", _analyzer);

        // Act
        IReadOnlyList<SearchTermPart> parts = SearchTermProbe.Parts(query);

        // Assert
        Assert.Equal(
            [
                "0 Phrase opening hours optional",
                "1 Word opening required",
                "1 Prefix opening required",
                "2 Word hours required",
                "2 Prefix hours required",
            ],
            parts.Select(p =>
                $"{p.Group} {p.Kind} {p.Text} {(p.Required ? "required" : "optional")}"
            )
        );
    }

    [Fact]
    public void Parts_TrailingStarWildcard_IsAPrefix()
    {
        // Act
        IReadOnlyList<SearchTermPart> parts = SearchTermProbe.Parts(
            new WildcardQuery(new Term("f", "caf*"))
        );

        // Assert
        Assert.Equal((SearchTermPartKind.Prefix, "caf"), (parts[0].Kind, parts[0].Text));
    }

    [Fact]
    public void Run_QueryForAWordTheTextHolds_Matches()
    {
        // Arrange
        using var index = new TestLuceneIndex("ExternalIndex", _analyzer);

        // Act
        bool matches = SearchTermProbe.Run(
            index.Index,
            null,
            "nodeName",
            "Opening hours",
            new TermQuery(new Term("nodeName", "hours"))
        );

        // Assert
        Assert.True(matches);
    }

    [Fact]
    public void Run_QueryForAWordTheTextLacks_DoesNotMatch()
    {
        // Arrange
        using var index = new TestLuceneIndex("ExternalIndex", _analyzer);

        // Act
        bool matches = SearchTermProbe.Run(
            index.Index,
            null,
            "nodeName",
            "Opening hours",
            new TermQuery(new Term("nodeName", "menu"))
        );

        // Assert
        Assert.False(matches);
    }

    public void Dispose() => _analyzer.Dispose();
}
