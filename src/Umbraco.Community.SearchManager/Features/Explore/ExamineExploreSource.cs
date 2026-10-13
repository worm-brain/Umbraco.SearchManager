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
using Explanation = Lucene.Net.Search.Explanation;
using IndexSearcher = Lucene.Net.Search.IndexSearcher;
using Query = Lucene.Net.Search.Query;
using QueryWrapperFilter = Lucene.Net.Search.QueryWrapperFilter;
using Term = Lucene.Net.Index.Term;
using TermQuery = Lucene.Net.Search.TermQuery;
using TopDocs = Lucene.Net.Search.TopDocs;

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
    /// <remarks>Lucene runs every chip form; raw Lucene depends on the target's capabilities.</remarks>
    public IReadOnlyList<UnsupportedFilter> Unsupported => [];

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

        LuceneCompilation compilation = Compile(
            query.Filters,
            query.Culture,
            query.Native,
            schema,
            target
        );
        IBooleanOperation operation = Parse(searcher, compilation.Query).Operation;

        // Without words to rank by every score is equal, so the newest documents come first,
        // as the prototype's "whole index" view shows them.
        bool newestFirst =
            !compilation.HasScoringClause
            && schema.Resolve(UpdateDateField) is { Type: FieldType.Date };
        IQueryExecutor executor = newestFirst
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
                compilation.Skipped,
                newestFirst
            )
        );
    }

    /// <inheritdoc />
    /// <remarks>
    /// Compiles and parses the query exactly as <see cref="SearchAsync"/> does, then asks Lucene's
    /// <c>IndexSearcher.Explain</c> for the document found by <c>__NodeId</c> (ADR 0020).
    /// A multi-index searcher can hold one id in several indexes; the highest-scoring copy wins.
    /// </remarks>
    public Task<ExplainResult> ExplainAsync(
        ExploreTarget target,
        ExplainRequest request,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(request);
        if (!target.Capabilities.ExplainScore)
        {
            throw new NotSupportedException(
                $"{target.Name} is not a Lucene index, so its scores cannot be explained"
            );
        }

        ISearcher searcher = SearcherOf(target);
        FieldSchema schema = ExamineFields.Read(searcher, IndexesBehind(searcher));
        IReadOnlyList<Core.Explore.Filter> filters = request.Filters ?? [];
        LuceneCompilation compilation = Compile(
            filters,
            request.Culture,
            request.Native,
            schema,
            target
        );
        Query query = ((LuceneSearchQueryBase)Parse(searcher, compilation.Query).Query).Query;
        HashSet<int> skipped = [.. compilation.Skipped.Select(skip => skip.Index)];

        // Native mode has no chips, so nothing to list as must-not-match.
        IReadOnlyList<int> mustNotMatch = request.Native is null
            ? ScoreExplanationReader.MustNotMatch(filters, skipped)
            : [];

        using ISearcherReference reference = ((BaseLuceneSearcher)searcher)
            .GetSearchContext()
            .GetSearcher();
        IndexSearcher indexSearcher = reference.IndexSearcher;
        var documents = new List<DocumentExplanation>();
        foreach (string id in request.Ids ?? [])
        {
            var byId = new TermQuery(new Term(ExamineFieldNames.ItemIdFieldName, id));

            // Filtering by id leaves the query's score as a search gives it.
            TopDocs top = indexSearcher.Search(query, new QueryWrapperFilter(byId), 1);
            if (top.ScoreDocs.Length == 0)
            {
                bool found = indexSearcher.Search(byId, 1).TotalHits > 0;
                documents.Add(new DocumentExplanation(id, found, false, null, null, mustNotMatch));
                continue;
            }

            Explanation explanation = indexSearcher.Explain(query, top.ScoreDocs[0].Doc);
            documents.Add(
                new DocumentExplanation(
                    id,
                    true,
                    true,
                    explanation.Value,
                    ScoreExplanationReader.Read(
                        Raw(explanation),
                        schema,
                        filters,
                        request.Culture,
                        skipped
                    ),
                    mustNotMatch
                )
            );
        }

        return Task.FromResult(new ExplainResult(documents));
    }

    /// <summary>Copies a Lucene explanation into the engine-neutral tree Core reads.</summary>
    /// <param name="explanation">The explanation.</param>
    /// <returns>The copy.</returns>
    private static RawExplanation Raw(Explanation explanation) =>
        new(
            explanation.Value,
            explanation.Description ?? string.Empty,
            explanation.IsMatch,
            [.. (explanation.GetDetails() ?? []).Select(Raw)]
        );

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

            // A field is counted without its own include chips, so its other values stay
            // listed (ADR 0021); most fields have none and reuse the results.
            IReadOnlyList<Core.Explore.Filter> own = FacetFilters.Without(
                query.Filters,
                field.Name,
                schema,
                query.Culture
            );
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
                                .. own,
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

            FixedBitSet counted = ReferenceEquals(own, query.Filters)
                ? documents
                : Matches(Compile(own, query.Culture, schema, target).Query);
            (IReadOnlyList<TermCount> values, bool sampled) = LuceneTerms.Count(
                indexSearcher.IndexReader,
                field.Name,
                ExamineFields.ValueTypeOf(field.Name, indexes),
                counted,
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

    /// <inheritdoc />
    public Task<CompiledQuery> CompileAsync(
        ExploreTarget target,
        CompileRequest request,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(request);

        ISearcher searcher = SearcherOf(target);
        FieldSchema schema = ExamineFields.Read(searcher, IndexesBehind(searcher));
        if (request.Native is not null)
        {
            string native = RequireNative(target, request.Native);
            return Task.FromResult(
                new CompiledQuery(
                    LuceneLanguage,
                    native,
                    ParsedText(searcher, native),
                    ExamineCSharp.Native(native),
                    false,
                    []
                )
            );
        }

        IReadOnlyList<Core.Explore.Filter> filters = request.Filters ?? [];
        LuceneCompilation compilation = Compile(filters, request.Culture, schema, target);
        var skipped = compilation.Skipped.Select(skip => skip.Index).ToHashSet();
        CSharpQuery csharp = ExamineCSharp.For(
            [.. filters.Where((_, index) => !skipped.Contains(index))],
            schema,
            request.Culture,
            Today,
            compilation.Query
        );
        return Task.FromResult(
            new CompiledQuery(
                LuceneLanguage,
                compilation.Query,
                ParsedText(searcher, compilation.Query),
                csharp.Code,
                csharp.Fluent,
                compilation.Skipped
            )
        );
    }

    /// <inheritdoc />
    public Task<NativeValidation> ValidateNativeAsync(
        ExploreTarget target,
        string query,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(target);
        string native = RequireNative(target, query);
        ISearcher searcher = SearcherOf(target);
        FieldSchema schema = ExamineFields.Read(searcher, IndexesBehind(searcher));
        IReadOnlyList<NativeWarning> warnings = NativeQueryLinter.Lint(native, schema);

        try
        {
            searcher.CreateQuery().NativeQuery(native);
            return Task.FromResult(new NativeValidation(true, null, null, warnings));
        }
        catch (ParseException exception)
        {
            return Task.FromResult(
                new NativeValidation(
                    false,
                    exception.Message,
                    LuceneErrorPosition.Find(native, exception),
                    warnings
                )
            );
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Core.Explore.Filter>> ReadNativeAsync(
        ExploreTarget target,
        string query,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(target);
        string native = RequireNative(target, query);
        ISearcher searcher = SearcherOf(target);
        FieldSchema schema = ExamineFields.Read(searcher, IndexesBehind(searcher));
        return Task.FromResult(LuceneToChips.Read(native, schema));
    }

    /// <summary>The query language this source compiles to.</summary>
    private const string LuceneLanguage = "Lucene";

    /// <summary>Today, by the server's clock and time zone, for relative date values.</summary>
    private DateOnly Today => DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime);

    /// <summary>
    /// The query as Lucene parsed it (<c>Query.ToString()</c>), without the clause Examine wraps it
    /// in; null when it does not parse or the searcher is not a Lucene searcher.
    /// </summary>
    /// <param name="searcher">The searcher.</param>
    /// <param name="lucene">The query text.</param>
    /// <returns>The parsed query's text.</returns>
    private static string? ParsedText(ISearcher searcher, string lucene)
    {
        try
        {
            if (Parse(searcher, lucene).Query is not LuceneSearchQueryBase parsed)
            {
                return null;
            }

            Lucene.Net.Search.BooleanQuery outer = parsed.Query;
            return outer.Clauses.Count == 1 ? outer.Clauses[0].Query.ToString() : outer.ToString();
        }
        catch (InvalidNativeQueryException)
        {
            return null;
        }
    }

    /// <summary>Refuses raw queries on a target that is not a Lucene searcher.</summary>
    /// <param name="target">The target.</param>
    /// <param name="query">The raw query.</param>
    /// <returns>The query, trimmed; <c>*:*</c> when blank.</returns>
    /// <exception cref="NotSupportedException">The target takes no raw queries.</exception>
    private static string RequireNative(ExploreTarget target, string query)
    {
        if (!target.Capabilities.NativeQuery)
        {
            throw new NotSupportedException(
                $"{target.Name} is not searched by Lucene, so it takes no raw Lucene queries"
            );
        }

        // A blank query in native mode matches everything, as the whole-index view does.
        string trimmed = (query ?? string.Empty).Trim();
        return trimmed.Length == 0 ? LuceneQueryCompiler.MatchAll : trimmed;
    }

    /// <summary>
    /// The query a search runs: the raw query exactly as typed in native mode (ranked like any
    /// text query), otherwise the compiled chips. Search and explain share it, so an explanation
    /// is of the query that ran.
    /// </summary>
    /// <param name="filters">The chips.</param>
    /// <param name="culture">The culture picked, or null.</param>
    /// <param name="native">The raw query in native mode, or null.</param>
    /// <param name="schema">The target's fields.</param>
    /// <param name="target">The target.</param>
    /// <returns>The compiled query.</returns>
    /// <exception cref="NotSupportedException">A raw query on a target that takes none.</exception>
    private LuceneCompilation Compile(
        IReadOnlyList<Core.Explore.Filter> filters,
        string? culture,
        string? native,
        FieldSchema schema,
        ExploreTarget target
    ) =>
        native is null
            ? Compile(filters, culture, schema, target)
            : new LuceneCompilation(RequireNative(target, native), [], true);

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
            ? SearchersOf(multi)
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

    /// <summary>The searchers a multi-index searcher combines.</summary>
    /// <param name="multi">The multi-index searcher.</param>
    /// <returns>Its searchers.</returns>
    /// <remarks>
    /// Read by name: the property's type differs between Examine 3 and Examine 4 (which Umbraco
    /// Search brings), so a compiled call binds to one signature and throws
    /// <see cref="MissingMethodException"/> under the other.
    /// </remarks>
    internal static ISearcher[] SearchersOf(MultiIndexSearcher multi) =>
        typeof(MultiIndexSearcher)
            .GetProperty(nameof(MultiIndexSearcher.Searchers))
            ?.GetValue(multi)
            is System.Collections.IEnumerable searchers
            ? [.. searchers.OfType<ISearcher>()]
            : [];

    private static ExploreCapabilities CapabilitiesOf(ISearcher searcher)
    {
        bool lucene = searcher is BaseLuceneSearcher;
        return new ExploreCapabilities(
            NativeQuery: lucene,
            TermStatistics: lucene,
            ExplainScore: lucene
        );
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
            || (searcher is MultiIndexSearcher multi && SearchersOf(multi).Any(IsMember));
    }
}
