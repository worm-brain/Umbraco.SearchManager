using Umbraco.Cms.Core.Models.ServerEvents;
using Umbraco.Community.SearchManager.Core.Rebuilds;

namespace Umbraco.Community.SearchManager.Features.Rebuilds.Events;

/// <summary>
/// The server events Search Manager sends when a rebuild run changes. Umbraco's server events
/// carry only a type, a source and a key, so an event says which run changed and how; the browser
/// then reads the runs from <c>GET /rebuilds</c>, which applies the member rule again.
/// </summary>
public static class RebuildEvents
{
    /// <summary>The event source for runs of indexes without member data (Settings access).</summary>
    public const string Source = "SearchManager:Rebuilds";

    /// <summary>The event source for runs of member indexes (Settings and Members access).</summary>
    public const string MemberSource = "SearchManager:MemberRebuilds";

    /// <summary>A run was queued.</summary>
    public const string Queued = "RebuildQueued";

    /// <summary>A run started.</summary>
    public const string Started = "RebuildStarted";

    /// <summary>A running run wrote more documents, so its estimate moved.</summary>
    public const string Progress = "RebuildProgress";

    /// <summary>A run succeeded.</summary>
    public const string Succeeded = "RebuildSucceeded";

    /// <summary>A run failed.</summary>
    public const string Failed = "RebuildFailed";

    /// <summary>A queued run was cancelled.</summary>
    public const string Cancelled = "RebuildCancelled";

    /// <summary>
    /// The event for a run's new state. A running run with no documents written yet has just
    /// started; once the source reports a count, each change is progress.
    /// </summary>
    /// <param name="run">The run, as the queue now holds it.</param>
    /// <returns>The event, keyed by the run id.</returns>
    public static ServerEvent For(RebuildRun run)
    {
        ArgumentNullException.ThrowIfNull(run);

        return new ServerEvent
        {
            EventType = run.Status switch
            {
                RebuildStatus.Queued => Queued,
                RebuildStatus.Running when run.DocumentsWritten is null => Started,
                RebuildStatus.Running => Progress,
                RebuildStatus.Succeeded => Succeeded,
                RebuildStatus.Failed => Failed,
                _ => Cancelled,
            },
            EventSource = run.IsMemberIndex ? MemberSource : Source,
            Key = run.Id,
        };
    }
}
