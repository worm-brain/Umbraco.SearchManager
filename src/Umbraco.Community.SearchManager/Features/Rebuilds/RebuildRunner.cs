using Microsoft.Extensions.Logging;
using Umbraco.Community.SearchManager.Core.Indexes;
using Umbraco.Community.SearchManager.Core.Rebuilds;

namespace Umbraco.Community.SearchManager.Features.Rebuilds;

/// <summary>
/// Runs one started rebuild run to its end: asks the index's source to rebuild it, feeds its
/// progress to the queue, and records success (with the new document count) or the failure, in
/// the queue and in the persisted history.
/// </summary>
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
                ?? throw new InvalidOperationException(
                    $"No index source is installed for the {run.Engine} engine."
                );

            logger.RebuildStarting(run.IndexName, run.TriggeredBy);
            await source.RebuildAsync(
                run.IndexName,
                new QueueProgress(queue, run.Id),
                stoppingToken
            );

            IReadOnlyList<IndexSummary> indexes = await registry.GetIndexesAsync(stoppingToken);
            documentsAfter = indexes
                .FirstOrDefault(index =>
                    string.Equals(index.Name, run.IndexName, StringComparison.OrdinalIgnoreCase)
                )
                ?.DocumentCount;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            failure = "The site shut down before the rebuild finished.";
        }
#pragma warning disable CA1031 // Any failure is the run's outcome, recorded for the user.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            logger.RebuildFailed(exception, run.IndexName);
            failure = exception.Message;
        }

        RebuildRun finished = queue.Complete(run.Id, documentsAfter, failure);
        logger.RebuildFinished(run.IndexName, finished.Status);
        Record(finished);
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
