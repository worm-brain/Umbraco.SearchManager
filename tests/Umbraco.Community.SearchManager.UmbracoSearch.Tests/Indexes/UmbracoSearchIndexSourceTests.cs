using Examine;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Infrastructure.Examine;
using Umbraco.Cms.Search.Core.Models.Indexing;
using Umbraco.Cms.Search.Core.Services;
using Umbraco.Cms.Search.Core.Services.ContentIndexing;
using Umbraco.Cms.Search.Provider.Examine.Configuration;
using Umbraco.Cms.Search.Provider.Examine.Services;
using Umbraco.Community.SearchManager.Core.Indexes;
using Umbraco.Community.SearchManager.UmbracoSearch.Indexes;
using IndexOptions = Umbraco.Cms.Search.Core.Configuration.IndexOptions;

namespace Umbraco.Community.SearchManager.UmbracoSearch.Tests.Indexes;

public class UmbracoSearchIndexSourceTests
{
    private readonly IndexOptions _options = new();
    private readonly IIndexerResolver _resolver = Substitute.For<IIndexerResolver>();
    private readonly IExamineManager _examineManager = Substitute.For<IExamineManager>();
    private readonly IIndexDiagnosticsFactory _diagnosticsFactory =
        Substitute.For<IIndexDiagnosticsFactory>();
    private readonly IActiveIndexManager _activeIndexManager =
        Substitute.For<IActiveIndexManager>();
    private readonly IDistributedContentIndexRebuilder _rebuilder =
        Substitute.For<IDistributedContentIndexRebuilder>();
    private readonly IIndexCommitMonitor _commitMonitor = Substitute.For<IIndexCommitMonitor>();
    private readonly UmbracoSearchRebuildTracker _tracker = new();
    private readonly FakeTimeProvider _time = new();

    public UmbracoSearchIndexSourceTests()
    {
        _activeIndexManager.ResolveActiveIndexName(Arg.Any<string>()).Returns(x => x.Arg<string>());
    }

    [Fact]
    public async Task GetIndexesAsync_HealthyRegistration_ReportsUmbracoSearchHealthAndCount()
    {
        // Arrange
        RegisterExamine<IPublishedContentChangeStrategy>(
            "Umb_PublishedContent",
            UmbracoObjectTypes.Document
        );
        Metadata("Umb_PublishedContent", 3370, HealthStatus.Healthy);

        // Act
        IndexSummary index = Assert.Single(await ListAsync());

        // Assert
        Assert.Equal(
            ("Umb_PublishedContent", "umbraco-search", IndexHealth.Healthy, 3370L),
            (index.Name, index.Engine, index.Health, index.DocumentCount)
        );
    }

    [Fact]
    public async Task GetIndexesAsync_ZeroDowntimeOn_ReadsFieldsFromActivePhysicalIndex()
    {
        // Arrange
        RegisterExamine<IDraftContentChangeStrategy>("Umb_Content", UmbracoObjectTypes.Document);
        Metadata("Umb_Content", 10, HealthStatus.Healthy);
        _activeIndexManager.ResolveActiveIndexName("Umb_Content").Returns("Umb_Content_b");
        PhysicalIndex(
            "Umb_Content_b",
            fields: ["Umb_Name_textsr1", "Umb_Id_keywords", "Sys_Culture"]
        );

        // Act
        IndexSummary index = Assert.Single(await ListAsync(zeroDowntime: true));

        // Assert
        Assert.Equal((true, 3), (index.ZeroDowntime, index.FieldCount));
    }

    [Fact]
    public async Task ClaimedExamineIndexNames_ExamineBackedRegistration_ClaimsAliasAndBothSlots()
    {
        // Arrange
        RegisterExamine<IDraftContentChangeStrategy>("Umb_Media", UmbracoObjectTypes.Media);

        // Act
        UmbracoSearchIndexSource source = CreateSource();

        // Assert
        Assert.Equal(
            ["Umb_Media", "Umb_Media_a", "Umb_Media_b"],
            source.ClaimedExamineIndexNames.Order(StringComparer.Ordinal)
        );
    }

    [Fact]
    public void ClaimedExamineIndexNames_OtherProvider_ClaimsNothing()
    {
        // Arrange
        _options.RegisterContentIndex<
            IOtherIndexer,
            Umbraco.Cms.Search.Core.Services.ISearcher,
            IDraftContentChangeStrategy
        >("Elastic_Content", UmbracoObjectTypes.Document);

        // Act
        UmbracoSearchIndexSource source = CreateSource();

        // Assert
        Assert.Empty(source.ClaimedExamineIndexNames);
    }

    [Fact]
    public async Task GetIndexesAsync_MetadataThrows_ListsCorruptedIndexWithMessage()
    {
        // Arrange
        RegisterExamine<IDraftContentChangeStrategy>("Umb_Content", UmbracoObjectTypes.Document);
        IIndexer indexer = Substitute.For<IIndexer>();
        indexer
            .GetMetadataAsync("Umb_Content")
            .ThrowsAsync(new InvalidOperationException("Lock obtain timed out"));
        _resolver.GetIndexer("Umb_Content").Returns(indexer);

        // Act
        IndexSummary index = Assert.Single(await ListAsync());

        // Assert
        Assert.Equal(
            (IndexHealth.Corrupted, "Lock obtain timed out"),
            (index.Health, index.HealthMessage)
        );
    }

    [Fact]
    public async Task GetIndexesAsync_IndexerNotResolved_ListsUnknownWithoutRebuild()
    {
        // Arrange
        RegisterExamine<IDraftContentChangeStrategy>("Umb_Content", UmbracoObjectTypes.Document);
        _resolver.GetIndexer("Umb_Content").Returns((IIndexer?)null);

        // Act
        IndexSummary index = Assert.Single(await ListAsync());

        // Assert
        Assert.Equal((IndexHealth.Unknown, false), (index.Health, index.Capabilities.Rebuild));
    }

    [Fact]
    public async Task GetIndexesAsync_MemberRegistration_IsFlaggedAsMemberIndex()
    {
        // Arrange
        RegisterExamine<IDraftContentChangeStrategy>("Umb_Members", UmbracoObjectTypes.Member);
        Metadata("Umb_Members", 12, HealthStatus.Healthy);

        // Act
        IndexSummary index = Assert.Single(await ListAsync());

        // Assert
        Assert.True(index.IsMemberIndex);
    }

    [Fact]
    public async Task GetIndexesAsync_SearchCoreNotAdded_ListsNothing()
    {
        // Arrange
        RegisterExamine<IDraftContentChangeStrategy>("Umb_Content", UmbracoObjectTypes.Document);
        var source = new UmbracoSearchIndexSource(
            Options.Create(_options),
            _examineManager,
            _diagnosticsFactory,
            Options.Create(new ExamineSearchProviderSettings()),
            NullLogger<UmbracoSearchIndexSource>.Instance,
            _tracker,
            _time
        );

        // Act
        IReadOnlyList<IndexSummary> indexes = await source.GetIndexesAsync(
            new HashSet<string>(),
            TestContext.Current.CancellationToken
        );

        // Assert
        Assert.Empty(indexes);
    }

    [Theory]
    [InlineData(HealthStatus.Healthy, IndexHealth.Healthy)]
    [InlineData(HealthStatus.Rebuilding, IndexHealth.Rebuilding)]
    [InlineData(HealthStatus.Corrupted, IndexHealth.Corrupted)]
    [InlineData(HealthStatus.Empty, IndexHealth.Empty)]
    [InlineData(HealthStatus.Unknown, IndexHealth.Unknown)]
    public void MapHealth_EachStatus_MapsToTheSameName(HealthStatus status, IndexHealth expected)
    {
        // Act
        IndexHealth health = UmbracoSearchIndexSource.MapHealth(status);

        // Assert
        Assert.Equal(expected, health);
    }

    [Fact]
    public async Task GetIndexesAsync_PublishedContentRegistration_DescribesPublishedContent()
    {
        // Arrange
        RegisterExamine<IPublishedContentChangeStrategy>(
            "Umb_PublishedContent",
            UmbracoObjectTypes.Document
        );
        Metadata("Umb_PublishedContent", 1, HealthStatus.Healthy);

        // Act
        IndexSummary index = Assert.Single(await ListAsync());

        // Assert
        Assert.Equal("Published content", index.Description);
    }

    [Fact]
    public async Task GetIndexesAsync_DraftContentRegistration_DescribesUnpublishedContent()
    {
        // Arrange
        RegisterExamine<IDraftContentChangeStrategy>("Umb_Content", UmbracoObjectTypes.Document);
        Metadata("Umb_Content", 1, HealthStatus.Healthy);

        // Act
        IndexSummary index = Assert.Single(await ListAsync());

        // Assert
        Assert.Equal("Content, including unpublished", index.Description);
    }

    [Fact]
    public async Task RebuildAsync_UmbracoSearchCompletes_Completes()
    {
        // Arrange
        RegisterExamine<IDraftContentChangeStrategy>("Umb_Content", UmbracoObjectTypes.Document);
        RebuildAnnounces("Umb_Content");

        // Act
        Task rebuild = RebuildAsync("Umb_Content");
        await rebuild;

        // Assert
        Assert.True(rebuild.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task RebuildAsync_NotZeroDowntime_WaitsForThePhysicalIndexToCommit()
    {
        // Arrange
        RegisterExamine<IDraftContentChangeStrategy>("Umb_Content", UmbracoObjectTypes.Document);
        RebuildAnnounces("Umb_Content");

        // Act
        await RebuildAsync("Umb_Content");

        // Assert
        await _commitMonitor
            .Received(1)
            .WaitForCommitAsync("Umb_Content", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RebuildAsync_UmbracoSearchRefuses_ThrowsWithReason()
    {
        // Arrange
        RegisterExamine<IDraftContentChangeStrategy>("Umb_Content", UmbracoObjectTypes.Document);
        _rebuilder.Rebuild("Umb_Content").Returns(false);

        // Act
        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                RebuildAsync("Umb_Content")
        );

        // Assert
        Assert.Contains("refused", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RebuildAsync_ZeroDowntimeCopyNotSwappedIn_ThrowsBecauseTheOldCopyWasKept()
    {
        // Arrange
        RegisterExamine<IDraftContentChangeStrategy>("Umb_Members", UmbracoObjectTypes.Member);
        _activeIndexManager.ResolveActiveIndexName("Umb_Members").Returns("Umb_Members_a");
        RebuildAnnounces("Umb_Members");

        // Act
        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                RebuildAsync("Umb_Members", zeroDowntime: true)
        );

        // Assert
        Assert.Contains("kept the current copy", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RebuildAsync_ZeroDowntimeCopySwappedIn_Completes()
    {
        // Arrange
        RegisterExamine<IDraftContentChangeStrategy>("Umb_Content", UmbracoObjectTypes.Document);
        _activeIndexManager
            .ResolveActiveIndexName("Umb_Content")
            .Returns("Umb_Content_a", "Umb_Content_b");
        RebuildAnnounces("Umb_Content");

        // Act
        Task rebuild = RebuildAsync("Umb_Content", zeroDowntime: true);
        await rebuild;

        // Assert
        Assert.True(rebuild.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task RebuildAsync_NeverStarts_FailsAfterTheStartTimeout()
    {
        // Arrange
        RegisterExamine<IDraftContentChangeStrategy>("Umb_Content", UmbracoObjectTypes.Document);
        _rebuilder.Rebuild("Umb_Content").Returns(true);
        Task rebuild = RebuildAsync("Umb_Content");

        // Act
        while (!rebuild.IsCompleted)
        {
            _time.Advance(TimeSpan.FromSeconds(30));
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        // Assert
        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                rebuild
        );
        Assert.Contains("did not start", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>Makes Umbraco Search accept the rebuild and announce its start and end at once.</summary>
    private void RebuildAnnounces(string alias) =>
        _rebuilder
            .Rebuild(alias)
            .Returns(_ =>
            {
                _tracker.OnStarting(alias);
                _tracker.OnCompleted(alias);
                return true;
            });

    private Task RebuildAsync(string alias, bool zeroDowntime = false) =>
        CreateSource(zeroDowntime)
            .RebuildAsync(alias, new Progress<long>(), TestContext.Current.CancellationToken);

    private void RegisterExamine<TStrategy>(string alias, UmbracoObjectTypes type)
        where TStrategy : class, IContentChangeStrategy =>
        _options.RegisterContentIndex<IExamineIndexer, IExamineSearcher, TStrategy>(alias, type);

    private void Metadata(string alias, long documents, HealthStatus health)
    {
        IIndexer indexer = Substitute.For<IIndexer>();
        indexer
            .GetMetadataAsync(alias)
            .Returns(new IndexMetadata(documents, health, "search-examine-provider"));
        _resolver.GetIndexer(alias).Returns(indexer);
    }

    private void PhysicalIndex(string name, string[] fields)
    {
        IIndex index = Substitute.For<IIndex>();
        index.Name.Returns(name);
        _examineManager
            .TryGetIndex(name, out Arg.Any<IIndex>()!)
            .Returns(call =>
            {
                call[1] = index;
                return true;
            });

        IIndexDiagnostics diagnostics = Substitute.For<IIndexDiagnostics>();
        diagnostics.IsHealthy().Returns(Attempt<string?>.Succeed());
        diagnostics.GetFieldNames().Returns(fields);
        diagnostics.Metadata.Returns(new Dictionary<string, object?>());
        _diagnosticsFactory.Create(index).Returns(diagnostics);
    }

    private UmbracoSearchIndexSource CreateSource(bool zeroDowntime = false) =>
        new(
            Options.Create(_options),
            _examineManager,
            _diagnosticsFactory,
            Options.Create(
                new ExamineSearchProviderSettings { ZeroDowntimeIndexing = zeroDowntime }
            ),
            NullLogger<UmbracoSearchIndexSource>.Instance,
            _tracker,
            _time,
            _resolver,
            _activeIndexManager,
            _rebuilder,
            _commitMonitor
        );

    private Task<IReadOnlyList<IndexSummary>> ListAsync(bool zeroDowntime = false) =>
        CreateSource(zeroDowntime)
            .GetIndexesAsync(new HashSet<string>(), TestContext.Current.CancellationToken);

    /// <summary>An indexer from a provider other than Examine.</summary>
    public interface IOtherIndexer : IIndexer;
}
