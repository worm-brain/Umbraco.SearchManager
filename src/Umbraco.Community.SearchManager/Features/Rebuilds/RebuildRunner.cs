using Microsoft.Extensions.Logging;
using Umbraco.Community.SearchManager.Core.Indexes;
using Umbraco.Community.SearchManager.Core.Rebuilds;

namespace Umbraco.Community.SearchManager.Features.Rebuilds;

/// <summary>
/// Runs one started rebuild run to its end: asks the index's source to rebuild it, feeds its
/// progress to the queue, and records success (with the new document count) or the failure.
/// </summary>
internal sealed class RebuildRunner(
    IRebuildQueue queue,
    IIndexSourceRegistry registry,
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
