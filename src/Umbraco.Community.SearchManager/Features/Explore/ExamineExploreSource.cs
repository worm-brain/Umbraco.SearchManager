using Examine;
using Examine.Lucene.Providers;
using Examine.Lucene.Search;
using Examine.Search;
using Lucene.Net.QueryParsers.Classic;
using Lucene.Net.Util;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Infrastructure.Examine;
using Umbraco.Community.SearchManager.Core.Explore;
using Umbraco.Community.SearchManager.Core.Indexes;
using Umbraco.Community.SearchManager.Features.Indexes;
using IndexSearcher = Lucene.Net.Search.IndexSearcher;

namespace Umbraco.Community.SearchManager.Features.Explore;

/// <summary>
/// Explore for core Examine (ADR 0002): every index in <see cref="IExamineManager"/> that no
/// other source claims, plus the searchers registered on their own (multi-index searchers), as
/// the core dashboard offers them. Filters compile to Lucene text, which runs through Examine's
/// <c>NativeQuery</c> exactly as shown, like the core dashboard's query box.
/// </summary>
internal sealed class ExamineExploreSource(
    IExamineManager examineManager,
    IEnumerable<IIndexSource> indexSources,
    ILanguageService languageService,
    TimeProvider timeProvider
) : IExploreSource
{
    /// <summary>The field results are sorted by, newest first, when no clause ranks them.</summary>
    private const string UpdateDateField = "updateDate";

    /// <inheritdoc />
    public string Engine => ExamineIndexSource.EngineId;

    /// <inheritdoc />
    public IReadOnlyList<FieldTypeOperators> Operators => FieldOperators.All;

    /// <inheritdoc />
    public Task<IReadOnlyList<ExploreTarget>> GetTargetsAsync(CancellationToken cancellationToken)
    {
        // Indexes another source owns (Umbraco Search's physical indexes) are explored there.
        var claimed = new HashSet<string>(
            indexSources
                .Where(source => source.Engine != Engine)
                .SelectMany(source => source.ClaimedExamineIndexNames),
            StringComparer.OrdinalIgnoreCase
        );

        IIndex[] indexes =
        [
            .. examineManager
                .Indexes.Where(index => !claimed.Contains(index.Name))
                .OrderBy(index => index.Name, StringComparer.OrdinalIgnoreCase),
        ];
        var indexNames = new HashSet<string>(
            examineManager.Indexes.Select(index => index.Name),
            StringComparer.OrdinalIgnoreCase
        );

        var targets = new List<ExploreTarget>();
        foreach (IIndex index in indexes)
        {
            targets.Add(
                new ExploreTarget(
                    index.Name,
                    Engine,
                    ExploreTargetKind.Index,
                    ExamineIndexSource.KnownDescriptions.GetValueOrDefault(index.Name),
                    index is IUmbracoMemberIndex,
                    CapabilitiesOf(index.Searcher)
                )
            );
        }

        foreach (
            ISearcher searcher in examineManager
                .RegisteredSearchers.Where(searcher => !indexNames.Contains(searcher.Name))
                .OrderBy(searcher => searcher.Name, StringComparer.OrdinalIgnoreCase)
        )
        {
            targets.Add(
                new ExploreTarget(
                    searcher.Name,
                    Engine,
                    ExploreTargetKind.Searcher,
                    searcher is MultiIndexSearcher ? "Searches several indexes" : null,
                    SearchesMembers(searcher),
                    CapabilitiesOf(searcher)
                )
            );
        }

        return Task.FromResult<IReadOnlyList<ExploreTarget>>(targets);
    }

    /// <inheritdoc />
    public Task<FieldSchema> GetSchemaAsync(
        ExploreTarget target,
        CancellationToken cancellationToken
    )
    {
        ISearcher searcher = SearcherOf(target);
        return Task.FromResult(ExamineFields.Read(searcher, IndexesBehind(searcher)));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetCulturesAsync(CancellationToken cancellationToken)
    {
        IEnumerable<ILanguage> languages = await languageService.GetAllAsync();
        return [.. languages.Select(language => language.IsoCode).Order(StringComparer.Ordinal)];
    }

    /// <inheritdoc />
    public Task<ExploreResult> SearchAsync(
        ExploreTarget target,
        ExploreQuery query,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(query);

        ISearcher searcher = SearcherOf(target);
        FieldSchema schema = ExamineFields.Read(searcher, IndexesBehind(searcher));
        LuceneCompilation compilation = Compile(query.Filters, query.Culture, schema, target);
        IBooleanOperation operation = Parse(searcher, compilation.Query).Operation;

        // Without words to rank by every score is equal, so the newest documents come first,
        // as the prototype's "whole index" view shows them.
        IQueryExecutor executor =
            !compilation.HasScoringClause
            && schema.Resolve(UpdateDateField) is { Type: FieldType.Date }
                ? operation.OrderByDescending(new SortableField(UpdateDateField, SortType.Long))
                : operation;

        // skipTakeMaxResults bounds the collector to the page asked for, as the core dashboard
        // does (Umbraco #17920).
        ISearchResults results = executor.Execute(
            new LuceneQueryOptions(
                query.Skip,
                query.Take,
                skipTakeMaxResults: query.Skip + query.Take
            )
        );

        return Task.FromResult(
            new ExploreResult(
                results.TotalItemCount,
                [.. results.Select(result => ExamineHits.ToHit(result, query.Culture))],
                compilation.Query,
                compilation.Skipped
            )
        );
    }

    /// <inheritdoc />
    public Task<TermsResult?> GetTermsAsync(
        ExploreTarget target,
        string field,
        string prefix,
        int take,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(target);
        RequireTermStatistics(target);

        ISearcher searcher = SearcherOf(target);
        LuceneIndex[] indexes = IndexesBehind(searcher);
        IndexField? resolved = ExamineFields.Read(searcher, indexes).Resolve(field);
        if (resolved is null)
        {
            return Task.FromResult<TermsResult?>(null);
        }

        using ISearcherReference reference = ((BaseLuceneSearcher)searcher)
            .GetSearchContext()
            .GetSearcher();
        (IReadOnlyList<TermCount> terms, bool sampled) = LuceneTerms.Top(
            reference.IndexSearcher.IndexReader,
            resolved.Name,
            ExamineFields.ValueTypeOf(resolved.Name, indexes),
            prefix ?? string.Empty,
            take
        );
        return Task.FromResult<TermsResult?>(
            new TermsResult(resolved.Name, resolved.Type, terms, sampled)
        );
    }

    /// <inheritdoc />
    public Task<FacetsResult> GetFacetsAsync(
        ExploreTarget target,
        FacetsQuery query,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(query);
        RequireTermStatistics(target);

        ISearcher searcher = SearcherOf(target);
        LuceneIndex[] indexes = IndexesBehind(searcher);
        FieldSchema schema = ExamineFields.Read(searcher, indexes);
        LuceneCompilation compilation = Compile(query.Filters, query.Culture, schema, target);

        using ISearcherReference reference = ((BaseLuceneSearcher)searcher)
            .GetSearchContext()
            .GetSearcher();
        IndexSearcher indexSearcher = reference.IndexSearcher;
        FixedBitSet Matches(string lucene) =>
            LuceneTerms.Matches(
                indexSearcher,
                ((LuceneSearchQueryBase)Parse(searcher, lucene).Query).Query
            );

        FixedBitSet documents = Matches(compilation.Query);
        var facets = new List<FieldFacet>();
        foreach (string name in query.Fields.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            IndexField? field = schema.Resolve(name);
            if (field is null)
            {
                continue;
            }

            field = schema.ForCulture(field, query.Culture);
            if (field.Type == FieldType.Date)
            {
                // Dates count relative ranges ("on or after 7 days ago"), the values a date chip
                // takes, rather than one value per day.
                TermCount[] ranges =
                [
                    .. RelativeDates.Ranges.Select(range =>
                    {
                        LuceneCompilation withRange = Compile(
                            [
                                .. query.Filters,
                                new Filter(
                                    FilterKind.Field,
                                    field.Name,
                                    FilterOperator.After,
                                    range
                                ),
                            ],
                            query.Culture,
                            schema,
                            target
                        );
                        return new TermCount(range, Matches(withRange.Query).Cardinality);
                    }),
                ];
                facets.Add(new FieldFacet(field.Name, field.Type, ranges, false));
                continue;
            }

            (IReadOnlyList<TermCount> values, bool sampled) = LuceneTerms.Count(
                indexSearcher.IndexReader,
                field.Name,
                ExamineFields.ValueTypeOf(field.Name, indexes),
                documents,
                query.Take
            );
            facets.Add(new FieldFacet(field.Name, field.Type, values, sampled));
        }

        return Task.FromResult(
            new FacetsResult(
                compilation.Query == LuceneQueryCompiler.MatchAll,
                documents.Cardinality,
                facets
            )
        );
    }

    /// <summary>Compiles filters for a target, resolving relative dates against the server's clock.</summary>
    /// <param name="filters">The chips.</param>
    /// <param name="culture">The culture picked, or null.</param>
    /// <param name="schema">The target's fields.</param>
    /// <param name="target">The target, for whether raw Lucene may run.</param>
    /// <returns>The compiled query.</returns>
    private LuceneCompilation Compile(
        IReadOnlyList<Core.Explore.Filter> filters,
        string? culture,
        FieldSchema schema,
        ExploreTarget target
    ) =>
        LuceneQueryCompiler.Compile(
            filters,
            schema,
            culture,
            DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime),
            target.Capabilities.NativeQuery
        );

    /// <summary>Parses Lucene text with the searcher's own query parser, as <c>NativeQuery</c> does.</summary>
    /// <param name="searcher">The searcher.</param>
    /// <param name="lucene">The query text.</param>
    /// <returns>The query and the operation to run it.</returns>
    /// <exception cref="InvalidNativeQueryException">The text does not parse.</exception>
    private static (IQuery Query, IBooleanOperation Operation) Parse(
        ISearcher searcher,
        string lucene
    )
    {
        IQuery query = searcher.CreateQuery();
        try
        {
            return (query, query.NativeQuery(lucene));
        }
        catch (ParseException exception)
        {
            throw new InvalidNativeQueryException(exception.Message);
        }
    }

    /// <summary>Refuses term reads on a target that is not a Lucene searcher.</summary>
    /// <param name="target">The target.</param>
    /// <exception cref="NotSupportedException">It has no term statistics.</exception>
    private static void RequireTermStatistics(ExploreTarget target)
    {
        if (!target.Capabilities.TermStatistics)
        {
            throw new NotSupportedException(
                $"{target.Name} is not a Lucene index, so its terms cannot be read"
            );
        }
    }

    /// <summary>The searcher behind a target.</summary>
    /// <param name="target">A target this source listed.</param>
    /// <returns>The index's own searcher, or the registered searcher.</returns>
    /// <exception cref="InvalidOperationException">The index or searcher has gone.</exception>
    private ISearcher SearcherOf(ExploreTarget target)
    {
        if (
            target.Kind == ExploreTargetKind.Index
            && examineManager.TryGetIndex(target.Name, out IIndex? index)
        )
        {
            return index.Searcher;
        }

        if (examineManager.TryGetSearcher(target.Name, out ISearcher? searcher))
        {
            return searcher;
        }

        throw new InvalidOperationException($"Examine has no index or searcher {target.Name}");
    }

    /// <summary>The Lucene indexes a searcher reads: its own index, or a multi-index searcher's.</summary>
    /// <param name="searcher">The searcher.</param>
    /// <returns>The indexes, in <see cref="IExamineManager"/> order.</returns>
    private LuceneIndex[] IndexesBehind(ISearcher searcher)
    {
        ISearcher[] searchers = searcher is MultiIndexSearcher multi
            ? [.. multi.Searchers]
            : [searcher];
        return
        [
            .. examineManager
                .Indexes.OfType<LuceneIndex>()
                .Where(index =>
                    searchers.Any(candidate => ReferenceEquals(candidate, index.Searcher))
                ),
        ];
    }

    private static ExploreCapabilities CapabilitiesOf(ISearcher searcher)
    {
        bool lucene = searcher is BaseLuceneSearcher;
        return new ExploreCapabilities(NativeQuery: lucene, TermStatistics: lucene);
    }

    /// <summary>
    /// Whether a registered searcher reads a member index: it is a member index's searcher, or a
    /// multi-index searcher with one among its searchers. Stricter than the core dashboard, which
    /// only checks names.
    /// </summary>
    /// <param name="searcher">The searcher.</param>
    /// <returns><see langword="true"/> when it exposes member data.</returns>
    private bool SearchesMembers(ISearcher searcher)
    {
        ISearcher[] memberSearchers =
        [
            .. examineManager
                .Indexes.Where(index => index is IUmbracoMemberIndex)
                .Select(index => index.Searcher),
        ];
        bool IsMember(ISearcher candidate) =>
            memberSearchers.Any(member =>
                ReferenceEquals(member, candidate)
                || string.Equals(member.Name, candidate.Name, StringComparison.OrdinalIgnoreCase)
            );

        return IsMember(searcher)
            || (searcher is MultiIndexSearcher multi && multi.Searchers.Any(IsMember));
    }
}
