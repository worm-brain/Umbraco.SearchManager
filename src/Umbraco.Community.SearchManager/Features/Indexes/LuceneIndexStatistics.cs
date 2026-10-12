using Examine.Lucene.Providers;
using Microsoft.Extensions.Logging;

namespace Umbraco.Community.SearchManager.Features.Indexes;

/// <summary>
/// Statistics read straight from a Lucene index that Umbraco's diagnostics do not report.
/// </summary>
/// <param name="DeletedDocumentCount">Documents marked deleted but not yet merged away.</param>
/// <param name="SizeInBytes">Total size of the index's files.</param>
internal sealed record LuceneIndexStatistics(long? DeletedDocumentCount, long? SizeInBytes)
{
    /// <summary>
    /// Reads the statistics, or returns null when the index cannot be read (it is being written,
    /// locked, or uses a directory that cannot list its files). Listing must never fail on this.
    /// </summary>
    /// <param name="index">The Lucene index.</param>
    /// <param name="logger">Logs why a statistic could not be read.</param>
    /// <returns>The statistics, with null for each value that could not be read.</returns>
    public static LuceneIndexStatistics? TryRead(LuceneIndex index, ILogger logger)
    {
        long? deleted = null;
        long? size = null;

        try
        {
            // The searcher's reader is the index as searches see it; Examine refreshes it after commits.
            using var reference = index.Searcher is BaseLuceneSearcher searcher
                ? searcher.GetSearchContext().GetSearcher()
                : null;
            if (reference is not null)
            {
                deleted = reference.IndexSearcher.IndexReader.NumDeletedDocs;
            }
        }
#pragma warning disable CA1031 // Statistics are optional; any failure means "unknown".
        catch (Exception exception)
#pragma warning restore CA1031
        {
            logger.StatisticFailed(exception, "deleted documents", index.Name);
        }

        try
        {
            Lucene.Net.Store.Directory directory = index.GetLuceneDirectory();
            size = directory.ListAll().Sum(file => directory.FileLength(file));
        }
#pragma warning disable CA1031 // Statistics are optional; any failure means "unknown".
        catch (Exception exception)
#pragma warning restore CA1031
        {
            logger.StatisticFailed(exception, "size on disk", index.Name);
        }

        return deleted is null && size is null ? null : new LuceneIndexStatistics(deleted, size);
    }
}
