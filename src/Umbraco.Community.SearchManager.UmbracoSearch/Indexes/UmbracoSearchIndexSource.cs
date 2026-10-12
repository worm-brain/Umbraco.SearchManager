using Examine;
using Examine.Lucene.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Infrastructure.Examine;
using Umbraco.Cms.Search.Core.Configuration;
using Umbraco.Cms.Search.Core.Models.Configuration;
using Umbraco.Cms.Search.Core.Models.Indexing;
using Umbraco.Cms.Search.Core.Services;
using Umbraco.Cms.Search.Core.Services.ContentIndexing;
using Umbraco.Cms.Search.Provider.Examine.Configuration;
using Umbraco.Cms.Search.Provider.Examine.Services;
using Umbraco.Community.SearchManager.Core.Indexes;
using Umbraco.Community.SearchManager.Features.Indexes;
using Umbraco.Community.SearchManager.Features.Rebuilds;
using IndexOptions = Umbraco.Cms.Search.Core.Configuration.IndexOptions;

namespace Umbraco.Community.SearchManager.UmbracoSearch.Indexes;

/// <summary>
/// Lists Umbraco Search's content index registrations (<c>Umb_PublishedContent</c>,
/// <c>Umb_Content</c>, <c>Umb_Media</c>, <c>Umb_Members</c> and any a site adds), with the health
/// and document count Umbraco Search reports.
/// </summary>
/// <remarks>
/// When a registration's indexer is the Examine provider, the physical Lucene index behind it
/// (the alias itself, or <c>{alias}_a</c> / <c>{alias}_b</c> with zero-downtime indexing) adds
/// fields, deleted documents, size on disk and its provider properties, read from the slot
/// <see cref="IActiveIndexManager"/> says is serving queries. Those physical names are claimed, so
/// the core Examine source does not list them a second time. Other providers show what
/// <see cref="IIndexer.GetMetadataAsync"/> gives and nothing more.
/// </remarks>
internal sealed class UmbracoSearchIndexSource : IIndexSource
{
    /// <summary>The engine id for Umbraco Search indexes.</summary>
    public const string EngineId = "umbraco-search";

    /// <summary>The suffixes the Examine provider gives an alias's two physical indexes under zero-downtime indexing.</summary>
    private static readonly string[] ZeroDowntimeSuffixes = ["_a", "_b"];

    private readonly ContentIndexRegistration[] _registrations;
    private readonly IIndexerResolver? _indexerResolver;
    private readonly IExamineManager _examineManager;
    private readonly IIndexDiagnosticsFactory _diagnosticsFactory;
    private readonly IActiveIndexManager? _activeIndexManager;
    private readonly bool _zeroDowntimeSetting;
    private readonly ILogger<UmbracoSearchIndexSource> _logger;
    private readonly UmbracoSearchRebuildTracker _tracker;
    private readonly TimeProvider _timeProvider;
    private readonly IDistributedContentIndexRebuilder? _rebuilder;
    private readonly IIndexCommitMonitor? _commitMonitor;
    private readonly ISearcherResolver? _searcherResolver;

    /// <summary>How long Umbraco Search may take to start a rebuild on this server.</summary>
    internal static readonly TimeSpan StartTimeout = TimeSpan.FromMinutes(2);

    /// <summary>
    /// How long a rebuild may go without its document count changing before it is taken to have
    /// failed (Umbraco Search logs a failure but announces nothing).
    /// </summary>
    internal static readonly TimeSpan StallTimeout = TimeSpan.FromMinutes(10);

    /// <summary>How long the Examine provider may take to swap in a zero-downtime copy.</summary>
    internal static readonly TimeSpan SwapTimeout = TimeSpan.FromMinutes(2);

    /// <summary>Creates the source.</summary>
    /// <param name="indexOptions">Umbraco Search's index registrations.</param>
    /// <param name="examineManager">Finds the Examine provider's physical indexes.</param>
    /// <param name="diagnosticsFactory">Reads a physical index's fields and provider properties.</param>
    /// <param name="providerSettings">The Examine provider's settings (zero-downtime indexing).</param>
    /// <param name="logger">Logs indexes that cannot be read.</param>
    /// <param name="tracker">Turns Umbraco Search's rebuild notifications into tasks.</param>
    /// <param name="timeProvider">Drives the rebuild timeouts and progress polling.</param>
    /// <param name="indexerResolver">
    /// Umbraco Search's indexer resolver; null when the site has not called <c>AddSearchCore()</c>,
    /// in which case the source lists nothing.
    /// </param>
    /// <param name="activeIndexManager">
    /// The Examine provider's slot manager; null when the site does not use the Examine provider.
    /// </param>
    /// <param name="rebuilder">Umbraco Search's distributed rebuilder; null without <c>AddSearchCore()</c>.</param>
    /// <param name="commitMonitor">The Examine provider's commit monitor; null without the provider.</param>
    /// <param name="searcherResolver">Umbraco Search's searcher resolver, for the search capability; null without <c>AddSearchCore()</c>.</param>
    public UmbracoSearchIndexSource(
        IOptions<IndexOptions> indexOptions,
        IExamineManager examineManager,
        IIndexDiagnosticsFactory diagnosticsFactory,
        IOptions<ExamineSearchProviderSettings> providerSettings,
        ILogger<UmbracoSearchIndexSource> logger,
        UmbracoSearchRebuildTracker tracker,
        TimeProvider timeProvider,
        IIndexerResolver? indexerResolver = null,
        IActiveIndexManager? activeIndexManager = null,
        IDistributedContentIndexRebuilder? rebuilder = null,
        IIndexCommitMonitor? commitMonitor = null,
        ISearcherResolver? searcherResolver = null
    )
    {
        ArgumentNullException.ThrowIfNull(indexOptions);
        ArgumentNullException.ThrowIfNull(providerSettings);

        _registrations = indexerResolver is null
            ? []
            : indexOptions.Value.GetContentIndexRegistrations();
        _indexerResolver = indexerResolver;
        _examineManager = examineManager;
        _diagnosticsFactory = diagnosticsFactory;
        _activeIndexManager = activeIndexManager;
        _zeroDowntimeSetting = providerSettings.Value.ZeroDowntimeIndexing;
        _logger = logger;
        _tracker = tracker;
        _timeProvider = timeProvider;
        _rebuilder = rebuilder;
        _commitMonitor = commitMonitor;
        _searcherResolver = searcherResolver;

        ClaimedExamineIndexNames = new HashSet<string>(
            _registrations
                .Where(IsExamineBacked)
                .SelectMany(registration =>
                    ZeroDowntimeSuffixes
                        .Select(suffix => registration.IndexAlias + suffix)
                        .Prepend(registration.IndexAlias)
                ),
            StringComparer.OrdinalIgnoreCase
        );
    }

    /// <inheritdoc />
    public string Engine => EngineId;

    /// <inheritdoc />
    public IReadOnlySet<string> ClaimedExamineIndexNames { get; }

    /// <inheritdoc />
    public async Task<IReadOnlyList<IndexSummary>> GetIndexesAsync(
        IReadOnlySet<string> claimedElsewhere,
        CancellationToken cancellationToken
    )
    {
        var result = new List<IndexSummary>();
        foreach (
            ContentIndexRegistration registration in _registrations.OrderBy(
                registration => registration.IndexAlias,
                StringComparer.OrdinalIgnoreCase
            )
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            result.Add(await DescribeAsync(registration));
        }

        return result;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Asks <see cref="IDistributedContentIndexRebuilder"/> to rebuild (on every server), then waits
    /// for Umbraco Search's notifications on this server. Umbraco Search announces no failure, so
    /// a rebuild that does not start within <see cref="StartTimeout"/>, or whose document count
    /// stands still for <see cref="StallTimeout"/>, is reported as failed. With the Examine
    /// provider the run is done only once the new data is committed: with zero-downtime indexing
    /// once the provider has swapped the new copy in (which it does after the commit), otherwise
    /// once the physical index has committed.
    /// </remarks>
    public async Task RebuildAsync(
        string indexName,
        IProgress<long> documentsWritten,
        CancellationToken cancellationToken
    )
    {
        ContentIndexRegistration registration =
            _registrations.FirstOrDefault(candidate =>
                string.Equals(candidate.IndexAlias, indexName, StringComparison.OrdinalIgnoreCase)
            )
            ?? throw new InvalidOperationException(
                $"Umbraco Search has no index called {indexName}."
            );
        if (_rebuilder is null)
        {
            throw new InvalidOperationException("Umbraco Search is not set up on this site.");
        }

        string alias = registration.IndexAlias;
        bool examineBacked = IsExamineBacked(registration) && _activeIndexManager is not null;
        bool zeroDowntime = examineBacked && _zeroDowntimeSetting;
        string? activeBefore = examineBacked
            ? _activeIndexManager!.ResolveActiveIndexName(alias)
            : null;

        using UmbracoSearchRebuildTracker.RebuildWatch watch = _tracker.Watch(alias);
        using var stopPolling = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        long? lastCount = null;
        var progress = new Progress(count =>
        {
            lastCount = count;
            documentsWritten.Report(count);
        });
        Task polling = examineBacked
            ? DocumentCountPoller.StartAsync(
                () => ReadWriteTargetCount(alias, zeroDowntime),
                progress,
                _timeProvider,
                stopPolling.Token
            )
            : Task.CompletedTask;

        try
        {
            if (!_rebuilder.Rebuild(alias))
            {
                throw new InvalidOperationException(
                    "Umbraco Search refused to rebuild this index. Check the log."
                );
            }

            if (!await CompletesWithinAsync(watch.Started, StartTimeout, cancellationToken))
            {
                throw new InvalidOperationException(
                    "Umbraco Search did not start the rebuild on this server within 2 minutes. Check the log."
                );
            }

            // Wait for completion, failing only when the count has stood still for a whole stall
            // period (a failed Umbraco Search rebuild is logged, never announced).
            long? countAtLastCheck = lastCount;
            while (!await CompletesWithinAsync(watch.Completed, StallTimeout, cancellationToken))
            {
                if (lastCount == countAtLastCheck)
                {
                    throw new InvalidOperationException(
                        "Umbraco Search stopped making progress for 10 minutes, so the rebuild probably failed. Check the log."
                    );
                }

                countAtLastCheck = lastCount;
            }

            if (zeroDowntime)
            {
                await WaitForSwapAsync(alias, activeBefore!, cancellationToken);
            }
            else if (examineBacked)
            {
                await WaitForCommitAsync(activeBefore!, cancellationToken);
            }
        }
        finally
        {
            await stopPolling.CancelAsync();
            await polling;
        }
    }

    /// <summary>
    /// Waits for the Examine provider to finish a zero-downtime rebuild, and fails when it kept
    /// the old copy (it does so when the new copy is empty or unhealthy). An index with nothing to
    /// index is the exception: the new copy is empty, the provider keeps the old one, which is
    /// empty too, so nothing changed and the run succeeds (with 0 documents) instead of failing.
    /// </summary>
    /// <param name="alias">The alias.</param>
    /// <param name="activeBefore">The physical index that was active before the rebuild.</param>
    /// <param name="cancellationToken">Stops waiting.</param>
    /// <returns>A task that completes once the new copy is active.</returns>
    private async Task WaitForSwapAsync(
        string alias,
        string activeBefore,
        CancellationToken cancellationToken
    )
    {
        DateTimeOffset deadline = _timeProvider.GetUtcNow() + SwapTimeout;
        while (_activeIndexManager!.IsRebuilding(alias) && _timeProvider.GetUtcNow() < deadline)
        {
            await Task.Delay(DocumentCountPoller.Interval, _timeProvider, cancellationToken);
        }

        string activeAfter = _activeIndexManager.ResolveActiveIndexName(alias);
        if (string.Equals(activeAfter, activeBefore, StringComparison.OrdinalIgnoreCase))
        {
            // Nothing to index, such as Umb_Members on a site without members: the kept copy is
            // empty and so is the new one, so search sees exactly what a swap would have given.
            if (
                ReadCount(activeAfter) == 0
                && ReadCount(_activeIndexManager.ResolveShadowIndexName(alias)) is null or 0
            )
            {
                return;
            }

            throw new InvalidOperationException(
                "Umbraco Search kept the current copy because the new copy was empty or unhealthy, so search still uses the old data."
            );
        }
    }

    /// <summary>Waits until the physical index has committed and its searcher sees the new data.</summary>
    /// <param name="physicalIndexName">The physical index.</param>
    /// <param name="cancellationToken">Stops waiting.</param>
    /// <returns>A task that completes once committed (or the provider's own timeout passes).</returns>
    private async Task WaitForCommitAsync(
        string physicalIndexName,
        CancellationToken cancellationToken
    )
    {
        if (_commitMonitor is not null)
        {
            await _commitMonitor.WaitForCommitAsync(physicalIndexName, cancellationToken);
        }

        if (_examineManager.TryGetIndex(physicalIndexName, out IIndex? index))
        {
            (index as LuceneIndex)?.WaitForChanges();
        }
    }

    /// <summary>
    /// Reads the document count of the physical index being written: the standby copy with
    /// zero-downtime indexing, otherwise the index itself.
    /// </summary>
    /// <param name="alias">The alias.</param>
    /// <param name="zeroDowntime">Whether zero-downtime indexing is on.</param>
    /// <returns>The count, or null when unknown.</returns>
    private long? ReadWriteTargetCount(string alias, bool zeroDowntime) =>
        ReadCount(
            zeroDowntime
                ? _activeIndexManager!.ResolveShadowIndexName(alias)
                : _activeIndexManager!.ResolveActiveIndexName(alias)
        );

    /// <summary>Reads the document count of one physical Examine index.</summary>
    /// <param name="name">The physical index name.</param>
    /// <returns>The count, or null when the index is missing or cannot say.</returns>
    private long? ReadCount(string name)
    {
        if (!_examineManager.TryGetIndex(name, out IIndex? index))
        {
            return null;
        }

        long count = _diagnosticsFactory.Create(index).GetDocumentCount();
        return count < 0 ? null : count;
    }

    /// <summary>Waits for <paramref name="task"/> for at most <paramref name="timeout"/>.</summary>
    /// <param name="task">The task.</param>
    /// <param name="timeout">How long to wait.</param>
    /// <param name="cancellationToken">Stops waiting.</param>
    /// <returns>Whether the task completed in time.</returns>
    private async Task<bool> CompletesWithinAsync(
        Task task,
        TimeSpan timeout,
        CancellationToken cancellationToken
    )
    {
        using var timer = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task delay = Task.Delay(timeout, _timeProvider, timer.Token);
        Task first = await Task.WhenAny(task, delay);
        await timer.CancelAsync();
        cancellationToken.ThrowIfCancellationRequested();
        return first == task;
    }

    /// <summary>Calls a callback synchronously for each report.</summary>
    /// <param name="report">The callback.</param>
    private sealed class Progress(Action<long> report) : IProgress<long>
    {
        /// <inheritdoc />
        public void Report(long value) => report(value);
    }

    /// <summary>Whether the registration's indexer is the Examine provider, so a physical Lucene index backs it.</summary>
    /// <param name="registration">The registration.</param>
    /// <returns><see langword="true"/> for the Examine provider.</returns>
    private bool IsExamineBacked(ContentIndexRegistration registration) =>
        typeof(IExamineIndexer).IsAssignableFrom(registration.Indexer);

    /// <summary>Builds the summary for one registration. Never throws for a broken index.</summary>
    /// <param name="registration">The registration.</param>
    /// <returns>The summary.</returns>
    private async Task<IndexSummary> DescribeAsync(ContentIndexRegistration registration)
    {
        string alias = registration.IndexAlias;
        bool examineBacked = IsExamineBacked(registration) && _activeIndexManager is not null;
        bool zeroDowntime = examineBacked && _zeroDowntimeSetting;
        var properties = new Dictionary<string, string>(StringComparer.Ordinal);

        IndexHealth health;
        string? healthMessage = null;
        long? documents = null;
        IIndexer? indexer = _indexerResolver!.GetIndexer(alias);
        if (indexer is null)
        {
            health = IndexHealth.Unknown;
            healthMessage =
                "Umbraco Search could not resolve the indexer for this index. Check the log";
        }
        else
        {
            try
            {
                IndexMetadata metadata = await indexer.GetMetadataAsync(alias);
                health = MapHealth(metadata.HealthStatus);
                documents = metadata.DocumentCount;
                properties["Provider"] = metadata.ProviderName;
            }
#pragma warning disable CA1031 // A broken index must be listed as corrupt, whatever it throws.
            catch (Exception exception)
#pragma warning restore CA1031
            {
                _logger.MetadataFailed(exception, alias);
                health = IndexHealth.Corrupted;
                healthMessage = exception.Message;
            }
        }

        properties["Contains"] = string.Join(", ", registration.ContainedObjectTypes);
        properties["Change strategy"] = DescribeStrategy(registration.ContentChangeStrategy);
        properties["Rebuilds"] = "Distributed to every server";

        PhysicalIndexStatistics? physical = examineBacked
            ? ReadPhysicalIndex(alias, zeroDowntime, properties)
            : null;

        return new IndexSummary(
            Name: alias,
            Engine: EngineId,
            Description: Describe(registration),
            Health: health,
            HealthMessage: healthMessage,
            DocumentCount: health is IndexHealth.Corrupted ? null : documents,
            DeletedDocumentCount: physical?.Statistics?.DeletedDocumentCount,
            FieldCount: physical?.FieldCount,
            SizeInBytes: physical?.Statistics?.SizeInBytes,
            Capabilities: new IndexCapabilities(
                Rebuild: indexer is not null,
                // Explore searches it through Umbraco Search's own searcher (ADR 0016).
                Search: _searcherResolver?.GetSearcher(alias) is not null,
                NativeQuery: false,
                TermStatistics: physical?.IsLucene ?? false
            ),
            RebuildUnavailableReason: indexer is null
                ? "Umbraco Search could not resolve the indexer for this index"
                : null,
            ZeroDowntime: zeroDowntime,
            IsMemberIndex: registration.ContainedObjectTypes.Contains(UmbracoObjectTypes.Member),
            Properties: properties
        );
    }

    /// <summary>
    /// Reads the physical Lucene index that serves queries for <paramref name="alias"/> and adds
    /// its provider properties. Returns null when it cannot be found or read.
    /// </summary>
    /// <param name="alias">The Umbraco Search alias.</param>
    /// <param name="zeroDowntime">Whether the alias has an active and a standby copy.</param>
    /// <param name="properties">The properties to add to.</param>
    /// <returns>The statistics, or null.</returns>
    private PhysicalIndexStatistics? ReadPhysicalIndex(
        string alias,
        bool zeroDowntime,
        Dictionary<string, string> properties
    )
    {
        string active = _activeIndexManager!.ResolveActiveIndexName(alias);
        properties["Physical index"] = zeroDowntime
            ? $"{active} (active), {_activeIndexManager.ResolveShadowIndexName(alias)} (standby)"
            : active;
        properties["Zero-downtime indexing"] = zeroDowntime ? "On" : "Off";

        if (!_examineManager.TryGetIndex(active, out IIndex? index))
        {
            return null;
        }

        int? fields = null;
        try
        {
            IIndexDiagnostics diagnostics = _diagnosticsFactory.Create(index);
            fields = diagnostics.GetFieldNames().Count();
            foreach ((string key, object? value) in diagnostics.Metadata)
            {
                properties[key] = value?.ToString() ?? string.Empty;
            }
        }
#pragma warning disable CA1031 // Statistics are optional; any failure means "unknown".
        catch (Exception exception)
#pragma warning restore CA1031
        {
            _logger.PhysicalIndexFailed(exception, active, alias);
        }

        LuceneIndexStatistics? statistics = index is LuceneIndex lucene
            ? LuceneIndexStatistics.TryRead(lucene, _logger)
            : null;
        return new PhysicalIndexStatistics(fields, statistics, index is LuceneIndex);
    }

    /// <summary>Maps Umbraco Search's health to the package's (the names match).</summary>
    /// <param name="status">Umbraco Search's health.</param>
    /// <returns>The package's health.</returns>
    internal static IndexHealth MapHealth(HealthStatus status) =>
        status switch
        {
            HealthStatus.Healthy => IndexHealth.Healthy,
            HealthStatus.Rebuilding => IndexHealth.Rebuilding,
            HealthStatus.Corrupted => IndexHealth.Corrupted,
            HealthStatus.Empty => IndexHealth.Empty,
            _ => IndexHealth.Unknown,
        };

    /// <summary>
    /// Describes what a registration holds, from its change strategy (published or draft) and
    /// object types, for example "Published content" or "Content, including unpublished".
    /// </summary>
    /// <param name="registration">The registration.</param>
    /// <returns>A short description.</returns>
    internal static string Describe(ContentIndexRegistration registration)
    {
        bool published = typeof(IPublishedContentChangeStrategy).IsAssignableFrom(
            registration.ContentChangeStrategy
        );
        string[] parts =
        [
            .. registration.ContainedObjectTypes.Select(type =>
                type switch
                {
                    UmbracoObjectTypes.Document => "content",
                    UmbracoObjectTypes.Media => "media",
                    UmbracoObjectTypes.Member => "members",
                    _ => type.ToString().ToLowerInvariant(),
                }
            ),
        ];
        string what = string.Join(" and ", parts);
        string sentence =
            published ? $"published {what}"
            : parts.Contains("content") ? $"{what}, including unpublished"
            : what;
        return string.Concat(sentence[..1].ToUpperInvariant(), sentence.AsSpan(1));
    }

    /// <summary>Names a change strategy for the details panel.</summary>
    /// <param name="strategy">The strategy type.</param>
    /// <returns>"Published content", "Draft content" or the type name.</returns>
    private static string DescribeStrategy(Type strategy) =>
        typeof(IPublishedContentChangeStrategy).IsAssignableFrom(strategy) ? "Published content"
        : typeof(IDraftContentChangeStrategy).IsAssignableFrom(strategy) ? "Draft content"
        : strategy.Name;

    /// <summary>What was read from the physical index behind an Umbraco Search index.</summary>
    /// <param name="FieldCount">Distinct field names, or null when unknown.</param>
    /// <param name="Statistics">Lucene statistics, or null when not a Lucene index or unreadable.</param>
    /// <param name="IsLucene">Whether the physical index is a Lucene index.</param>
    private sealed record PhysicalIndexStatistics(
        int? FieldCount,
        LuceneIndexStatistics? Statistics,
        bool IsLucene
    );
}
