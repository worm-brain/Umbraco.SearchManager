using Examine;
using Examine.Search;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.Entities;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Infrastructure.Examine;
using Umbraco.Cms.Search.Core.Configuration;
using Umbraco.Cms.Search.Core.Models.Configuration;
using Umbraco.Cms.Search.Core.Models.Searching;
using Umbraco.Cms.Search.Core.Models.Searching.Sorting;
using Umbraco.Cms.Search.Core.Services;
using Umbraco.Cms.Search.Provider.Examine.Services;
using Umbraco.Community.SearchManager.Core.Explore;
using Umbraco.Community.SearchManager.UmbracoSearch.Indexes;
using Umbraco.Extensions;
using Direction = Umbraco.Cms.Core.Direction;
using IndexOptions = Umbraco.Cms.Search.Core.Configuration.IndexOptions;
using SearchFieldNames = Umbraco.Cms.Search.Core.Constants.FieldNames;
using SearchFilter = Umbraco.Cms.Search.Core.Models.Searching.Filtering.Filter;
using SearchManagerFilter = Umbraco.Community.SearchManager.Core.Explore.Filter;
using UmbracoSearcher = Umbraco.Cms.Search.Core.Services.ISearcher;

namespace Umbraco.Community.SearchManager.UmbracoSearch.Explore;

/// <summary>
/// Explore for Umbraco Search (ADR 0016): every content index registration with a searcher. Chips
/// compile to <see cref="UmbracoSearcher.SearchAsync"/> (<see cref="UmbracoSearchCompiler"/>),
/// which returns only keys and object types, so names, icons and document types come from
/// Umbraco's entities, and the stored fields from the Examine provider's active physical index
/// when the index uses that provider.
/// </summary>
internal sealed class UmbracoSearchExploreSource : IExploreSource
{
    /// <summary>The Examine provider's culture field and its value for invariant documents.</summary>
    private const string CultureField = "Sys_Culture";

    private const string Invariant = "none";

    private readonly ContentIndexRegistration[] _registrations;
    private readonly ISearcherResolver? _searcherResolver;
    private readonly IExamineManager _examineManager;
    private readonly IIndexDiagnosticsFactory _diagnosticsFactory;
    private readonly IActiveIndexManager? _activeIndexManager;
    private readonly IEntityService _entityService;
    private readonly IContentTypeService _contentTypeService;
    private readonly IMediaTypeService _mediaTypeService;
    private readonly IMemberTypeService _memberTypeService;
    private readonly ILanguageService _languageService;
    private readonly TimeProvider _timeProvider;

    /// <summary>Creates the source.</summary>
    /// <param name="indexOptions">Umbraco Search's index registrations.</param>
    /// <param name="examineManager">Finds the Examine provider's physical indexes.</param>
    /// <param name="diagnosticsFactory">Reads a physical index's field names.</param>
    /// <param name="entityService">Names, icons and document types of the documents found.</param>
    /// <param name="contentTypeService">Resolves document type aliases to keys.</param>
    /// <param name="mediaTypeService">Resolves media type aliases to keys.</param>
    /// <param name="memberTypeService">Resolves member type aliases to keys.</param>
    /// <param name="languageService">The site's cultures.</param>
    /// <param name="timeProvider">Today, for relative dates.</param>
    /// <param name="searcherResolver">
    /// Umbraco Search's searcher resolver; null without <c>AddSearchCore()</c>, when the source
    /// lists nothing.
    /// </param>
    /// <param name="activeIndexManager">The Examine provider's slot manager; null without the provider.</param>
    public UmbracoSearchExploreSource(
        IOptions<IndexOptions> indexOptions,
        IExamineManager examineManager,
        IIndexDiagnosticsFactory diagnosticsFactory,
        IEntityService entityService,
        IContentTypeService contentTypeService,
        IMediaTypeService mediaTypeService,
        IMemberTypeService memberTypeService,
        ILanguageService languageService,
        TimeProvider timeProvider,
        ISearcherResolver? searcherResolver = null,
        IActiveIndexManager? activeIndexManager = null
    )
    {
        ArgumentNullException.ThrowIfNull(indexOptions);
        _registrations = searcherResolver is null
            ? []
            : indexOptions.Value.GetContentIndexRegistrations();
        _searcherResolver = searcherResolver;
        _examineManager = examineManager;
        _diagnosticsFactory = diagnosticsFactory;
        _activeIndexManager = activeIndexManager;
        _entityService = entityService;
        _contentTypeService = contentTypeService;
        _mediaTypeService = mediaTypeService;
        _memberTypeService = memberTypeService;
        _languageService = languageService;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public string Engine => UmbracoSearchIndexSource.EngineId;

    /// <inheritdoc />
    public IReadOnlyList<FieldTypeOperators> Operators => UmbracoSearchCompiler.Operators;

    /// <inheritdoc />
    public IReadOnlyList<UnsupportedFilter> Unsupported => UmbracoSearchCompiler.Unsupported;

    /// <inheritdoc />
    public Task<IReadOnlyList<ExploreTarget>> GetTargetsAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<ExploreTarget> targets =
        [
            .. _registrations
                .Where(registration =>
                    _searcherResolver!.GetSearcher(registration.IndexAlias) is not null
                )
                .OrderBy(registration => registration.IndexAlias, StringComparer.OrdinalIgnoreCase)
                .Select(registration => new ExploreTarget(
                    registration.IndexAlias,
                    Engine,
                    ExploreTargetKind.Index,
                    UmbracoSearchIndexSource.Describe(registration),
                    registration.ContainedObjectTypes.Contains(UmbracoObjectTypes.Member),
                    // Umbraco Search takes filters, not raw queries; no term statistics in v1,
                    // and no scores to explain.
                    new ExploreCapabilities(
                        NativeQuery: false,
                        TermStatistics: false,
                        ExplainScore: false
                    )
                )),
        ];
        return Task.FromResult(targets);
    }

    /// <inheritdoc />
    public Task<FieldSchema> GetSchemaAsync(
        ExploreTarget target,
        CancellationToken cancellationToken
    ) => Task.FromResult(FieldsOf(target).Schema);

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetCulturesAsync(CancellationToken cancellationToken)
    {
        IEnumerable<ILanguage> languages = await _languageService.GetAllAsync();
        return [.. languages.Select(language => language.IsoCode).Order(StringComparer.Ordinal)];
    }

    /// <inheritdoc />
    /// <remarks>
    /// The site's default language: Umbraco Search keeps one document per culture and finds only
    /// invariant documents without one, so starting on no culture hides variant content (ADR 0021).
    /// </remarks>
    public async Task<string?> GetDefaultCultureAsync(CancellationToken cancellationToken) =>
        await _languageService.GetDefaultIsoCodeAsync();

    /// <inheritdoc />
    /// <exception cref="NotSupportedException">Always: Umbraco Search returns no scores to explain.</exception>
    public Task<ExplainResult> ExplainAsync(
        ExploreTarget target,
        ExplainRequest request,
        CancellationToken cancellationToken
    ) => throw new NotSupportedException("Umbraco Search does not return scores to explain");

    /// <inheritdoc />
    /// <remarks>
    /// Without a culture Umbraco Search searches only invariant documents (it keeps one document
    /// per culture for variant content). Protection is bypassed: Explore is a Settings tool.
    /// Without free text, results are sorted by update date, newest first, as core Examine's are.
    /// </remarks>
    public async Task<ExploreResult> SearchAsync(
        ExploreTarget target,
        ExploreQuery query,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(query);

        UmbracoSearcher searcher =
            _searcherResolver?.GetSearcher(target.Name)
            ?? throw new InvalidOperationException(
                $"Umbraco Search has no searcher for {target.Name}"
            );
        UmbracoSearchFields fields = FieldsOf(target);
        UmbracoSearchCompilation compilation = Compile(query.Filters, fields);
        bool newestFirst = compilation.Query is null;
        string? culture = string.IsNullOrWhiteSpace(query.Culture) ? null : query.Culture;

        Umbraco.Cms.Search.Core.Models.Searching.SearchResult result = await searcher.SearchAsync(
            target.Name,
            query: compilation.Query,
            filters: [.. compilation.Filters.Select(filter => filter.Filter)],
            facets: null,
            sorters: newestFirst
                ? [new DateTimeOffsetSorter(SearchFieldNames.UpdateDate, Direction.Descending)]
                : null,
            culture: culture,
            segment: null,
            accessContext: AccessContext.BypassProtection(),
            skip: query.Skip,
            take: query.Take
        );

        Document[] documents = [.. result.Documents];
        IReadOnlyDictionary<Guid, IEntitySlim> entities = Entities(documents);
        IReadOnlyDictionary<Guid, IReadOnlyDictionary<string, IReadOnlyList<string>>> stored =
            StoredFields(target.Name, documents, culture);

        return new ExploreResult(
            result.Total,
            [
                .. documents.Select(document =>
                    UmbracoSearchHits.ToHit(
                        document,
                        entities.GetValueOrDefault(document.Id),
                        stored.GetValueOrDefault(document.Id),
                        culture
                    )
                ),
            ],
            UmbracoSearchCSharp.For(
                target.Name,
                compilation,
                culture,
                newestFirst,
                query.Skip,
                query.Take
            ),
            compilation.Skipped,
            newestFirst
        );
    }

    /// <inheritdoc />
    /// <exception cref="NotSupportedException">Always: Umbraco Search targets have no term statistics.</exception>
    public Task<TermsResult?> GetTermsAsync(
        ExploreTarget target,
        string field,
        string prefix,
        int take,
        CancellationToken cancellationToken
    ) => throw NoTermStatistics(target);

    /// <inheritdoc />
    /// <exception cref="NotSupportedException">Always: Umbraco Search targets have no term statistics.</exception>
    public Task<FacetsResult> GetFacetsAsync(
        ExploreTarget target,
        FacetsQuery query,
        CancellationToken cancellationToken
    ) => throw NoTermStatistics(target);

    /// <inheritdoc />
    /// <remarks>The query is the <c>SearchAsync</c> call itself, in C#; there is no parsed form.</remarks>
    public Task<CompiledQuery> CompileAsync(
        ExploreTarget target,
        CompileRequest request,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(request);
        if (request.Native is not null)
        {
            throw NoNativeQueries(target);
        }

        UmbracoSearchCompilation compilation = Compile(request.Filters ?? [], FieldsOf(target));
        string code = UmbracoSearchCSharp.For(
            target.Name,
            compilation,
            string.IsNullOrWhiteSpace(request.Culture) ? null : request.Culture,
            compilation.Query is null
        );
        return Task.FromResult(
            new CompiledQuery("C#", code, null, code, true, compilation.Skipped)
        );
    }

    /// <inheritdoc />
    /// <exception cref="NotSupportedException">Always: Umbraco Search takes no raw queries.</exception>
    public Task<NativeValidation> ValidateNativeAsync(
        ExploreTarget target,
        string query,
        CancellationToken cancellationToken
    ) => throw NoNativeQueries(target);

    /// <inheritdoc />
    /// <exception cref="NotSupportedException">Always: Umbraco Search takes no raw queries.</exception>
    public Task<IReadOnlyList<SearchManagerFilter>> ReadNativeAsync(
        ExploreTarget target,
        string query,
        CancellationToken cancellationToken
    ) => throw NoNativeQueries(target);

    /// <summary>Compiles chips for a target as a search would (used by "Find a node", ADR 0019).</summary>
    /// <param name="target">A target this source listed.</param>
    /// <param name="filters">The chips.</param>
    /// <returns>The compiled request.</returns>
    internal UmbracoSearchCompilation CompileFor(
        ExploreTarget target,
        IReadOnlyList<SearchManagerFilter> filters
    ) => Compile(filters, FieldsOf(target));

    /// <summary>Umbraco Search's searcher for an alias.</summary>
    /// <param name="alias">The index alias.</param>
    /// <returns>The searcher, or null when Umbraco Search has none.</returns>
    internal UmbracoSearcher? SearcherFor(string alias) => _searcherResolver?.GetSearcher(alias);

    /// <summary>The Examine provider's physical index serving an alias, or null for other providers.</summary>
    /// <param name="alias">The index alias.</param>
    /// <returns>The index, or null.</returns>
    internal IIndex? PhysicalIndexFor(string alias) => PhysicalIndex(alias);

    /// <summary>Compiles chips, resolving relative dates against the server's clock.</summary>
    private UmbracoSearchCompilation Compile(
        IReadOnlyList<SearchManagerFilter> filters,
        UmbracoSearchFields fields
    ) =>
        UmbracoSearchCompiler.Compile(
            filters,
            fields,
            DateOnly.FromDateTime(_timeProvider.GetLocalNow().DateTime),
            ContentTypeKeys
        );

    /// <summary>The keys of the document, media and member types with an alias.</summary>
    private IReadOnlyList<Guid> ContentTypeKeys(string alias) =>
        [
            .. new IContentTypeComposition?[]
            {
                _contentTypeService.Get(alias),
                _mediaTypeService.Get(alias),
                _memberTypeService.Get(alias),
            }
                .OfType<IContentTypeComposition>()
                .Select(type => type.Key)
                .Distinct(),
        ];

    /// <summary>
    /// The fields of a target: the system fields, plus every property field of the Examine
    /// provider's active physical index when the index uses that provider.
    /// </summary>
    private UmbracoSearchFields FieldsOf(ExploreTarget target)
    {
        IIndex? physical = PhysicalIndex(target.Name);
        if (physical is null)
        {
            return UmbracoSearchFields.FromPhysicalNames([]);
        }

        IIndexDiagnostics diagnostics = _diagnosticsFactory.Create(physical);
        long count = diagnostics.GetDocumentCount();
        return UmbracoSearchFields.FromPhysicalNames(
            diagnostics.GetFieldNames(),
            count < 0 ? null : count
        );
    }

    /// <summary>The Examine provider's index that serves queries for an alias, or null.</summary>
    private IIndex? PhysicalIndex(string alias) =>
        UmbracoSearchPhysicalIndexes.Find(
            _registrations,
            alias,
            _activeIndexManager,
            _examineManager
        );

    /// <summary>Reads the entities of the documents found, by key.</summary>
    private Dictionary<Guid, IEntitySlim> Entities(Document[] documents)
    {
        var entities = new Dictionary<Guid, IEntitySlim>();
        foreach (
            IGrouping<UmbracoObjectTypes, Document> group in documents.GroupBy(d => d.ObjectType)
        )
        {
            foreach (
                IEntitySlim entity in _entityService.GetAll(
                    group.Key,
                    [.. group.Select(document => document.Id)]
                )
            )
            {
                entities[entity.Key] = entity;
            }
        }

        return entities;
    }

    /// <summary>
    /// Reads the stored fields of the documents found from the Examine provider's active physical
    /// index: the culture's document of each (or the invariant one). Empty for other providers.
    /// </summary>
    private IReadOnlyDictionary<
        Guid,
        IReadOnlyDictionary<string, IReadOnlyList<string>>
    > StoredFields(string alias, Document[] documents, string? culture)
    {
        IIndex? physical = PhysicalIndex(alias);
        return physical is null || documents.Length == 0
            ? new Dictionary<Guid, IReadOnlyDictionary<string, IReadOnlyList<string>>>()
            : ReadStoredFields(physical.Searcher, [.. documents.Select(d => d.Id)], culture);
    }

    /// <summary>
    /// Reads stored fields by key from an Examine provider index, as its own document endpoint
    /// does (by <c>Field_Umb_Id_keywords</c>), keeping the document of the culture searched.
    /// </summary>
    /// <param name="searcher">The physical index's searcher.</param>
    /// <param name="keys">The document keys.</param>
    /// <param name="culture">The culture searched, or null for invariant documents.</param>
    /// <returns>Each key's fields, named as filters take them (<see cref="UmbracoSearchHits.LogicalFields"/>).</returns>
    internal static IReadOnlyDictionary<
        Guid,
        IReadOnlyDictionary<string, IReadOnlyList<string>>
    > ReadStoredFields(Examine.ISearcher searcher, IReadOnlyList<Guid> keys, string? culture)
    {
        string[] cultures = culture is null ? [Invariant] : [culture, Invariant];
        ISearchResults results = searcher
            .CreateQuery()
            .GroupedOr(
                [UmbracoSearchFields.PhysicalPrefix + SearchFieldNames.Id + "_keywords"],
                [.. keys.Select(key => key.ToString("D"))]
            )
            .And()
            .GroupedOr([CultureField], cultures)
            .Execute(QueryOptions.SkipTake(0, keys.Count * cultures.Length));

        var fields = new Dictionary<Guid, IReadOnlyDictionary<string, IReadOnlyList<string>>>();
        // The culture's own document wins over the invariant one when both exist.
        foreach (
            ISearchResult result in results.OrderBy(result =>
                result.AllValues.TryGetValue(CultureField, out IReadOnlyList<string>? values)
                && values.Contains(Invariant)
                    ? 1
                    : 0
            )
        )
        {
            if (
                result.AllValues.TryGetValue(
                    UmbracoSearchFields.PhysicalPrefix + SearchFieldNames.Id + "_keywords",
                    out IReadOnlyList<string>? ids
                )
                && ids.Count > 0
                && Guid.TryParse(ids[0], out Guid key)
            )
            {
                fields.TryAdd(key, UmbracoSearchHits.LogicalFields(result.AllValues));
            }
        }

        return fields;
    }

    private static NotSupportedException NoTermStatistics(ExploreTarget target) =>
        new($"{target.Name} is an Umbraco Search index, so its terms cannot be read");

    private static NotSupportedException NoNativeQueries(ExploreTarget target) =>
        new($"{target.Name} is an Umbraco Search index, which takes filters, not raw Lucene");
}
