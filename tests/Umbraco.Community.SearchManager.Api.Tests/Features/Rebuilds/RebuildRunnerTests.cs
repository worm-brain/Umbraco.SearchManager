using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Umbraco.Community.SearchManager.Core.Indexes;
using Umbraco.Community.SearchManager.Core.Rebuilds;
using Umbraco.Community.SearchManager.Features.Rebuilds;

namespace Umbraco.Community.SearchManager.Api.Tests.Features.Rebuilds;

public sealed class RebuildRunnerTests : IDisposable
{
    private readonly RebuildQueue _queue = new(TimeProvider.System);
    private readonly IIndexSource _source = Substitute.For<IIndexSource>();
    private readonly IIndexSourceRegistry _registry = Substitute.For<IIndexSourceRegistry>();

    public RebuildRunnerTests()
    {
        _registry.GetSource("examine").Returns(_source);
        _registry
            .GetIndexesAsync(Arg.Any<CancellationToken>())
            .Returns([IndexSummariesForRunner.Healthy("ProductIndex", documents: 60)]);
    }

    public void Dispose() => _queue.Dispose();

    [Fact]
    public async Task RunAsync_SourceThrows_RecordsTheFailureWithItsMessage()
    {
        // Arrange
        _source
            .RebuildAsync("ProductIndex", Arg.Any<IProgress<long>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Lock obtain timed out"));
        RebuildRun run = Start("ProductIndex");

        // Act
        await Runner().RunAsync(run, TestContext.Current.CancellationToken);

        // Assert
        RebuildRun finished = Assert.Single(_queue.GetRuns());
        Assert.Equal(
            (RebuildStatus.Failed, "Lock obtain timed out"),
            (finished.Status, finished.Error)
        );
    }

    [Fact]
    public async Task RunAsync_SourceSucceeds_RecordsTheNewDocumentCount()
    {
        // Arrange
        RebuildRun run = Start("ProductIndex");

        // Act
        await Runner().RunAsync(run, TestContext.Current.CancellationToken);

        // Assert
        RebuildRun finished = Assert.Single(_queue.GetRuns());
        Assert.Equal((RebuildStatus.Succeeded, 60L), (finished.Status, finished.DocumentsAfter));
    }

    [Fact]
    public async Task RunAsync_SourceReportsProgress_IsRecordedOnTheRun()
    {
        // Arrange
        long? seen = null;
        _source
            .RebuildAsync("ProductIndex", Arg.Any<IProgress<long>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                call.Arg<IProgress<long>>().Report(25);
                seen = _queue.GetRuns()[0].DocumentsWritten;
                return Task.CompletedTask;
            });
        RebuildRun run = Start("ProductIndex");

        // Act
        await Runner().RunAsync(run, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(25, seen);
    }

    [Fact]
    public async Task RunAsync_NoSourceForTheEngine_RecordsAFailure()
    {
        // Arrange
        _registry.GetSource("examine").Returns((IIndexSource?)null);
        RebuildRun run = Start("ProductIndex");

        // Act
        await Runner().RunAsync(run, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(RebuildStatus.Failed, Assert.Single(_queue.GetRuns()).Status);
    }

    private RebuildRunner Runner() => new(_queue, _registry, NullLogger<RebuildRunner>.Instance);

    private RebuildRun Start(string name)
    {
        _queue.Enqueue(
            [new RebuildRequest(name, "examine", 50, 50, false, false)],
            RebuildMode.OneAtATime,
            "Jack"
        );
        return Assert.Single(_queue.StartNext());
    }
}

/// <summary>Index summaries for the runner tests.</summary>
internal static class IndexSummariesForRunner
{
    public static IndexSummary Healthy(string name, long documents) =>
        new(
            name,
            "examine",
            null,
            IndexHealth.Healthy,
            null,
            documents,
            0,
            1,
            1,
            new IndexCapabilities(true, true, true, true),
            null,
            false,
            false,
            new Dictionary<string, string>()
        );
}
