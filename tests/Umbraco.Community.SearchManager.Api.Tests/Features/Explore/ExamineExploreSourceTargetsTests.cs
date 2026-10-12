using Examine;
using Examine.Lucene.Providers;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Infrastructure.Examine;
using Umbraco.Community.SearchManager.Core.Explore;
using Umbraco.Community.SearchManager.Core.Indexes;
using Umbraco.Community.SearchManager.Features.Explore;

namespace Umbraco.Community.SearchManager.Api.Tests.Features.Explore;

public sealed class ExamineExploreSourceTargetsTests : IDisposable
{
    private readonly TestLuceneIndex _content = new(
        "ExternalIndex",
        TestLuceneIndex.Content(1, "page", "Page", 1, new DateTime(2026, 1, 1))
    );

    private readonly TestLuceneIndex _members = new(
        "MembersIndex",
        TestLuceneIndex.Content(2, "member", "Member", 1, new DateTime(2026, 1, 1))
    );

    private readonly IExamineManager _examineManager = Substitute.For<IExamineManager>();
    private readonly List<IIndexSource> _otherSources = [];

    public ExamineExploreSourceTargetsTests()
    {
        // The member index is Umbraco's member index type over a real Lucene searcher.
        IIndex memberIndex = Substitute.For<IIndex, IUmbracoMemberIndex>();
        memberIndex.Name.Returns("MembersIndex");
        memberIndex.Searcher.Returns(_members.Index.Searcher);

        _examineManager.Indexes.Returns([_content.Index, memberIndex]);
        _examineManager.RegisteredSearchers.Returns([]);
    }

    [Fact]
    public async Task GetTargetsAsync_Indexes_AreListedWithDescriptionAndLuceneCapabilities()
    {
        // Act
        ExploreTarget target = (await ListAsync()).First(t => t.Name == "ExternalIndex");

        // Assert
        Assert.Equal(
            new ExploreTarget(
                "ExternalIndex",
                "examine",
                ExploreTargetKind.Index,
                "Published content and media",
                false,
                new ExploreCapabilities(true, true)
            ),
            target
        );
    }

    [Fact]
    public async Task GetTargetsAsync_MemberIndex_IsFlaggedAsMemberData()
    {
        // Act
        ExploreTarget target = (await ListAsync()).First(t => t.Name == "MembersIndex");

        // Assert
        Assert.True(target.IsMemberData);
    }

    [Fact]
    public async Task GetTargetsAsync_MultiSearcherOverAMemberIndex_IsMemberData()
    {
        // Arrange
        var multi = new MultiIndexSearcher(
            "EverythingSearcher",
            new Lazy<IEnumerable<ISearcher>>(() =>
                [_content.Index.Searcher, _members.Index.Searcher]
            )
        );
        _examineManager.RegisteredSearchers.Returns([multi]);

        // Act
        ExploreTarget target = (await ListAsync()).First(t => t.Name == "EverythingSearcher");

        // Assert
        Assert.Equal((ExploreTargetKind.Searcher, true), (target.Kind, target.IsMemberData));
    }

    [Fact]
    public async Task GetTargetsAsync_MultiSearcherOverContentOnly_IsNotMemberData()
    {
        // Arrange
        var multi = new MultiIndexSearcher(
            "ContentSearcher",
            new Lazy<IEnumerable<ISearcher>>(() => [_content.Index.Searcher])
        );
        _examineManager.RegisteredSearchers.Returns([multi]);

        // Act
        ExploreTarget target = (await ListAsync()).First(t => t.Name == "ContentSearcher");

        // Assert
        Assert.False(target.IsMemberData);
    }

    [Fact]
    public async Task GetTargetsAsync_IndexClaimedByAnotherSource_IsLeftOut()
    {
        // Arrange
        IIndexSource search = Substitute.For<IIndexSource>();
        search.Engine.Returns("umbraco-search");
        search.ClaimedExamineIndexNames.Returns(new HashSet<string> { "ExternalIndex" });
        _otherSources.Add(search);

        // Act
        IReadOnlyList<ExploreTarget> targets = await ListAsync();

        // Assert
        Assert.Equal(["MembersIndex"], targets.Select(t => t.Name));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _content.Dispose();
        _members.Dispose();
    }

    private Task<IReadOnlyList<ExploreTarget>> ListAsync() =>
        new ExamineExploreSource(
            _examineManager,
            _otherSources,
            Substitute.For<ILanguageService>(),
            new FakeTimeProvider()
        ).GetTargetsAsync(TestContext.Current.CancellationToken);
}
