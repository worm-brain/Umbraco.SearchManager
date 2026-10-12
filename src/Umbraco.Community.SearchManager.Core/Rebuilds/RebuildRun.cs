using System.Text.Json.Serialization;

namespace Umbraco.Community.SearchManager.Core.Rebuilds;

/// <summary>Where a rebuild run is in its life.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<RebuildStatus>))]
public enum RebuildStatus
{
    /// <summary>Waiting for its turn.</summary>
    Queued,

    /// <summary>Rebuilding now.</summary>
    Running,

    /// <summary>Finished, and the rebuilt index is committed and searchable.</summary>
    Succeeded,

    /// <summary>Stopped with an error.</summary>
    Failed,

    /// <summary>Cancelled before it started.</summary>
    Cancelled,
}

/// <summary>How the runs of one request are scheduled.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<RebuildMode>))]
public enum RebuildMode
{
    /// <summary>One at a time, after any other one-at-a-time run (the default; less load on the site).</summary>
    OneAtATime,

    /// <summary>Each run starts straight away.</summary>
    AllAtOnce,
}

/// <summary>One rebuild of one index, as the Rebuilds panel shows it.</summary>
/// <param name="Id">The run's id.</param>
/// <param name="IndexName">The index.</param>
/// <param name="Engine">The engine that owns the index.</param>
/// <param name="Mode">How the run is scheduled.</param>
/// <param name="Status">Where the run is.</param>
/// <param name="TriggeredBy">Who or what asked for it (a user's name, for example).</param>
/// <param name="QueuedAt">When it was queued.</param>
/// <param name="StartedAt">When it started, once it has.</param>
/// <param name="EndedAt">When it ended, once it has.</param>
/// <param name="DocumentsBefore">Documents in the index when it was queued, when known.</param>
/// <param name="DocumentsAfter">Documents in the index after a successful run, when known.</param>
/// <param name="DocumentsWritten">Documents in the copy being built so far, when the source reports it.</param>
/// <param name="EstimatedTotal">
/// The document count the run is expected to reach (the previous run's final count), for an
/// estimated percentage; null when there is nothing to estimate from.
/// </param>
/// <param name="ZeroDowntime">Whether search keeps working while it runs.</param>
/// <param name="IsMemberIndex">Whether the index holds member data.</param>
/// <param name="Error">Why a failed run failed.</param>
public sealed record RebuildRun(
    Guid Id,
    string IndexName,
    string Engine,
    RebuildMode Mode,
    RebuildStatus Status,
    string TriggeredBy,
    DateTimeOffset QueuedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? EndedAt,
    long? DocumentsBefore,
    long? DocumentsAfter,
    long? DocumentsWritten,
    long? EstimatedTotal,
    bool ZeroDowntime,
    bool IsMemberIndex,
    string? Error
)
{
    /// <summary>Whether the run is queued or running.</summary>
    [JsonIgnore]
    public bool IsActive => Status is RebuildStatus.Queued or RebuildStatus.Running;
}

/// <summary>An index to rebuild.</summary>
/// <param name="IndexName">The index.</param>
/// <param name="Engine">Its engine.</param>
/// <param name="DocumentsBefore">Its document count now, when known.</param>
/// <param name="EstimatedTotal">The count the run is expected to reach, when known.</param>
/// <param name="ZeroDowntime">Whether search keeps working while it runs.</param>
/// <param name="IsMemberIndex">Whether it holds member data.</param>
public sealed record RebuildRequest(
    string IndexName,
    string Engine,
    long? DocumentsBefore,
    long? EstimatedTotal,
    bool ZeroDowntime,
    bool IsMemberIndex
);

/// <summary>An index the queue would not take, and why.</summary>
/// <param name="IndexName">The index.</param>
/// <param name="Reason">Why, in words a user can act on.</param>
public sealed record RebuildRefusal(string IndexName, string Reason);

/// <summary>What happened to a request to queue rebuilds.</summary>
/// <param name="Queued">The runs that were queued.</param>
/// <param name="Refused">The indexes that were not, with the reason.</param>
public sealed record EnqueueResult(
    IReadOnlyList<RebuildRun> Queued,
    IReadOnlyList<RebuildRefusal> Refused
);

/// <summary>What happened to a request to cancel a run.</summary>
public enum CancelResult
{
    /// <summary>The run was queued and is now cancelled.</summary>
    Cancelled,

    /// <summary>No run has that id.</summary>
    NotFound,

    /// <summary>The run has started or finished, so it cannot be cancelled.</summary>
    NotQueued,
}
