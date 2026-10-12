using Microsoft.Extensions.Time.Testing;
using Umbraco.Community.SearchManager.Core.Rebuilds;

namespace Umbraco.Community.SearchManager.Core.Tests.Rebuilds;

public sealed class RebuildQueueTests : IDisposable
{
    private readonly FakeTimeProvider _time = new(
        new DateTimeOffset(2026, 10, 12, 9, 0, 0, TimeSpan.Zero)
    );
    private readonly RebuildQueue _queue;

    public RebuildQueueTests() => _queue = new RebuildQueue(_time);

    public void Dispose() => _queue.Dispose();

    [Fact]
    public void StartNext_OneAtATime_StartsOnlyTheOldestQueuedRun()
    {
        // Arrange
        Enqueue(RebuildMode.OneAtATime, "ExternalIndex", "InternalIndex", "Umb_Content");

        // Act
        IReadOnlyList<RebuildRun> started = _queue.StartNext();

        // Assert
        Assert.Equal(["ExternalIndex"], started.Select(run => run.IndexName));
    }

    [Fact]
    public void StartNext_AfterTheRunningRunCompletes_StartsTheNextInQueueOrder()
    {
        // Arrange
        Enqueue(RebuildMode.OneAtATime, "ExternalIndex", "InternalIndex");
        RebuildRun first = Assert.Single(_queue.StartNext());
        _queue.Complete(first.Id, documentsAfter: 10, failure: null);

        // Act
        IReadOnlyList<RebuildRun> started = _queue.StartNext();

        // Assert
        Assert.Equal(["InternalIndex"], started.Select(run => run.IndexName));
    }

    [Fact]
    public void StartNext_WhileAOneAtATimeRunIsRunning_StartsNothingElseOneAtATime()
    {
        // Arrange
        Enqueue(RebuildMode.OneAtATime, "ExternalIndex", "InternalIndex");
        _queue.StartNext();

        // Act
        IReadOnlyList<RebuildRun> started = _queue.StartNext();

        // Assert
        Assert.Empty(started);
    }

    [Fact]
    public void StartNext_AllAtOnce_StartsEveryQueuedRun()
    {
        // Arrange
        Enqueue(RebuildMode.AllAtOnce, "ExternalIndex", "InternalIndex", "Umb_Content");

        // Act
        IReadOnlyList<RebuildRun> started = _queue.StartNext();

        // Assert
        Assert.Equal(3, started.Count);
    }

    [Fact]
    public void Enqueue_IndexAlreadyQueued_IsRefusedWithReason()
    {
        // Arrange
        Enqueue(RebuildMode.OneAtATime, "ExternalIndex");

        // Act
        EnqueueResult result = Enqueue(RebuildMode.OneAtATime, "externalindex", "InternalIndex");

        // Assert
        Assert.Equal(
            "InternalIndex | externalindex",
            $"{string.Join(",", result.Queued.Select(run => run.IndexName))} | "
                + string.Join(",", result.Refused.Select(refusal => refusal.IndexName))
        );
    }

    [Fact]
    public void Enqueue_IndexRunning_IsRefused()
    {
        // Arrange
        Enqueue(RebuildMode.OneAtATime, "ExternalIndex");
        _queue.StartNext();

        // Act
        EnqueueResult result = Enqueue(RebuildMode.OneAtATime, "ExternalIndex");

        // Assert
        Assert.Single(result.Refused);
    }

    [Fact]
    public void Enqueue_IndexFinished_IsQueuedAgain()
    {
        // Arrange
        Enqueue(RebuildMode.OneAtATime, "ExternalIndex");
        _queue.Complete(_queue.StartNext()[0].Id, 1, null);

        // Act
        EnqueueResult result = Enqueue(RebuildMode.OneAtATime, "ExternalIndex");

        // Assert
        Assert.Single(result.Queued);
    }

    [Fact]
    public void Cancel_QueuedRun_IsCancelledAndNeverStarts()
    {
        // Arrange
        RebuildRun run = Enqueue(RebuildMode.OneAtATime, "ExternalIndex").Queued[0];

        // Act
        CancelResult result = _queue.Cancel(run.Id);

        // Assert
        Assert.Equal(
            (CancelResult.Cancelled, RebuildStatus.Cancelled, 0),
            (result, _queue.GetRuns()[0].Status, _queue.StartNext().Count)
        );
    }

    [Fact]
    public void Cancel_RunningRun_IsRefused()
    {
        // Arrange
        Enqueue(RebuildMode.OneAtATime, "ExternalIndex");
        RebuildRun running = _queue.StartNext()[0];

        // Act
        CancelResult result = _queue.Cancel(running.Id);

        // Assert
        Assert.Equal(CancelResult.NotQueued, result);
    }

    [Fact]
    public void Cancel_UnknownRun_IsNotFound()
    {
        // Act
        CancelResult result = _queue.Cancel(Guid.NewGuid());

        // Assert
        Assert.Equal(CancelResult.NotFound, result);
    }

    [Fact]
    public void Complete_WithError_RecordsFailureAndEndTime()
    {
        // Arrange
        Enqueue(RebuildMode.OneAtATime, "ProductIndex");
        RebuildRun running = _queue.StartNext()[0];
        _time.Advance(TimeSpan.FromSeconds(22));

        // Act
        RebuildRun finished = _queue.Complete(
            running.Id,
            documentsAfter: null,
            "Lock obtain timed out"
        );

        // Assert
        Assert.Equal(
            (RebuildStatus.Failed, "Lock obtain timed out", TimeSpan.FromSeconds(22)),
            (finished.Status, finished.Error, finished.EndedAt - finished.StartedAt)
        );
    }

    [Fact]
    public void ReportProgress_RunningRun_RecordsDocumentsWritten()
    {
        // Arrange
        Enqueue(RebuildMode.OneAtATime, "ExternalIndex");
        RebuildRun running = _queue.StartNext()[0];

        // Act
        _queue.ReportProgress(running.Id, 1200);

        // Assert
        Assert.Equal(1200, _queue.GetRuns()[0].DocumentsWritten);
    }

    [Fact]
    public void ClearFinished_KeepsActiveRuns()
    {
        // Arrange
        Enqueue(RebuildMode.OneAtATime, "ExternalIndex", "InternalIndex");
        _queue.Complete(_queue.StartNext()[0].Id, 1, null);

        // Act
        _queue.ClearFinished();

        // Assert
        Assert.Equal(["InternalIndex"], _queue.GetRuns().Select(run => run.IndexName));
    }

    [Fact]
    public void RunChanged_Enqueue_IsRaisedForEachQueuedRun()
    {
        // Arrange
        var changes = new List<RebuildRun>();
        _queue.RunChanged += (_, run) => changes.Add(run);

        // Act
        Enqueue(RebuildMode.OneAtATime, "ExternalIndex", "InternalIndex");

        // Assert
        Assert.Equal(2, changes.Count);
    }

    private EnqueueResult Enqueue(RebuildMode mode, params string[] names) =>
        _queue.Enqueue(
            names.Select(name => new RebuildRequest(name, "examine", 100, 100, false, false)),
            mode,
            "Jack"
        );
}
