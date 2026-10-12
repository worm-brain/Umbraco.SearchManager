using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Models.ServerEvents;
using Umbraco.Cms.Core.ServerEvents;
using Umbraco.Community.SearchManager.Core.Rebuilds;

namespace Umbraco.Community.SearchManager.Features.Rebuilds.Events;

/// <summary>
/// Sends a server event through <see cref="IServerEventRouter"/> each time the rebuild queue
/// changes a run, so the browser updates without polling. Routed (never broadcast) events reach
/// only the users <see cref="RebuildEventAuthorizer"/> admits. Progress is sent at most once per
/// <see cref="ProgressInterval"/> per run; every other change is sent at once.
/// </summary>
/// <param name="queue">The rebuild queue.</param>
/// <param name="router">Umbraco's server event router.</param>
/// <param name="timeProvider">Drives the progress throttle.</param>
/// <param name="logger">Logs events that could not be sent.</param>
internal sealed class RebuildEventBroadcaster(
    IRebuildQueue queue,
    IServerEventRouter router,
    TimeProvider timeProvider,
    ILogger<RebuildEventBroadcaster> logger
) : IHostedService
{
    /// <summary>The shortest gap between two progress events of one run.</summary>
    public static readonly TimeSpan ProgressInterval = TimeSpan.FromSeconds(2);

    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _lastProgress = new();

    /// <summary>Starts listening to the queue.</summary>
    /// <param name="cancellationToken">Unused.</param>
    /// <returns>A completed task.</returns>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        queue.RunChanged += OnRunChanged;
        return Task.CompletedTask;
    }

    /// <summary>Stops listening to the queue.</summary>
    /// <param name="cancellationToken">Unused.</param>
    /// <returns>A completed task.</returns>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        queue.RunChanged -= OnRunChanged;
        return Task.CompletedTask;
    }

    /// <summary>Sends the event for a changed run, unless it is progress inside the throttle.</summary>
    /// <param name="sender">The queue.</param>
    /// <param name="run">The run.</param>
    private void OnRunChanged(object? sender, RebuildRun run)
    {
        ServerEvent serverEvent = RebuildEvents.For(run);
        if (serverEvent.EventType is RebuildEvents.Progress && !ProgressDue(run.Id))
        {
            return;
        }

        if (!run.IsActive)
        {
            _lastProgress.TryRemove(run.Id, out _);
        }

        // The queue raises its event synchronously; sending must not hold up the rebuild.
        _ = SendAsync(serverEvent);
    }

    /// <summary>Whether a progress event for the run may be sent now, recording it if so.</summary>
    /// <param name="id">The run.</param>
    /// <returns>Whether at least <see cref="ProgressInterval"/> has passed since the last one.</returns>
    private bool ProgressDue(Guid id)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        if (_lastProgress.TryGetValue(id, out DateTimeOffset last) && now - last < ProgressInterval)
        {
            return false;
        }

        _lastProgress[id] = now;
        return true;
    }

    /// <summary>Routes one event; a failure (no hub, a dropped connection) is only logged.</summary>
    /// <param name="serverEvent">The event.</param>
    /// <returns>A task that never faults.</returns>
    private async Task SendAsync(ServerEvent serverEvent)
    {
        try
        {
            await router.RouteEventAsync(serverEvent);
        }
#pragma warning disable CA1031 // Browsers fall back to polling; a lost event is not an error for the run.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            logger.RebuildEventNotSent(exception, serverEvent.EventType);
        }
    }
}
