using System.Collections.Concurrent;
using Examine;
using Examine.Lucene.Providers;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Infrastructure.Examine;
using Umbraco.Community.SearchManager.Core.Rebuilds;
using Umbraco.Community.SearchManager.Features.Indexes;

namespace Umbraco.Community.SearchManager.Features.Rebuilds.External;

/// <summary>
/// Tracks the core Examine rebuilds Search Manager did not start, from start to end, and adds each
/// finished one to the rebuild history (ADR 0022). One per process; <see cref="RecordingIndexRebuilder"/>
/// and <see cref="RecordingIndexPopulator"/> report into it.
/// </summary>
/// <remarks>
/// While a run is tracked its Lucene index is switched to synchronous processing, as Search
/// Manager's own rebuilds are (ADR 0011): every write is committed before the populators return,
/// so the document count read at the end is the rebuilt index's, not a count taken while Examine
/// is still draining its write queue.
/// </remarks>
/// <param name="history">The persisted history.</param>
/// <param name="diagnosticsFactory">Reads document counts.</param>
/// <param name="timeProvider">Supplies the start and end times.</param>
/// <param name="logger">Logs a history that cannot be written.</param>
internal sealed class CoreRebuildRecorder(
    IRebuildHistory history,
    IIndexDiagnosticsFactory diagnosticsFactory,
    TimeProvider timeProvider,
    ILogger<CoreRebuildRecorder> logger
)
{
    private readonly ConcurrentDictionary<string, TrackedRun> _runs = new(
        StringComparer.OrdinalIgnoreCase
    );

    private int _startupSeen;

    /// <summary>Whether a rebuild of the index is being tracked now.</summary>
    /// <param name="indexName">The index.</param>
    /// <returns><see langword="true"/> while a tracked run of it is going.</returns>
    public bool IsTracking(string indexName) => _runs.ContainsKey(indexName);

    /// <summary>
    /// Claims the one startup rebuild of this process. Umbraco asks for it once, shortly after
    /// the site starts; later multi-index rebuilds are not startup rebuilds.
    /// </summary>
    /// <returns><see langword="true"/> the first time only.</returns>
    public bool TryClaimStartup() => Interlocked.Exchange(ref _startupSeen, 1) == 0;

    /// <summary>Starts tracking a rebuild of the index that is about to begin.</summary>
    /// <param name="index">The index.</param>
    /// <param name="trigger">What started it.</param>
    /// <returns><see langword="false"/> when a run of the index is already tracked (nothing changes).</returns>
    public bool Begin(IIndex index, RebuildTrigger trigger)
    {
        ArgumentNullException.ThrowIfNull(index);

        var run = new TrackedRun(index, trigger, timeProvider.GetUtcNow(), ReadCount(index));
        if (!_runs.TryAdd(index.Name, run))
        {
            return false;
        }

        index.IndexingError += run.OnIndexingError;
        run.ThreadingMode = (index as LuceneIndex)?.WithThreadingMode(
            IndexThreadingMode.Synchronous
        );
        return true;
    }

    /// <summary>
    /// Notes why a tracked run is failing. The first reason wins; an index with no tracked run is
    /// ignored.
    /// </summary>
    /// <param name="indexName">The index.</param>
    /// <param name="message">Why it failed.</param>
    public void Fail(string indexName, string message)
    {
        if (_runs.TryGetValue(indexName, out TrackedRun? run))
        {
            run.Fail(message);
        }
    }

    /// <summary>
    /// Stops tracking the run and records it: failed when <paramref name="exception"/> is set or a
    /// failure was noted, otherwise succeeded. Never throws; a history that cannot be written is
    /// logged.
    /// </summary>
    /// <param name="indexName">The index.</param>
    /// <param name="exception">What the rebuild threw, if anything.</param>
    public void End(string indexName, Exception? exception)
    {
        if (!_runs.TryRemove(indexName, out TrackedRun? run))
        {
            return;
        }

        Stop(run);
        if (exception is not null)
        {
            run.Fail(exception.Message);
        }

        try
        {
            // Synchronous processing has committed every write; this waits for the reader to see them.
            (run.Index as LuceneIndex)?.WaitForChanges();

            history.Add(
                new RebuildHistoryEntry(
                    run.Index.Name,
                    ExamineIndexSource.EngineId,
                    Environment.MachineName,
                    run.Trigger,
                    TriggeredBy: null,
                    run.StartedAt,
                    timeProvider.GetUtcNow(),
                    run.Error is null ? RebuildStatus.Succeeded : RebuildStatus.Failed,
                    run.DocumentsBefore,
                    ReadCount(run.Index),
                    run.Error,
                    run.Index is IUmbracoMemberIndex
                )
            );
        }
#pragma warning disable CA1031 // Runs at the end of someone else's rebuild; a history failure is only logged.
        catch (Exception historyException)
#pragma warning restore CA1031
        {
            logger.HistoryNotRecorded(historyException, run.Index.Name);
        }
    }

    /// <summary>
    /// Stops tracking a run that never started (Umbraco refused it), without recording anything.
    /// </summary>
    /// <param name="indexName">The index.</param>
    public void Discard(string indexName)
    {
        if (_runs.TryRemove(indexName, out TrackedRun? run))
        {
            Stop(run);
        }
    }

    /// <summary>Unhooks a run from its index and restores the index's threading mode.</summary>
    /// <param name="run">The run.</param>
    private static void Stop(TrackedRun run)
    {
        run.Index.IndexingError -= run.OnIndexingError;
        run.ThreadingMode?.Dispose();
    }

    /// <summary>Reads an index's document count, or null when it cannot count or reading fails.</summary>
    /// <param name="index">The index.</param>
    /// <returns>The count, or null.</returns>
    private long? ReadCount(IIndex index)
    {
        try
        {
            long count = diagnosticsFactory.Create(index).GetDocumentCount();
            return count < 0 ? null : count;
        }
#pragma warning disable CA1031 // An unreadable count is recorded as unknown.
        catch (Exception)
#pragma warning restore CA1031
        {
            return null;
        }
    }

    /// <summary>One tracked rebuild.</summary>
    /// <param name="index">The index.</param>
    /// <param name="trigger">What started it.</param>
    /// <param name="startedAt">When it started.</param>
    /// <param name="documentsBefore">Documents before, when known.</param>
    private sealed class TrackedRun(
        IIndex index,
        RebuildTrigger trigger,
        DateTimeOffset startedAt,
        long? documentsBefore
    )
    {
        private string? _error;

        /// <summary>The index.</summary>
        public IIndex Index { get; } = index;

        /// <summary>What started the run.</summary>
        public RebuildTrigger Trigger { get; } = trigger;

        /// <summary>When the run started.</summary>
        public DateTimeOffset StartedAt { get; } = startedAt;

        /// <summary>Documents before, when known.</summary>
        public long? DocumentsBefore { get; } = documentsBefore;

        /// <summary>The first failure noted, or null.</summary>
        public string? Error => Volatile.Read(ref _error);

        /// <summary>Restores the index's threading mode at the end; null for a non-Lucene index.</summary>
        public IDisposable? ThreadingMode { get; set; }

        /// <summary>Notes a failure unless one is already noted.</summary>
        /// <param name="message">Why.</param>
        public void Fail(string message) => Interlocked.CompareExchange(ref _error, message, null);

        /// <summary>Lucene reports write failures through this event rather than by throwing.</summary>
        /// <param name="sender">The index.</param>
        /// <param name="args">The error.</param>
        public void OnIndexingError(object? sender, IndexingErrorEventArgs args) =>
            Fail(args.Exception?.Message ?? args.Message);
    }
}
