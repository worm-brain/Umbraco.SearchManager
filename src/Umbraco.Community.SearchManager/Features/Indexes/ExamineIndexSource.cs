using Examine;
using Examine.Lucene.Providers;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core;
using Umbraco.Cms.Infrastructure.Examine;
using Umbraco.Cms.Infrastructure.Models;
using Umbraco.Cms.Infrastructure.Services;
using Umbraco.Community.SearchManager.Core.Indexes;
using Umbraco.Community.SearchManager.Features.Rebuilds;
using Umbraco.Community.SearchManager.Features.Rebuilds.External;

namespace Umbraco.Community.SearchManager.Features.Indexes;

/// <summary>
/// Lists the core Examine indexes (everything in <see cref="IExamineManager"/>), with the same
/// health rules as the core Examine Management dashboard plus the Lucene statistics it does not
/// show: deleted documents and size on disk.
/// </summary>
/// <remarks>
/// Health follows Umbraco's <c>IndexPresentationFactory</c>: rebuilding wins, then any exception
/// while reading diagnostics means the index is corrupt, then <see cref="IIndexDiagnostics.IsHealthy"/>.
/// Indexes that are not Lucene indexes (ExamineX and other providers) get Umbraco's generic
/// diagnostics, which cannot count; their counts are reported as unknown rather than -1.
/// <para>
/// Rebuilds go through Umbraco's <see cref="IIndexRebuilder"/> in the foreground (so Umbraco's own
/// "is rebuilding" state and its concurrency guard still apply), with a Lucene index switched to
/// synchronous processing for the duration: every batch is then committed before the populators
/// move on, so when the rebuilder returns the index is committed and searchable (ADR 0011).
/// </para>
/// </remarks>
internal sealed class ExamineIndexSource(
    IExamineManager examineManager,
    IIndexDiagnosticsFactory diagnosticsFactory,
    IIndexingRebuilderService rebuilderService,
    IIndexRebuilder indexRebuilder,
    TimeProvider timeProvider,
    ILogger<ExamineIndexSource> logger
) : IIndexSource
{
    /// <summary>The engine id for core Examine indexes.</summary>
    public const string EngineId = "examine";

    /// <summary>Descriptions of the indexes Umbraco itself registers.</summary>
    internal static readonly Dictionary<string, string> KnownDescriptions = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        [Constants.UmbracoIndexes.ExternalIndexName] = "Published content and media",
        [Constants.UmbracoIndexes.InternalIndexName] =
            "All content and media, including unpublished",
        [Constants.UmbracoIndexes.MembersIndexName] = "Members",
        [Constants.UmbracoIndexes.DeliveryApiContentIndexName] =
            "Delivery API content, one document per culture",
    };

    /// <inheritdoc />
    public string Engine => EngineId;

    /// <inheritdoc />
    public IReadOnlySet<string> ClaimedExamineIndexNames { get; } = new HashSet<string>();

    /// <inheritdoc />
    public async Task<IReadOnlyList<IndexSummary>> GetIndexesAsync(
        IReadOnlySet<string> claimedElsewhere,
        CancellationToken cancellationToken
    )
    {
        var result = new List<IndexSummary>();
        foreach (
            IIndex index in examineManager
                .Indexes.Where(index => !claimedElsewhere.Contains(index.Name))
                .OrderBy(index => index.Name, StringComparer.OrdinalIgnoreCase)
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            result.Add(await DescribeAsync(index));
        }

        return result;
    }

    /// <inheritdoc />
    public async Task RebuildAsync(
        string indexName,
        IProgress<long> documentsWritten,
        CancellationToken cancellationToken
    )
    {
        if (!examineManager.TryGetIndex(indexName, out IIndex? index))
        {
            throw new InvalidOperationException($"There is no Examine index called {indexName}.");
        }

        if (!CanRebuild(indexName))
        {
            throw new InvalidOperationException(
                "No populator is registered for this index, so Umbraco cannot rebuild it."
            );
        }

        if (await rebuilderService.IsRebuildingAsync(indexName))
        {
            throw new InvalidOperationException(
                "Umbraco is already rebuilding this index (started from somewhere else)."
            );
        }

        // Lucene reports write failures through IndexingError rather than by throwing; the first
        // one fails the run.
        string? indexingError = null;
        void OnIndexingError(object? sender, IndexingErrorEventArgs args) =>
            indexingError ??= args.Exception?.Message ?? args.Message;

        using var stopPolling = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task polling = DocumentCountPoller.StartAsync(
            () => ReadDocumentCount(index),
            documentsWritten,
            timeProvider,
            stopPolling.Token
        );

        index.IndexingError += OnIndexingError;
        try
        {
            // Synchronous processing commits each batch as it is written, so the rebuilder only
            // returns once Lucene has committed everything (Examine's default is to queue writes
            // and commit on a timer, which lets "is rebuilding" clear before the writes land).
            // The scope tells Search Manager's rebuilder decorator this run is recorded already.
            using (SearchManagerRebuildScope.Begin())
            using ((index as LuceneIndex)?.WithThreadingMode(IndexThreadingMode.Synchronous))
            {
                Attempt<IndexRebuildResult> attempt = await indexRebuilder.RebuildIndexAsync(
                    indexName,
                    TimeSpan.Zero,
                    useBackgroundThread: false
                );
                if (!attempt.Success)
                {
                    throw new InvalidOperationException(
                        attempt.Result switch
                        {
                            IndexRebuildResult.AlreadyRebuilding =>
                                "Umbraco is already rebuilding this index (started from somewhere else).",
                            IndexRebuildResult.NotAllowedToRun =>
                                "Umbraco is not allowed to rebuild indexes on this server right now (it is not the main domain, or not fully started).",
                            _ => "Umbraco did not rebuild the index. Check the log.",
                        }
                    );
                }
            }

            (index as LuceneIndex)?.WaitForChanges();
        }
        finally
        {
            index.IndexingError -= OnIndexingError;
            await stopPolling.CancelAsync();
            await polling;
        }

        if (indexingError is not null)
        {
            throw new InvalidOperationException(indexingError);
        }
    }

    /// <summary>Reads an index's document count for progress, or null when it cannot count.</summary>
    /// <param name="index">The index.</param>
    /// <returns>The count, or null.</returns>
    private long? ReadDocumentCount(IIndex index)
    {
        long count = diagnosticsFactory.Create(index).GetDocumentCount();
        return count < 0 ? null : count;
    }

    /// <summary>Builds the summary for one index. Never throws for a broken index.</summary>
    /// <param name="index">The index.</param>
    /// <returns>The summary.</returns>
    private async Task<IndexSummary> DescribeAsync(IIndex index)
    {
        bool rebuilding = await rebuilderService.IsRebuildingAsync(index.Name);
        bool canRebuild = CanRebuild(index.Name);
        bool isLucene = index is LuceneIndex;
        bool isMember = index is IUmbracoMemberIndex;

        IndexHealth health;
        string? healthMessage = null;
        long? documents = null;
        int? fields = null;
        var properties = new Dictionary<string, string>(StringComparer.Ordinal);

        try
        {
            IIndexDiagnostics diagnostics = diagnosticsFactory.Create(index);
            Attempt<string?> healthy = diagnostics.IsHealthy();
            long documentCount = diagnostics.GetDocumentCount();
            int fieldCount = diagnostics.GetFieldNames().Count();

            // Umbraco's generic diagnostics (non-Lucene providers) answer -1 for the document
            // count and list no fields: neither number is known, so neither is reported.
            documents = documentCount < 0 ? null : documentCount;
            fields = documentCount < 0 ? null : fieldCount;

            foreach ((string key, object? value) in diagnostics.Metadata)
            {
                properties[key] = value?.ToString() ?? string.Empty;
            }

            if (!healthy.Success)
            {
                health = IndexHealth.Unhealthy;
                healthMessage = healthy.Result;
            }
            else
            {
                health = documents == 0 ? IndexHealth.Empty : IndexHealth.Healthy;
            }
        }
#pragma warning disable CA1031 // A broken index must be listed as corrupt, whatever it throws.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            logger.DiagnosticsFailed(exception, index.Name);
            health = IndexHealth.Corrupted;
            healthMessage = exception.Message;
        }

        if (rebuilding)
        {
            health = IndexHealth.Rebuilding;
        }

        LuceneIndexStatistics? statistics =
            isLucene && health is not IndexHealth.Corrupted
                ? LuceneIndexStatistics.TryRead((LuceneIndex)index, logger)
                : null;

        bool rebuildAvailable = canRebuild && health is not IndexHealth.Corrupted;
        string? rebuildReason =
            rebuildAvailable ? null
            : health is IndexHealth.Corrupted && canRebuild
                ? "The index cannot be read. Check the log; Umbraco recreates a corrupt index when the site restarts"
            : "No populator is registered for this index, so Umbraco cannot rebuild it";

        return new IndexSummary(
            Name: index.Name,
            Engine: EngineId,
            Description: KnownDescriptions.GetValueOrDefault(index.Name),
            Health: health,
            HealthMessage: healthMessage,
            DocumentCount: health is IndexHealth.Corrupted ? null : documents,
            DeletedDocumentCount: statistics?.DeletedDocumentCount,
            FieldCount: health is IndexHealth.Corrupted ? null : fields,
            SizeInBytes: statistics?.SizeInBytes,
            Capabilities: new IndexCapabilities(
                Rebuild: rebuildAvailable,
                Search: health is not IndexHealth.Corrupted,
                NativeQuery: isLucene,
                TermStatistics: isLucene
            ),
            RebuildUnavailableReason: rebuildReason,
            ZeroDowntime: false,
            IsMemberIndex: isMember,
            Properties: properties
        );
    }

    /// <summary>
    /// Whether Umbraco can rebuild the index (it has a populator). <c>CanRebuild</c> throws for an
    /// index it does not know, which counts as "no".
    /// </summary>
    /// <param name="indexName">The index name.</param>
    /// <returns><see langword="true"/> when the index can be rebuilt.</returns>
    private bool CanRebuild(string indexName)
    {
        try
        {
            return rebuilderService.CanRebuild(indexName);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}
