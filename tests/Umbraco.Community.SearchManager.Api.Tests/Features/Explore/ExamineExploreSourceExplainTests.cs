using Examine;
using Examine.Lucene.Providers;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Umbraco.Cms.Core.Services;
using Umbraco.Community.SearchManager.Core.Explore;
using Umbraco.Community.SearchManager.Core.Indexes;
using Umbraco.Community.SearchManager.Features.Explore;

namespace Umbraco.Community.SearchManager.Api.Tests.Features.Explore;

/// <summary>
/// Score explanations from a real in-memory Lucene index (ADR 0020). Besides the source's
/// behaviour, these pin what the reader depends on: that Lucene still prints the descriptions
/// <c>ScoreExplanationReaderTests</c> was captured from, and that an explanation's value is the
/// score the search returned.
/// </summary>
public sealed class ExamineExploreSourceExplainTests : IDisposable
{
    private readonly TestLuceneIndex _index = new(
        "ExternalIndex",
        TestLuceneIndex.Content(1, "blogPost", "Umbraco guide", 2, new DateTime(2026, 1, 15)),
        TestLuceneIndex.Content(
            2,
            "contentPage",
            "Getting started with Umbraco",
            3,
            new DateTime(2026, 3, 1)
        ),
        TestLuceneIndex.Content(3, "product", "Red hoodie", 3, new DateTime(2026, 9, 30)),
        TestLuceneIndex.Content(4, "product", "Blue mug", 4, new DateTime(2025, 12, 31))
    );

    private readonly TestLuceneIndex _other = new(
        "OtherIndex",
        TestLuceneIndex.Content(
            1,
            "blogPost",
            "Umbraco guide to Umbraco",
            2,
            new DateTime(2026, 1, 15)
        )
    );

    private readonly IExamineManager _examineManager = Substitute.For<IExamineManager>();
    private readonly ExamineExploreSource _source;
    private readonly ExploreTarget _target = Target("ExternalIndex", ExploreTargetKind.Index);

    public ExamineExploreSourceExplainTests()
    {
        _examineManager.Indexes.Returns([_index.Index, _other.Index]);
        _examineManager
            .TryGetIndex("ExternalIndex", out Arg.Any<IIndex>()!)
            .Returns(call =>
            {
                call[1] = _index.Index;
                return true;
            });
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 12, 12, 0, 0, TimeSpan.Zero));
        clock.SetLocalTimeZone(TimeZoneInfo.Utc);
        _source = new ExamineExploreSource(
            _examineManager,
            Array.Empty<IIndexSource>(),
            Substitute.For<ILanguageService>(),
            clock
        );
    }

    /// <summary>
    /// Chips (Kind|Field|Op|Value|Exclude) and an id they find. Each ranks: filters alone sort
    /// newest first and return no score, which the client explains without asking.
    /// </summary>
    public static TheoryData<string[], string> Searches =>
        new()
        {
            { ["Text||Contains|umbraco|False"], "2" },
            { ["Text||Contains|umbraco guide|False"], "1" },
            { ["Text||Contains|umbraco|False", "Field|type|Is|blogPost|False"], "1" },
            { ["Text||Contains|umbraco|False", "Field|level|Equals|3|False"], "2" },
            { ["Text||Contains|umbraco|False", "Field|type|Is|product|True"], "2" },
            { ["Text||Phrase|getting started|False"], "2" },
            { ["Text||Contains|hood*|False"], "3" },
        };

    [Theory]
    [MemberData(nameof(Searches))]
    public async Task ExplainAsync_Search_ValueIsTheScoreTheSearchReturned(
        string[] chips,
        string id
    )
    {
        // Arrange
        Filter[] filters = [.. chips.Select(Chip)];
        ExploreResult search = await _source.SearchAsync(
            _target,
            new ExploreQuery(filters),
            TestContext.Current.CancellationToken
        );

        // Act
        DocumentExplanation explained = await ExplainOneAsync(filters, id);

        // Assert
        Assert.Equal(search.Hits.Single(hit => hit.Id == id).Score, explained.Score);
    }

    [Fact]
    public async Task ExplainAsync_WordAndType_ReadsAWordMatchAndAFilter()
    {
        // Act
        DocumentExplanation explained = await ExplainOneAsync(
            [Chip("Text||Contains|umbraco|False"), Chip("Field|type|Is|blogPost|False")],
            "1"
        );

        // Assert: Lucene still prints what the reader recognises.
        ExplainNode root = explained.Root!;
        Assert.Equal(
            "Sum[Sum[TermMatch nodeName:umbraco idf 1.287682 tf 1 norm 0.625], Filter __NodeTypeAlias:blogpost #1]",
            Shape(root)
        );
    }

    [Fact]
    public async Task ExplainAsync_Phrase_ReadsAPhraseMatch()
    {
        // Act
        DocumentExplanation explained = await ExplainOneAsync(
            [Chip("Text||Phrase|getting started|False")],
            "2"
        );

        // Assert
        Assert.Equal(
            "Sum[PhraseMatch nodeName:getting started idf 3.3862944 tf 1 norm 0.5]",
            Shape(explained.Root!)
        );
    }

    [Fact]
    public async Task ExplainAsync_OneOfTwoOptionalWords_ReadsCoord()
    {
        // Act
        DocumentExplanation explained = await _source
            .ExplainAsync(
                _target,
                new ExplainRequest([], null, "umbraco hoodie", ["3"]),
                TestContext.Current.CancellationToken
            )
            .ContinueWith(task => task.Result.Documents[0]);

        // Assert
        Assert.Equal(
            "Coord 1/2[Sum[Sum[TermMatch nodeName:hoodie idf 1.6931472 tf 1 norm 0.625]]]",
            Shape(explained.Root!)
        );
    }

    [Fact]
    public async Task ExplainAsync_NativeQuery_ExplainsWithoutMustNotMatch()
    {
        // Act
        DocumentExplanation explained = await _source
            .ExplainAsync(
                _target,
                new ExplainRequest([], null, "nodeName:mug -__NodeTypeAlias:blogpost", ["4"]),
                TestContext.Current.CancellationToken
            )
            .ContinueWith(task => task.Result.Documents[0]);

        // Assert
        Assert.Equal((true, 0), (explained.Matched, explained.MustNotMatch.Count));
    }

    [Fact]
    public async Task ExplainAsync_ExcludedChip_IsListedAsMustNotMatch()
    {
        // Act
        DocumentExplanation explained = await ExplainOneAsync(
            [Chip("Text||Contains|umbraco|False"), Chip("Field|type|Is|product|True")],
            "2"
        );

        // Assert
        Assert.Equal([1], explained.MustNotMatch);
    }

    [Fact]
    public async Task ExplainAsync_IdNotInTheIndex_IsNotFound()
    {
        // Act
        DocumentExplanation explained = await ExplainOneAsync(
            [Chip("Text||Contains|umbraco|False")],
            "99"
        );

        // Assert
        Assert.Equal(
            (false, false, (ExplainNode?)null),
            (explained.Found, explained.Matched, explained.Root)
        );
    }

    [Fact]
    public async Task ExplainAsync_DocumentThatNoLongerMatches_IsFoundButNotMatched()
    {
        // Act
        DocumentExplanation explained = await ExplainOneAsync(
            [Chip("Text||Contains|umbraco|False")],
            "3"
        );

        // Assert
        Assert.Equal((true, false), (explained.Found, explained.Matched));
    }

    [Fact]
    public async Task ExplainAsync_TwoIds_ExplainsBothInOrder()
    {
        // Act
        ExplainResult result = await _source.ExplainAsync(
            _target,
            new ExplainRequest([Chip("Text||Contains|umbraco|False")], null, null, ["2", "1"]),
            TestContext.Current.CancellationToken
        );

        // Assert
        Assert.Equal(["2", "1"], result.Documents.Select(document => document.Id));
    }

    [Fact]
    public async Task ExplainAsync_TargetWithoutTheCapability_IsNotSupported()
    {
        // Arrange
        ExploreTarget target = _target with
        {
            Capabilities = new ExploreCapabilities(true, true),
        };

        // Act
        Task Explain() =>
            _source.ExplainAsync(
                target,
                new ExplainRequest([], null, null, ["1"]),
                TestContext.Current.CancellationToken
            );

        // Assert
        await Assert.ThrowsAsync<NotSupportedException>(Explain);
    }

    [Fact]
    public async Task ExplainAsync_MultiIndexSearcherWithTheIdTwice_ExplainsTheHigherScore()
    {
        // Arrange: node 1 is in both indexes; OtherIndex says "umbraco" twice, so scores higher.
        var multi = new MultiIndexSearcher(
            "BothSearcher",
            new Lazy<IEnumerable<ISearcher>>(() => [_index.Index.Searcher, _other.Index.Searcher])
        );
        _examineManager
            .TryGetSearcher("BothSearcher", out Arg.Any<ISearcher>()!)
            .Returns(call =>
            {
                call[1] = multi;
                return true;
            });
        ExploreTarget target = Target("BothSearcher", ExploreTargetKind.Searcher);
        Filter[] filters = [Chip("Text||Contains|umbraco|False")];
        ExploreResult search = await _source.SearchAsync(
            target,
            new ExploreQuery(filters),
            TestContext.Current.CancellationToken
        );

        // Act
        DocumentExplanation explained = (
            await _source.ExplainAsync(
                target,
                new ExplainRequest(filters, null, null, ["1"]),
                TestContext.Current.CancellationToken
            )
        ).Documents[0];

        // Assert
        Assert.Equal(
            search.Hits.Where(hit => hit.Id == "1").Max(hit => hit.Score),
            explained.Score
        );
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _index.Dispose();
        _other.Dispose();
    }

    private async Task<DocumentExplanation> ExplainOneAsync(Filter[] filters, string id) =>
        (
            await _source.ExplainAsync(
                _target,
                new ExplainRequest(filters, null, null, [id]),
                TestContext.Current.CancellationToken
            )
        ).Documents[0];

    // A compact picture of the tree: kind, field:term, factors, chip index, children.
    private static string Shape(ExplainNode node)
    {
        string text = node.Kind.ToString();
        if (node.Kind == ExplainNodeKind.Coord)
        {
            text += $" {node.Factors?.Matched}/{node.Factors?.Of}";
        }

        if (node.Field is not null)
        {
            text += $" {node.Field}:{node.Term}";
        }

        if (node.Kind is ExplainNodeKind.TermMatch or ExplainNodeKind.PhraseMatch)
        {
            text += FormattableString.Invariant(
                $" idf {node.Factors?.Idf} tf {node.Factors?.Tf} norm {node.Factors?.FieldNorm}"
            );
        }

        if (node.FilterIndex is int chip)
        {
            text += $" #{chip}";
        }

        return node.Kind == ExplainNodeKind.Filter || node.Children.Count == 0
            ? text
            : $"{text}[{string.Join(", ", node.Children.Select(Shape))}]";
    }

    private static ExploreTarget Target(string name, ExploreTargetKind kind) =>
        new(name, "examine", kind, null, false, new ExploreCapabilities(true, true, true));

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
