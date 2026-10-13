using System.Globalization;
using Examine;
using Examine.Lucene.Providers;
using Examine.Lucene.Search;
using Examine.Search;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Search.Core.Models.Configuration;
using Umbraco.Cms.Search.Core.Models.Searching;
using Umbraco.Cms.Search.Core.Models.Searching.Filtering;
using Umbraco.Cms.Search.Core.Services;
using Umbraco.Cms.Search.Core.Services.ContentIndexing;
using Umbraco.Community.SearchManager.Core.Diagnose;
using Umbraco.Community.SearchManager.Core.Explore;
using Umbraco.Community.SearchManager.UmbracoSearch.Explore;
using Umbraco.Community.SearchManager.UmbracoSearch.Indexes;
using IndexOptions = Umbraco.Cms.Search.Core.Configuration.IndexOptions;
using SearchFieldNames = Umbraco.Cms.Search.Core.Constants.FieldNames;
using SearchFilter = Umbraco.Cms.Search.Core.Models.Searching.Filtering.Filter;
using SearchManagerFilter = Umbraco.Community.SearchManager.Core.Explore.Filter;
using UmbracoSearcher = Umbraco.Cms.Search.Core.Services.ISearcher;

namespace Umbraco.Community.SearchManager.UmbracoSearch.Diagnose;

/// <summary>
/// "Find a node" for Umbraco Search indexes (ADR 0019). Umbraco Search keeps one document per
/// culture: with the Examine provider they are read from its physical index by
/// <c>Field_Umb_Id_keywords</c>; other providers are asked through <c>SearchAsync</c> with a key
/// filter, culture by culture, and cannot say whether their copy is out of date.
/// </summary>
internal sealed class UmbracoSearchNodePresenceSource : INodePresenceSource
{
    /// <summary>The Examine provider's culture field, and its value for invariant documents.</summary>
    private const string CultureField = "Sys_Culture";

    private const string Invariant = "none";

    /// <summary>The Examine provider's field holding the node key.</summary>
    private const string KeyField =
        UmbracoSearchFields.PhysicalPrefix + SearchFieldNames.Id + "_keywords";

    /// <summary>The Examine provider's field holding the update date.</summary>
    private const string UpdateDateField =
        UmbracoSearchFields.PhysicalPrefix + SearchFieldNames.UpdateDate + "_datetimeoffsets";

    /// <summary>Most documents read per node (one per culture).</summary>
    private const int MaxDocuments = 50;

    private readonly ContentIndexRegistration[] _registrations;
    private readonly UmbracoSearchExploreSource _explore;

    /// <summary>Creates the source.</summary>
    /// <param name="indexOptions">Umbraco Search's index registrations.</param>
    /// <param name="explore">Umbraco Search's Explore source, for compiling chips and reading stored fields.</param>
    /// <param name="indexerResolver">Umbraco Search's indexer resolver; null without <c>AddSearchCore()</c>, when nothing is checked.</param>
    public UmbracoSearchNodePresenceSource(
        IOptions<IndexOptions> indexOptions,
        UmbracoSearchExploreSource explore,
        IIndexerResolver? indexerResolver = null
    )
    {
        ArgumentNullException.ThrowIfNull(indexOptions);
        _registrations = indexerResolver is null
            ? []
            : indexOptions.Value.GetContentIndexRegistrations();
        _explore = explore;
    }

    /// <inheritdoc />
    public string Engine => UmbracoSearchIndexSource.EngineId;

    /// <inheritdoc />
    public async Task<IReadOnlyList<IndexPresence>> GetPresenceAsync(
        NodeFacts node,
        IReadOnlyList<ExploreTarget> targets,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(targets);

        var presences = new List<IndexPresence>();
        foreach (ExploreTarget target in targets)
        {
            ContentIndexRegistration? registration = Registration(target.Name);
            if (registration is not null)
            {
                presences.Add(await PresenceAsync(node, target, registration, cancellationToken));
            }
        }

        return presences;
    }

    /// <inheritdoc />
    public async Task<NodeMatchResult> MatchAsync(
        ExploreTarget target,
        NodeFacts node,
        NodeMatchRequest request,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(request);

        UmbracoSearcher searcher =
            _explore.SearcherFor(target.Name)
            ?? throw new NotSupportedException(
                $"Umbraco Search has no searcher for {target.Name}."
            );
        string? culture = string.IsNullOrWhiteSpace(request.Culture) ? null : request.Culture;
        IReadOnlyList<SearchManagerFilter> filters = request.Filters ?? [];
        KeywordFilter onlyNode = OnlyNode(node);

        // The node's stored values, from the Examine provider's physical index when there is one.
        IReadOnlyDictionary<string, IReadOnlyList<string>>? values = _explore.PhysicalIndexFor(
            target.Name
        )
            is { } physical
            ? UmbracoSearchExploreSource
                .ReadStoredFields(physical.Searcher, [node.Key], culture)
                .GetValueOrDefault(node.Key)
            : null;

        long inIndex = await CountAsync(searcher, target.Name, null, [onlyNode], culture);
        var clauses = new List<ClauseMatch>();
        for (int position = 0; position < filters.Count; position++)
        {
            SearchManagerFilter chip = filters[position];
            UmbracoSearchCompilation alone = _explore.CompileFor(target, [chip]);
            SkippedFilter? skipped = alone.Skipped.Count > 0 ? alone.Skipped[0] : null;
            bool? matched = skipped is null
                ? await CountAsync(
                    searcher,
                    target.Name,
                    alone.Query,
                    [.. alone.Filters.Select(filter => filter.Filter), onlyNode],
                    culture
                ) > 0
                : null;
            IReadOnlyList<string> stored =
                chip.Field is not null
                && values is not null
                && values.TryGetValue(chip.Field, out IReadOnlyList<string>? found)
                    ? found
                    : [];
            clauses.Add(new ClauseMatch(position, matched, skipped?.Reason, chip.Field, stored));
        }

        UmbracoSearchCompilation whole = _explore.CompileFor(target, filters);
        bool matches =
            inIndex > 0
            && await CountAsync(
                searcher,
                target.Name,
                whole.Query,
                [.. whole.Filters.Select(filter => filter.Filter), onlyNode],
                culture
            ) > 0;
        return new NodeMatchResult(
            inIndex > 0,
            matches,
            UmbracoSearchCSharp.For(target.Name, whole, culture, whole.Query is null),
            clauses
        );
    }

    /// <summary>Checks one Umbraco Search index for a node.</summary>
    private async Task<IndexPresence> PresenceAsync(
        NodeFacts node,
        ExploreTarget target,
        ContentIndexRegistration registration,
        CancellationToken cancellationToken
    )
    {
        bool publishedOnly = typeof(IPublishedContentChangeStrategy).IsAssignableFrom(
            registration.ContentChangeStrategy
        );
        bool holdsDocuments = registration.ContainedObjectTypes.Contains(
            UmbracoObjectTypes.Document
        );
        var rules = new IndexRules(
            HoldsEntityType: holdsDocuments,
            PublishedOnly: publishedOnly,
            // The draft index keeps trashed content, under the recycle bin; unpublishing drops it
            // from the published one.
            IncludesTrashed: !publishedOnly
        );
        if (!holdsDocuments)
        {
            return Missing(node, target, rules);
        }

        IIndex? physical = _explore.PhysicalIndexFor(target.Name);
        (IReadOnlyList<string> Cultures, int Count, DateTime? Date)? found = physical is not null
            ? ReadPhysical(physical, node)
            : await SearchAsync(target.Name, node, cancellationToken);
        if (found is null)
        {
            // An empty physical index (a first rebuild still running) is not "out of date".
            return Missing(
                node,
                target,
                rules with
                {
                    Empty = physical is not null && IsEmpty(physical),
                }
            );
        }

        DateTime? expected = StaleCheck.Expected(node, publishedOnly, null);
        string? viewCulture = found.Value.Cultures.Count > 0 ? found.Value.Cultures[0] : null;
        return new IndexPresence(
            target.Name,
            target.Engine,
            PresenceStatus.Present,
            found.Value.Count,
            found.Value.Cultures,
            null,
            StaleCheck.IsStale(found.Value.Date, expected),
            found.Value.Date,
            found.Value.Date is null ? null : expected,
            new SearchManagerFilter(
                FilterKind.Field,
                SearchFieldNames.Id,
                FilterOperator.Is,
                node.Key.ToString("D")
            ),
            node.Key.ToString("D"),
            viewCulture
        );
    }

    /// <summary>A missing or not-applicable entry with the first reason that applies.</summary>
    private static IndexPresence Missing(NodeFacts node, ExploreTarget target, IndexRules rules)
    {
        (PresenceStatus status, string reason) = PresenceRules.Explain(node, rules);
        return new IndexPresence(target.Name, target.Engine, status, 0, [], reason);
    }

    /// <summary>
    /// Reads the node's culture documents from the Examine provider's physical index, with the
    /// update date it records (the node's own, the same on every culture document).
    /// </summary>
    /// <returns>The cultures, document count and date, or null when the index has none.</returns>
    internal static (IReadOnlyList<string> Cultures, int Count, DateTime? Date)? ReadPhysical(
        IIndex physical,
        NodeFacts node
    )
    {
        if (physical.Searcher is not BaseLuceneSearcher lucene)
        {
            return null;
        }

        // A term query on the raw key field, read straight from Lucene: Examine's own query
        // builder types a field it has not written since start-up as full text (ADR 0003), which
        // would split the key and miss it.
        var cultures = new List<string>();
        string? updated = null;
        int count;
        using (ISearcherReference reference = lucene.GetSearchContext().GetSearcher())
        {
            IndexSearcher searcher = reference.IndexSearcher;
            TopDocs found = searcher.Search(
                new TermQuery(new Term(KeyField, node.Key.ToString("D"))),
                MaxDocuments
            );
            count = found.ScoreDocs.Length;
            foreach (ScoreDoc hit in found.ScoreDocs)
            {
                Lucene.Net.Documents.Document document = searcher.Doc(hit.Doc);
                string? culture = document.Get(CultureField, CultureInfo.InvariantCulture);
                if (culture is not null && culture != Invariant)
                {
                    cultures.Add(culture);
                }

                updated ??= document.Get(UpdateDateField, CultureInfo.InvariantCulture);
            }
        }

        return count == 0
            ? null
            : (
                [
                    .. cultures
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Order(StringComparer.Ordinal),
                ],
                count,
                Ticks(updated)
            );
    }

    /// <summary>
    /// Asks Umbraco Search for the node in every culture it has (and invariant), for providers
    /// whose documents cannot be read directly. They record no date this can compare.
    /// </summary>
    private async Task<(IReadOnlyList<string> Cultures, int Count, DateTime? Date)?> SearchAsync(
        string alias,
        NodeFacts node,
        CancellationToken cancellationToken
    )
    {
        UmbracoSearcher? searcher = _explore.SearcherFor(alias);
        if (searcher is null)
        {
            return null;
        }

        var cultures = new List<string>();
        int count = 0;
        foreach (string? culture in node.Cultures.Select(culture => culture.Iso).Prepend(null))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await CountAsync(searcher, alias, null, [OnlyNode(node)], culture) > 0)
            {
                count++;
                if (culture is not null)
                {
                    cultures.Add(culture);
                }
            }
        }

        return count == 0 ? null : (cultures, count, null);
    }

    /// <summary>Counts what a request finds, bypassing public access (Diagnose is a Settings tool).</summary>
    private static async Task<long> CountAsync(
        UmbracoSearcher searcher,
        string alias,
        string? query,
        IReadOnlyList<SearchFilter> filters,
        string? culture
    )
    {
        Umbraco.Cms.Search.Core.Models.Searching.SearchResult result = await searcher.SearchAsync(
            alias,
            query: query,
            filters: filters,
            facets: null,
            sorters: null,
            culture: culture,
            segment: null,
            accessContext: AccessContext.BypassProtection(),
            skip: 0,
            take: 1
        );
        return result.Total;
    }

    /// <summary>Whether a physical index holds no documents.</summary>
    private static bool IsEmpty(IIndex physical) =>
        physical.Searcher.CreateQuery().All().Execute(QueryOptions.SkipTake(0, 1)).TotalItemCount
        == 0;

    /// <summary>The filter that keeps only the node's documents.</summary>
    private static KeywordFilter OnlyNode(NodeFacts node) =>
        new(SearchFieldNames.Id, [node.Key.ToString("D")], false);

    private ContentIndexRegistration? Registration(string alias) =>
        _registrations.FirstOrDefault(candidate =>
            string.Equals(candidate.IndexAlias, alias, StringComparison.OrdinalIgnoreCase)
        );

    /// <summary>Reads a date the provider stored as ticks (of the offset's clock time).</summary>
    private static DateTime? Ticks(string? value) =>
        long.TryParse(
            value,
            System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture,
            out long ticks
        )
        && ticks >= DateTime.MinValue.Ticks
        && ticks <= DateTime.MaxValue.Ticks
            ? new DateTime(ticks, DateTimeKind.Unspecified)
            : null;

    private static string? First(ISearchResult result, string field) =>
        result.AllValues.TryGetValue(field, out IReadOnlyList<string>? values) && values.Count > 0
            ? values[0]
            : null;
}
