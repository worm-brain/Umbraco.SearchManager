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

    /// <summary>Creates the source.</summary>
    /// <param name="indexOptions">Umbraco Search's index registrations.</param>
    /// <param name="examineManager">Finds the Examine provider's physical indexes.</param>
    /// <param name="diagnosticsFactory">Reads a physical index's fields and provider properties.</param>
    /// <param name="providerSettings">The Examine provider's settings (zero-downtime indexing).</param>
    /// <param name="logger">Logs indexes that cannot be read.</param>
    /// <param name="indexerResolver">
    /// Umbraco Search's indexer resolver; null when the site has not called <c>AddSearchCore()</c>,
    /// in which case the source lists nothing.
    /// </param>
    /// <param name="activeIndexManager">
    /// The Examine provider's slot manager; null when the site does not use the Examine provider.
    /// </param>
    public UmbracoSearchIndexSource(
        IOptions<IndexOptions> indexOptions,
        IExamineManager examineManager,
        IIndexDiagnosticsFactory diagnosticsFactory,
        IOptions<ExamineSearchProviderSettings> providerSettings,
        ILogger<UmbracoSearchIndexSource> logger,
        IIndexerResolver? indexerResolver = null,
        IActiveIndexManager? activeIndexManager = null
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
                Search: false,
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
