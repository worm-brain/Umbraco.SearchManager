using Microsoft.Extensions.Logging;
using Umbraco.Community.SearchManager.Core.Indexes;
using Umbraco.Community.SearchManager.Core.Rebuilds;

namespace Umbraco.Community.SearchManager.Features.Rebuilds;

/// <summary>
/// Runs one started rebuild run to its end: asks the index's source to rebuild it, feeds its
/// progress to the queue, and records success (with the new document count) or the failure, in
/// the queue and in the persisted history.
/// </summary>
/// <remarks>
/// A failed run keeps the document count the index was left with, unless the source refused to
/// start (<see cref="RebuildRefusedException"/>): a failed run with a count is one that failed
/// part-way, which is how an index is known to be incomplete.
/// </remarks>
/// <param name="queue">The queue the run belongs to.</param>
/// <param name="registry">Finds the run's index source.</param>
/// <param name="history">The persisted history.</param>
/// <param name="logger">Logs the run.</param>
internal sealed class RebuildRunner(
    IRebuildQueue queue,
    IIndexSourceRegistry registry,
    IRebuildHistory history,
    ILogger<RebuildRunner> logger
)
{
    /// <summary>Runs <paramref name="run"/>, which <see cref="IRebuildQueue.StartNext"/> has just started. Never throws.</summary>
    /// <param name="run">The run.</param>
    /// <param name="stoppingToken">Signals that the site is shutting down.</param>
    /// <returns>A task that completes once the run is finished in the queue.</returns>
    public async Task RunAsync(RebuildRun run, CancellationToken stoppingToken)
    {
        string? failure = null;
        long? documentsAfter = null;
        try
        {
            IIndexSource source =
                registry.GetSource(run.Engine)
                ?? throw new RebuildRefusedException(
                    $"No index source is installed for the {run.Engine} engine."
                );

            logger.RebuildStarting(run.IndexName, run.TriggeredBy);
            await source.RebuildAsync(
                run.IndexName,
                new QueueProgress(queue, run.Id),
                stoppingToken
            );

            documentsAfter = await ReadDocumentCountAsync(run.IndexName, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            failure = "The site shut down before the rebuild finished.";
            documentsAfter = await TryReadDocumentCountAsync(run.IndexName);
        }
        catch (RebuildRefusedException exception)
        {
            logger.RebuildFailed(exception, run.IndexName);
            failure = exception.Message;
        }
#pragma warning disable CA1031 // Any failure is the run's outcome, recorded for the user.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            logger.RebuildFailed(exception, run.IndexName);
            failure = exception.Message;
            documentsAfter = await TryReadDocumentCountAsync(run.IndexName);
        }

        RebuildRun finished = queue.Complete(run.Id, documentsAfter, failure);
        logger.RebuildFinished(run.IndexName, finished.Status);
        Record(finished);
    }

    /// <summary>Reads the index's current document count through its source.</summary>
    /// <param name="indexName">The index.</param>
    /// <param name="cancellationToken">Stops reading.</param>
    /// <returns>The count, or null when the index is gone or its source cannot count.</returns>
    private async Task<long?> ReadDocumentCountAsync(
        string indexName,
        CancellationToken cancellationToken
    )
    {
        IReadOnlyList<IndexSummary> indexes = await registry.GetIndexesAsync(cancellationToken);
        return indexes
            .FirstOrDefault(index =>
                string.Equals(index.Name, indexName, StringComparison.OrdinalIgnoreCase)
            )
            ?.DocumentCount;
    }

    /// <summary>
    /// Reads what a failed rebuild left in the index. Runs during shutdown too, so it takes no
    /// token, and anything it throws means "unknown".
    /// </summary>
    /// <param name="indexName">The index.</param>
    /// <returns>The count, or null.</returns>
    private async Task<long?> TryReadDocumentCountAsync(string indexName)
    {
        try
        {
            return await ReadDocumentCountAsync(indexName, CancellationToken.None);
        }
#pragma warning disable CA1031 // The run has failed already; an unreadable count is recorded as unknown.
        catch (Exception)
#pragma warning restore CA1031
        {
            return null;
        }
    }

    /// <summary>
    /// Adds a finished run to the persisted history. A history that cannot be written is logged
    /// and never changes the run's outcome.
    /// </summary>
    /// <param name="finished">The finished run.</param>
    private void Record(RebuildRun finished)
    {
        try
        {
            history.Add(
                new RebuildHistoryEntry(
                    finished.IndexName,
                    finished.Engine,
                    Environment.MachineName,
                    RebuildTrigger.User,
                    finished.TriggeredBy,
                    finished.StartedAt ?? finished.QueuedAt,
                    finished.EndedAt ?? finished.QueuedAt,
                    finished.Status,
                    finished.DocumentsBefore,
                    finished.DocumentsAfter,
                    finished.Error,
                    finished.IsMemberIndex
                )
            );
        }
#pragma warning disable CA1031 // The rebuild itself is done; a history failure is only logged.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            logger.HistoryNotRecorded(exception, finished.IndexName);
        }
    }

    /// <summary>Forwards a source's document counts to the queue, synchronously (unlike <see cref="Progress{T}"/>).</summary>
    /// <param name="queue">The queue.</param>
    /// <param name="id">The run.</param>
    private sealed class QueueProgress(IRebuildQueue queue, Guid id) : IProgress<long>
    {
        /// <inheritdoc />
        public void Report(long value) => queue.ReportProgress(id, value);
    }
}
