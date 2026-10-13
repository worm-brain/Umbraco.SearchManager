using Examine;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Umbraco.Cms.Core;
using Umbraco.Cms.Infrastructure.Examine;
using Umbraco.Cms.Infrastructure.Models;
using Umbraco.Cms.Infrastructure.Services;
using Umbraco.Community.SearchManager.Core.Indexes;
using Umbraco.Community.SearchManager.Features.Indexes;
using Umbraco.Community.SearchManager.Features.Rebuilds.External;

namespace Umbraco.Community.SearchManager.Api.Tests.Features.Indexes;

public class ExamineIndexSourceTests
{
    private readonly IExamineManager _examineManager = Substitute.For<IExamineManager>();
    private readonly IIndexDiagnosticsFactory _diagnosticsFactory =
        Substitute.For<IIndexDiagnosticsFactory>();
    private readonly IIndexRebuilder _indexRebuilder = Substitute.For<IIndexRebuilder>();
    private readonly IIndexingRebuilderService _rebuilder =
        Substitute.For<IIndexingRebuilderService>();

    [Fact]
    public async Task GetIndexesAsync_HealthyIndex_ReportsCountsPropertiesAndRebuild()
    {
        // Arrange
        AddIndex("ExternalIndex", documents: 3412, fields: ["nodeName", "bodyText"]);

        // Act
        IndexSummary index = Assert.Single(await ListAsync());

        // Assert
        Assert.Multiple(
            () => Assert.Equal(IndexHealth.Healthy, index.Health),
            () => Assert.Equal(3412, index.DocumentCount),
            () => Assert.Equal(2, index.FieldCount),
            () => Assert.Equal("Published content and media", index.Description),
            () => Assert.True(index.Capabilities.Rebuild),
            () => Assert.Equal("StandardAnalyzer", index.Properties["DefaultAnalyzer"])
        );
    }

    [Fact]
    public async Task GetIndexesAsync_DiagnosticsThrow_ListsCorruptedIndexWithMessage()
    {
        // Arrange
        IIndex index = AddIndex("ProductIndex", documents: 1, fields: []);
        _diagnosticsFactory
            .Create(index)
            .Throws(new InvalidOperationException("Lock obtain timed out"));

        // Act
        IndexSummary summary = Assert.Single(await ListAsync());

        // Assert
        Assert.Multiple(
            () => Assert.Equal(IndexHealth.Corrupted, summary.Health),
            () => Assert.Equal("Lock obtain timed out", summary.HealthMessage),
            () => Assert.Null(summary.DocumentCount),
            () => Assert.False(summary.Capabilities.Rebuild)
        );
    }

    [Fact]
    public async Task GetIndexesAsync_UnhealthyIndex_ReportsUnhealthyWithReason()
    {
        // Arrange
        IIndex index = AddIndex("ExternalIndex", documents: 5, fields: []);
        _diagnosticsFactory.Create(index).IsHealthy().Returns(Attempt.Fail("Reader failed"));

        // Act
        IndexSummary summary = Assert.Single(await ListAsync());

        // Assert
        Assert.Equal(
            (IndexHealth.Unhealthy, "Reader failed"),
            (summary.Health, summary.HealthMessage)
        );
    }

    [Fact]
    public async Task GetIndexesAsync_GenericDiagnosticsCannotCount_ReportsUnknownCounts()
    {
        // Arrange
        AddIndex("ExamineXIndex", documents: -1, fields: []);

        // Act
        IndexSummary summary = Assert.Single(await ListAsync());

        // Assert
        Assert.Equal((null, null), (summary.DocumentCount, summary.FieldCount));
    }

    [Fact]
    public async Task GetIndexesAsync_EmptyIndex_ReportsEmpty()
    {
        // Arrange
        AddIndex("DeliveryApiContentIndex", documents: 0, fields: []);

        // Act
        IndexSummary summary = Assert.Single(await ListAsync());

        // Assert
        Assert.Equal(IndexHealth.Empty, summary.Health);
    }

    [Fact]
    public async Task GetIndexesAsync_IndexRebuilding_ReportsRebuilding()
    {
        // Arrange
        AddIndex("InternalIndex", documents: 100, fields: []);
        _rebuilder.IsRebuildingAsync("InternalIndex").Returns(true);

        // Act
        IndexSummary summary = Assert.Single(await ListAsync());

        // Assert
        Assert.Equal(IndexHealth.Rebuilding, summary.Health);
    }

    [Fact]
    public async Task GetIndexesAsync_NoPopulator_RebuildUnavailableWithReason()
    {
        // Arrange
        AddIndex("DeliveryApiContentIndex", documents: 0, fields: []);
        _rebuilder.CanRebuild("DeliveryApiContentIndex").Returns(false);

        // Act
        IndexSummary summary = Assert.Single(await ListAsync());

        // Assert
        Assert.Multiple(
            () => Assert.False(summary.Capabilities.Rebuild),
            () =>
                Assert.Contains(
                    "No populator",
                    summary.RebuildUnavailableReason,
                    StringComparison.Ordinal
                )
        );
    }

    [Fact]
    public async Task GetIndexesAsync_MemberIndex_IsFlagged()
    {
        // Arrange
        IIndex members = Substitute.For<IIndex, IUmbracoMemberIndex>();
        members.Name.Returns("MembersIndex");
        Register(members, documents: 12, fields: []);

        // Act
        IndexSummary summary = Assert.Single(await ListAsync());

        // Assert
        Assert.True(summary.IsMemberIndex);
    }

    [Fact]
    public async Task GetIndexesAsync_IndexClaimedElsewhere_IsLeftOut()
    {
        // Arrange
        AddIndex("ExternalIndex", documents: 1, fields: []);
        AddIndex("Umb_Content_a", documents: 1, fields: []);

        // Act
        IReadOnlyList<IndexSummary> indexes = await ListAsync(claimed: "Umb_Content_a");

        // Assert
        Assert.Equal(["ExternalIndex"], indexes.Select(index => index.Name));
    }

    [Fact]
    public async Task RebuildAsync_UmbracoRebuildsTheIndex_Completes()
    {
        // Arrange
        AddIndex("ExternalIndex", documents: 10, fields: []);
        _indexRebuilder
            .RebuildIndexAsync("ExternalIndex", Arg.Any<TimeSpan?>(), false)
            .Returns(Attempt.Succeed(IndexRebuildResult.Success));

        // Act
        Task rebuild = RebuildAsync("ExternalIndex");
        await rebuild;

        // Assert
        Assert.True(rebuild.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task RebuildAsync_UmbracoAlreadyRebuilding_ThrowsWithReason()
    {
        // Arrange
        AddIndex("ExternalIndex", documents: 10, fields: []);
        _rebuilder.IsRebuildingAsync("ExternalIndex").Returns(true);

        // Act
        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                RebuildAsync("ExternalIndex")
        );

        // Assert
        Assert.Contains("already rebuilding", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RebuildAsync_RebuilderRefuses_ThrowsWithReason()
    {
        // Arrange
        AddIndex("ExternalIndex", documents: 10, fields: []);
        _indexRebuilder
            .RebuildIndexAsync("ExternalIndex", Arg.Any<TimeSpan?>(), false)
            .Returns(Attempt.Fail(IndexRebuildResult.NotAllowedToRun));

        // Act
        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                RebuildAsync("ExternalIndex")
        );

        // Assert
        Assert.Contains("not allowed", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RebuildAsync_IndexingErrorDuringRebuild_ThrowsWithTheError()
    {
        // Arrange
        IIndex index = AddIndex("ProductIndex", documents: 10, fields: []);
        _indexRebuilder
            .RebuildIndexAsync("ProductIndex", Arg.Any<TimeSpan?>(), false)
            .Returns(_ =>
            {
                index.IndexingError += Raise.EventWith(
                    new IndexingErrorEventArgs(index, "Value cannot be null", "1234", null)
                );
                return Attempt.Succeed(IndexRebuildResult.Success);
            });

        // Act
        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                RebuildAsync("ProductIndex")
        );

        // Assert
        Assert.Equal("Value cannot be null", exception.Message);
    }

    [Fact]
    public async Task RebuildAsync_NoPopulator_Throws()
    {
        // Arrange
        AddIndex("DeliveryApiContentIndex", documents: 0, fields: []);
        _rebuilder.CanRebuild("DeliveryApiContentIndex").Returns(false);

        // Act
        Task rebuild = RebuildAsync("DeliveryApiContentIndex");

        // Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => rebuild);
    }

    [Fact]
    public async Task RebuildAsync_DuringTheRebuild_MarksTheFlowAsSearchManagers()
    {
        // Arrange
        AddIndex("ExternalIndex", documents: 10, fields: []);
        bool marked = false;
        _indexRebuilder
            .RebuildIndexAsync("ExternalIndex", Arg.Any<TimeSpan?>(), false)
            .Returns(_ =>
            {
                marked = SearchManagerRebuildScope.IsActive;
                return Attempt.Succeed(IndexRebuildResult.Success);
            });

        // Act
        await RebuildAsync("ExternalIndex");

        // Assert
        Assert.Equal((true, false), (marked, SearchManagerRebuildScope.IsActive));
    }

    private ExamineIndexSource CreateSource() =>
        new(
            _examineManager,
            _diagnosticsFactory,
            _rebuilder,
            _indexRebuilder,
            TimeProvider.System,
            NullLogger<ExamineIndexSource>.Instance
        );

    private Task RebuildAsync(string name) =>
        CreateSource()
            .RebuildAsync(name, new Progress<long>(), TestContext.Current.CancellationToken);

    private async Task<IReadOnlyList<IndexSummary>> ListAsync(params string[] claimed)
    {
        ExamineIndexSource source = CreateSource();
        return await source.GetIndexesAsync(
            new HashSet<string>(claimed),
            TestContext.Current.CancellationToken
        );
    }

    private IIndex AddIndex(string name, long documents, string[] fields)
    {
        IIndex index = Substitute.For<IIndex>();
        index.Name.Returns(name);
        Register(index, documents, fields);
        return index;
    }

    private void Register(IIndex index, long documents, string[] fields)
    {
        IIndexDiagnostics diagnostics = Substitute.For<IIndexDiagnostics>();
        diagnostics.IsHealthy().Returns(Attempt<string?>.Succeed());
        diagnostics.GetDocumentCount().Returns(documents);
        diagnostics.GetFieldNames().Returns(fields);
        diagnostics.Metadata.Returns(
            new Dictionary<string, object?> { ["DefaultAnalyzer"] = "StandardAnalyzer" }
        );
        _diagnosticsFactory.Create(index).Returns(diagnostics);
        _rebuilder.CanRebuild(index.Name).Returns(true);
        _examineManager
            .TryGetIndex(index.Name, out Arg.Any<IIndex>()!)
            .Returns(call =>
            {
                call[1] = index;
                return true;
            });

        IIndex[] existing = [.. _examineManager.Indexes ?? []];
        _examineManager.Indexes.Returns([.. existing, index]);
    }
}
