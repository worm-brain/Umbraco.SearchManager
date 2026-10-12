using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Umbraco.Cms.Core.Models.ServerEvents;
using Umbraco.Cms.Core.ServerEvents;
using Umbraco.Community.SearchManager.Core.Rebuilds;
using Umbraco.Community.SearchManager.Features.Rebuilds.Events;

namespace Umbraco.Community.SearchManager.Api.Tests.Features.Rebuilds.Events;

public sealed class RebuildEventBroadcasterTests : IAsyncLifetime
{
    private readonly FakeTimeProvider _time = new();
    private readonly RebuildQueue _queue;
    private readonly IServerEventRouter _router = Substitute.For<IServerEventRouter>();
    private readonly RebuildEventBroadcaster _broadcaster;
    private readonly List<ServerEvent> _sent = [];

    public RebuildEventBroadcasterTests()
    {
        _queue = new RebuildQueue(_time);
        _router.RouteEventAsync(Arg.Do<ServerEvent>(_sent.Add)).Returns(Task.CompletedTask);
        _broadcaster = new RebuildEventBroadcaster(
            _queue,
            _router,
            _time,
            NullLogger<RebuildEventBroadcaster>.Instance
        );
    }

    public async ValueTask InitializeAsync() =>
        await _broadcaster.StartAsync(TestContext.Current.CancellationToken);

    public async ValueTask DisposeAsync()
    {
        await _broadcaster.StopAsync(CancellationToken.None);
        _queue.Dispose();
    }

    [Fact]
    public void RunChanged_QueuedAndStarted_RoutesAnEventForEach()
    {
        // Act
        Queue("ProductIndex");
        _queue.StartNext();

        // Assert
        Assert.Equal(
            [RebuildEvents.Queued, RebuildEvents.Started],
            _sent.Select(serverEvent => serverEvent.EventType)
        );
    }

    [Fact]
    public void RunChanged_ProgressWithinTheThrottle_RoutesOnlyTheFirst()
    {
        // Arrange
        Queue("ProductIndex");
        RebuildRun run = Assert.Single(_queue.StartNext());

        // Act
        _queue.ReportProgress(run.Id, 10);
        _time.Advance(TimeSpan.FromMilliseconds(500));
        _queue.ReportProgress(run.Id, 20);

        // Assert
        Assert.Single(_sent, serverEvent => serverEvent.EventType == RebuildEvents.Progress);
    }

    [Fact]
    public void RunChanged_ProgressAfterTheThrottle_RoutesItAgain()
    {
        // Arrange
        Queue("ProductIndex");
        RebuildRun run = Assert.Single(_queue.StartNext());

        // Act
        _queue.ReportProgress(run.Id, 10);
        _time.Advance(RebuildEventBroadcaster.ProgressInterval);
        _queue.ReportProgress(run.Id, 20);

        // Assert
        Assert.Equal(
            2,
            _sent.Count(serverEvent => serverEvent.EventType == RebuildEvents.Progress)
        );
    }

    [Fact]
    public void RunChanged_Finished_IsNeverThrottled()
    {
        // Arrange
        Queue("ProductIndex");
        RebuildRun run = Assert.Single(_queue.StartNext());
        _queue.ReportProgress(run.Id, 10);

        // Act
        _queue.Complete(run.Id, 10, failure: null);

        // Assert
        Assert.Equal(RebuildEvents.Succeeded, _sent[^1].EventType);
    }

    [Fact]
    public async Task StopAsync_AfterStopping_RoutesNothing()
    {
        // Arrange
        await _broadcaster.StopAsync(TestContext.Current.CancellationToken);

        // Act
        Queue("ProductIndex");

        // Assert
        Assert.Empty(_sent);
    }

    private void Queue(string name) =>
        _queue.Enqueue(
            [new RebuildRequest(name, "examine", 1, 1, false, false)],
            RebuildMode.OneAtATime,
            "Jack"
        );
}
