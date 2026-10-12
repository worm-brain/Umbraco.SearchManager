using Examine;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Umbraco.Cms.Core;
using Umbraco.Cms.Infrastructure.Examine;
using Umbraco.Cms.Infrastructure.Services;
using Umbraco.Community.SearchManager.Core.Indexes;
using Umbraco.Community.SearchManager.Features.Indexes;

namespace Umbraco.Community.SearchManager.Api.Tests.Features.Indexes;

public class ExamineIndexSourceTests
{
    private readonly IExamineManager _examineManager = Substitute.For<IExamineManager>();
    private readonly IIndexDiagnosticsFactory _diagnosticsFactory =
        Substitute.For<IIndexDiagnosticsFactory>();
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

    private async Task<IReadOnlyList<IndexSummary>> ListAsync(params string[] claimed)
    {
        var source = new ExamineIndexSource(
            _examineManager,
            _diagnosticsFactory,
            _rebuilder,
            NullLogger<ExamineIndexSource>.Instance
        );
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

        IIndex[] existing = [.. _examineManager.Indexes ?? []];
        _examineManager.Indexes.Returns([.. existing, index]);
    }
}
