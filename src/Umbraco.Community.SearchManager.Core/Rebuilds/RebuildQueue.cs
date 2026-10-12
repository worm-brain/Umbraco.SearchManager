using System.Diagnostics.CodeAnalysis;

namespace Umbraco.Community.SearchManager.Core.Rebuilds;

/// <summary>
/// The server-side rebuild queue: which runs are queued, running and finished on this server.
/// A worker takes startable runs with <see cref="StartNext"/>, runs them, and reports back with
/// <see cref="ReportProgress"/> and <see cref="Complete"/>.
/// </summary>
[SuppressMessage(
    "Naming",
    "CA1711",
    Justification = "It is a queue: the domain term the UI and brief use."
)]
public interface IRebuildQueue
{
    /// <summary>Raised after any run changes (queued, started, progressed, finished, cancelled).</summary>
    event EventHandler<RebuildRun>? RunChanged;

    /// <summary>
    /// Queues a run per request, refusing any index that already has a queued or running run.
    /// </summary>
    /// <param name="requests">The indexes to rebuild.</param>
    /// <param name="mode">How to schedule them.</param>
    /// <param name="triggeredBy">Who asked, for the history.</param>
    /// <returns>The queued runs and the refusals.</returns>
    EnqueueResult Enqueue(
        IEnumerable<RebuildRequest> requests,
        RebuildMode mode,
        string triggeredBy
    );

    /// <summary>Lists every run the queue holds, oldest first.</summary>
    /// <returns>The runs.</returns>
    IReadOnlyList<RebuildRun> GetRuns();

    /// <summary>Cancels a queued run. A running run cannot be cancelled.</summary>
    /// <param name="id">The run.</param>
    /// <returns>What happened.</returns>
    CancelResult Cancel(Guid id);

    /// <summary>Forgets every finished run.</summary>
    /// <returns>How many runs were removed.</returns>
    int ClearFinished();

    /// <summary>
    /// Marks the runs that may start now as running and returns them: every queued all-at-once
    /// run, and the oldest queued one-at-a-time run when no one-at-a-time run is running.
    /// </summary>
    /// <returns>The runs to start, possibly none.</returns>
    IReadOnlyList<RebuildRun> StartNext();

    /// <summary>Records how many documents a running run has written so far.</summary>
    /// <param name="id">The run.</param>
    /// <param name="documentsWritten">Documents in the copy being built.</param>
    void ReportProgress(Guid id, long documentsWritten);

    /// <summary>Finishes a running run.</summary>
    /// <param name="id">The run.</param>
    /// <param name="documentsAfter">Documents in the index afterwards, when known.</param>
    /// <param name="failure">Null for success; otherwise why it failed.</param>
    /// <returns>The finished run.</returns>
    /// <exception cref="InvalidOperationException">The run is not running.</exception>
    RebuildRun Complete(Guid id, long? documentsAfter, string? failure);

    /// <summary>Waits until something may have become startable (a run was queued or finished).</summary>
    /// <param name="cancellationToken">Stops waiting.</param>
    /// <returns>A task that completes on the next change.</returns>
    Task WaitForWorkAsync(CancellationToken cancellationToken);
}

/// <inheritdoc cref="IRebuildQueue" />
/// <remarks>
/// Status lives in memory for the life of the process; finished runs are kept until cleared, up
/// to <see cref="MaxFinishedRuns"/>, oldest dropped first. Thread-safe: every change happens under
/// one lock and <see cref="RunChanged"/> is raised outside it.
/// </remarks>
/// <param name="timeProvider">Supplies the run timestamps.</param>
[SuppressMessage(
    "Naming",
    "CA1711",
    Justification = "It is a queue: the domain term the UI and brief use."
)]
public sealed class RebuildQueue(TimeProvider timeProvider) : IRebuildQueue, IDisposable
{
    /// <summary>How many finished runs are kept in memory.</summary>
    public const int MaxFinishedRuns = 100;

    private readonly object _lock = new();
    private readonly List<RebuildRun> _runs = [];
    private readonly SemaphoreSlim _signal = new(0);

    /// <inheritdoc />
    public event EventHandler<RebuildRun>? RunChanged;

    /// <inheritdoc />
    public EnqueueResult Enqueue(
        IEnumerable<RebuildRequest> requests,
        RebuildMode mode,
        string triggeredBy
    )
    {
        ArgumentNullException.ThrowIfNull(requests);

        var queued = new List<RebuildRun>();
        var refused = new List<RebuildRefusal>();
        lock (_lock)
        {
            foreach (RebuildRequest request in requests)
            {
                bool busy =
                    _runs.Any(run => run.IsActive && SameIndex(run.IndexName, request.IndexName))
                    || queued.Any(run => SameIndex(run.IndexName, request.IndexName));
                if (busy)
                {
                    refused.Add(
                        new RebuildRefusal(
                            request.IndexName,
                            "A rebuild of this index is already queued or running"
                        )
                    );
                    continue;
                }

                var run = new RebuildRun(
                    Id: Guid.NewGuid(),
                    IndexName: request.IndexName,
                    Engine: request.Engine,
                    Mode: mode,
                    Status: RebuildStatus.Queued,
                    TriggeredBy: triggeredBy,
                    QueuedAt: timeProvider.GetUtcNow(),
                    StartedAt: null,
                    EndedAt: null,
                    DocumentsBefore: request.DocumentsBefore,
                    DocumentsAfter: null,
                    DocumentsWritten: null,
                    EstimatedTotal: request.EstimatedTotal,
                    ZeroDowntime: request.ZeroDowntime,
                    IsMemberIndex: request.IsMemberIndex,
                    Error: null
                );
                _runs.Add(run);
                queued.Add(run);
            }
        }

        foreach (RebuildRun run in queued)
        {
            RunChanged?.Invoke(this, run);
        }

        if (queued.Count > 0)
        {
            _signal.Release();
        }

        return new EnqueueResult(queued, refused);
    }

    /// <inheritdoc />
    public IReadOnlyList<RebuildRun> GetRuns()
    {
        lock (_lock)
        {
            return [.. _runs];
        }
    }

    /// <inheritdoc />
    public CancelResult Cancel(Guid id)
    {
        RebuildRun cancelled;
        lock (_lock)
        {
            int index = _runs.FindIndex(run => run.Id == id);
            if (index < 0)
            {
                return CancelResult.NotFound;
            }

            if (_runs[index].Status is not RebuildStatus.Queued)
            {
                return CancelResult.NotQueued;
            }

            cancelled = _runs[index] with
            {
                Status = RebuildStatus.Cancelled,
                EndedAt = timeProvider.GetUtcNow(),
            };
            _runs[index] = cancelled;
            TrimFinished();
        }

        RunChanged?.Invoke(this, cancelled);
        return CancelResult.Cancelled;
    }

    /// <inheritdoc />
    public int ClearFinished()
    {
        lock (_lock)
        {
            return _runs.RemoveAll(run => !run.IsActive);
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<RebuildRun> StartNext()
    {
        var started = new List<RebuildRun>();
        lock (_lock)
        {
            bool sequentialRunning = _runs.Any(run =>
                run is { Status: RebuildStatus.Running, Mode: RebuildMode.OneAtATime }
            );
            for (int i = 0; i < _runs.Count; i++)
            {
                RebuildRun run = _runs[i];
                if (run.Status is not RebuildStatus.Queued)
                {
                    continue;
                }

                if (run.Mode is RebuildMode.OneAtATime)
                {
                    if (sequentialRunning)
                    {
                        continue;
                    }

                    sequentialRunning = true;
                }

                _runs[i] = run with
                {
                    Status = RebuildStatus.Running,
                    StartedAt = timeProvider.GetUtcNow(),
                };
                started.Add(_runs[i]);
            }
        }

        foreach (RebuildRun run in started)
        {
            RunChanged?.Invoke(this, run);
        }

        return started;
    }

    /// <inheritdoc />
    public void ReportProgress(Guid id, long documentsWritten)
    {
        RebuildRun? changed = null;
        lock (_lock)
        {
            int index = _runs.FindIndex(run => run.Id == id);
            if (
                index >= 0
                && _runs[index].Status is RebuildStatus.Running
                && _runs[index].DocumentsWritten != documentsWritten
            )
            {
                changed = _runs[index] = _runs[index] with { DocumentsWritten = documentsWritten };
            }
        }

        if (changed is not null)
        {
            RunChanged?.Invoke(this, changed);
        }
    }

    /// <inheritdoc />
    public RebuildRun Complete(Guid id, long? documentsAfter, string? failure)
    {
        RebuildRun finished;
        lock (_lock)
        {
            int index = _runs.FindIndex(run => run.Id == id);
            if (index < 0 || _runs[index].Status is not RebuildStatus.Running)
            {
                throw new InvalidOperationException($"Run {id} is not running.");
            }

            finished = _runs[index] with
            {
                Status = failure is null ? RebuildStatus.Succeeded : RebuildStatus.Failed,
                EndedAt = timeProvider.GetUtcNow(),
                DocumentsAfter = documentsAfter,
                Error = failure,
            };
            _runs[index] = finished;
            TrimFinished();
        }

        RunChanged?.Invoke(this, finished);
        _signal.Release();
        return finished;
    }

    /// <inheritdoc />
    public Task WaitForWorkAsync(CancellationToken cancellationToken) =>
        _signal.WaitAsync(cancellationToken);

    /// <summary>Releases the wake-up signal.</summary>
    public void Dispose() => _signal.Dispose();

    /// <summary>Drops the oldest finished runs beyond <see cref="MaxFinishedRuns"/>. Call under the lock.</summary>
    private void TrimFinished()
    {
        int excess = _runs.Count(run => !run.IsActive) - MaxFinishedRuns;
        for (int i = 0; i < _runs.Count && excess > 0; )
        {
            if (!_runs[i].IsActive)
            {
                _runs.RemoveAt(i);
                excess--;
            }
            else
            {
                i++;
            }
        }
    }

    /// <summary>Index names compare case-insensitively, as Examine's do.</summary>
    /// <param name="a">One name.</param>
    /// <param name="b">The other.</param>
    /// <returns>Whether they name the same index.</returns>
    private static bool SameIndex(string a, string b) =>
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
