using System.Globalization;
using Examine;
using Examine.Lucene.Providers;
using Examine.Lucene.Search;
using Examine.Search;
using Lucene.Net.QueryParsers.Classic;
using Umbraco.Cms.Core;
using Umbraco.Cms.Infrastructure.Examine;
using Umbraco.Community.SearchManager.Core.Diagnose;
using Umbraco.Community.SearchManager.Core.Explore;
using Umbraco.Community.SearchManager.Features.Explore;
using Umbraco.Community.SearchManager.Features.Indexes;

namespace Umbraco.Community.SearchManager.Features.Diagnose.Nodes;

/// <summary>
/// "Find a node" for core Examine indexes (ADR 0019): looks the node up in each index by id,
/// explains a missing node from the index's own value set validator, and tests a query chip by
/// chip by restricting each chip to the node.
/// </summary>
/// <param name="examineManager">Examine's indexes.</param>
/// <param name="timeProvider">Today, for relative date chips.</param>
internal sealed class ExamineNodePresenceSource(
    IExamineManager examineManager,
    TimeProvider timeProvider
) : INodePresenceSource
{
    /// <summary>The category Umbraco gives content documents (<c>__IndexType</c>).</summary>
    private const string ContentCategory = "content";

    /// <summary>The Delivery API index keeps one document per culture, keyed by this field rather than <c>__NodeId</c>.</summary>
    /// <remarks>
    /// A literal: <c>UmbracoExamineFieldNames.DeliveryApiContentIndex.Id</c> is missing from some
    /// 17.x versions the package supports.
    /// </remarks>
    private const string DeliveryApiIdField = "id";

    /// <summary>The Delivery API index's culture field and its value for invariant documents.</summary>
    private const string DeliveryApiCultureField = "culture";

    private const string Invariant = "none";

    /// <summary>The prefix of a variant document's per-culture published flags.</summary>
    private const string PublishedPrefix = UmbracoExamineFieldNames.PublishedFieldName + "_";

    /// <summary>Most documents read per node (the Delivery API index has one per culture).</summary>
    private const int MaxDocuments = 50;

    private readonly ExamineTargets _targets = new(examineManager);

    /// <inheritdoc />
    public string Engine => ExamineIndexSource.EngineId;

    /// <inheritdoc />
    public Task<IReadOnlyList<IndexPresence>> GetPresenceAsync(
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
            cancellationToken.ThrowIfCancellationRequested();
            if (_targets.IndexOf(target) is { } index)
            {
                presences.Add(Presence(node, target, index));
            }
        }

        return Task.FromResult<IReadOnlyList<IndexPresence>>(presences);
    }

    /// <inheritdoc />
    public Task<NodeMatchResult> MatchAsync(
        ExploreTarget target,
        NodeFacts node,
        NodeMatchRequest request,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(request);

        IIndex index =
            _targets.IndexOf(target)
            ?? throw new NotSupportedException(
                $"{target.Name} is a searcher; test a query against one of its indexes."
            );
        ISearcher searcher = index.Searcher;
        FieldSchema schema = ExamineFields.Read(searcher, _targets.IndexesBehind(searcher));
        string? culture = string.IsNullOrWhiteSpace(request.Culture) ? null : request.Culture;
        IReadOnlyList<Filter> filters = request.Filters ?? [];
        Filter onlyNode = NodeFilter(index, node, schema);
        DateOnly today = DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime);

        LuceneCompilation Compile(IReadOnlyList<Filter> chips) =>
            LuceneQueryCompiler.Compile(
                chips,
                schema,
                culture,
                today,
                target.Capabilities.NativeQuery
            );

        // The node's own document, for the values each chip's field holds.
        ISearchResult? document = Documents(index, node)
            .OrderByDescending(result => IsCultureDocument(result, culture))
            .FirstOrDefault();

        var clauses = new List<ClauseMatch>();
        for (int position = 0; position < filters.Count; position++)
        {
            Filter chip = filters[position];
            IndexField? field =
                chip.Kind == FilterKind.Field && chip.Field is not null
                    ? schema.Resolve(chip.Field) is { } resolved
                        ? schema.ForCulture(resolved, culture)
                        : null
                    : null;
            IReadOnlyList<string> values =
                field is not null
                && document is not null
                && document.AllValues.TryGetValue(field.Name, out IReadOnlyList<string>? stored)
                    ? [.. stored]
                    : [];

            // The chip and "is this node" together: a hit means the node matches the chip.
            LuceneCompilation alone = Compile([chip, onlyNode]);
            SkippedFilter? skipped = alone.Skipped.FirstOrDefault(skip => skip.Index == 0);
            clauses.Add(
                new ClauseMatch(
                    position,
                    skipped is null ? Count(searcher, alone.Query) > 0 : null,
                    skipped?.Reason,
                    field?.Name ?? chip.Field,
                    values
                )
            );
        }

        LuceneCompilation whole = Compile([.. filters, onlyNode]);
        return Task.FromResult(
            new NodeMatchResult(
                document is not null,
                document is not null && Count(searcher, whole.Query) > 0,
                Compile(filters).Query,
                clauses
            )
        );
    }

    /// <summary>Checks one index for a node.</summary>
    /// <param name="node">The node.</param>
    /// <param name="target">The index's target.</param>
    /// <param name="index">The index.</param>
    /// <returns>The presence.</returns>
    private static IndexPresence Presence(NodeFacts node, ExploreTarget target, IIndex index)
    {
        bool deliveryApi = IsDeliveryApi(index);
        bool publishedOnly = index is IUmbracoIndex umbraco
            ? umbraco.PublishedValuesOnly
            : Validator(index)?.PublishedValuesOnly ?? false;
        ISearchResult[] documents = [.. Documents(index, node)];
        if (documents.Length == 0)
        {
            (PresenceStatus status, string reason) = PresenceRules.Explain(
                node,
                Rules(index, node)
            );
            return new IndexPresence(target.Name, target.Engine, status, 0, [], reason);
        }

        string[] cultures = deliveryApi
            ?
            [
                .. documents
                    .Select(result => First(result, DeliveryApiCultureField))
                    .OfType<string>()
                    .Where(culture => culture != Invariant)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Order(StringComparer.Ordinal),
            ]
            : CulturesOf(documents[0], publishedOnly);

        // The date the index holds against the one it should, per culture for variant content
        // (the first stale one wins); the Delivery API index records no date.
        (DateTime? indexDate, DateTime? expected, bool? stale) = deliveryApi
            ? (null, null, null)
            : Staleness(documents[0], node, publishedOnly, cultures);

        return new IndexPresence(
            target.Name,
            target.Engine,
            PresenceStatus.Present,
            documents.Length,
            cultures,
            null,
            stale,
            indexDate,
            expected,
            NodeFilter(index, node, schema: null),
            documents[0].Id,
            null
        );
    }

    /// <summary>Compares the dates the index holds for a node with the node's own.</summary>
    /// <param name="document">The node's document.</param>
    /// <param name="node">The node.</param>
    /// <param name="publishedOnly">Whether the index holds published content only.</param>
    /// <param name="cultures">The cultures the document has.</param>
    /// <returns>The index date, the expected date, and whether the index is behind.</returns>
    private static (DateTime? Index, DateTime? Expected, bool? Stale) Staleness(
        ISearchResult document,
        NodeFacts node,
        bool publishedOnly,
        string[] cultures
    )
    {
        if (node.Cultures.Count == 0 || cultures.Length == 0)
        {
            DateTime? indexDate = ExamineHits.Ticks(First(document, "updateDate"));
            DateTime? expected = StaleCheck.Expected(node, publishedOnly, null);
            return (indexDate, expected, StaleCheck.IsStale(indexDate, expected));
        }

        (DateTime? Index, DateTime? Expected, bool? Stale) first = (null, null, null);
        foreach (string culture in cultures)
        {
            DateTime? indexDate = ExamineHits.Ticks(
                First(document, "updateDate_" + culture.ToLowerInvariant())
            );
            DateTime? expected = StaleCheck.Expected(node, publishedOnly, culture);
            bool? stale = StaleCheck.IsStale(indexDate, expected);
            if (stale is true)
            {
                return (indexDate, expected, true);
            }

            if (first.Stale is null)
            {
                first = (indexDate, expected, stale);
            }
        }

        return first;
    }

    /// <summary>Reads what an index's rules say about a node it does not hold.</summary>
    /// <param name="index">The index.</param>
    /// <param name="node">The node.</param>
    /// <returns>The rules.</returns>
    private static IndexRules Rules(IIndex index, NodeFacts node)
    {
        if (index is IUmbracoMemberIndex)
        {
            return new IndexRules(HoldsEntityType: false);
        }

        IContentValueSetValidator? content = Validator(index);
        ValueSetValidator? types =
            (index as BaseIndexProvider)?.ValueSetValidator as ValueSetValidator;
        bool included =
            (
                types?.IncludeItemTypes is not { } include
                || !include.Any()
                || include.Contains(node.ContentTypeAlias, StringComparer.OrdinalIgnoreCase)
            )
            && (
                types?.ExcludeItemTypes is not { } exclude
                || !exclude.Contains(node.ContentTypeAlias, StringComparer.OrdinalIgnoreCase)
            );
        bool publishedOnly = index is IUmbracoIndex umbraco
            ? umbraco.PublishedValuesOnly
            : content?.PublishedValuesOnly ?? false;

        return new IndexRules(
            HoldsEntityType: HoldsContent(index, content),
            ItemTypeIncluded: included,
            Empty: DocumentCount(index) == 0,
            PublishedOnly: publishedOnly,
            IncludesTrashed: content?.ValidateRecycleBin(node.Path, ContentCategory)
                ?? !publishedOnly,
            ParentId: content?.ParentId,
            PathAllowed: content?.ValidatePath(node.Path, ContentCategory) ?? true,
            IncludesProtected: content?.SupportProtectedContent ?? true
        );
    }

    /// <summary>
    /// Whether an index holds content documents: it has some (<c>__IndexType</c>), or, while it is
    /// empty, it validates content. Custom indexes of other categories are not applicable.
    /// </summary>
    /// <param name="index">The index.</param>
    /// <param name="validator">Its content validator, if any.</param>
    /// <returns><see langword="true"/> when the index holds content.</returns>
    private static bool HoldsContent(IIndex index, IContentValueSetValidator? validator)
    {
        if (index.Searcher is BaseLuceneSearcher lucene)
        {
            using ISearcherReference reference = lucene.GetSearchContext().GetSearcher();
            Lucene.Net.Index.Terms? categories = Lucene.Net.Index.MultiFields.GetTerms(
                reference.IndexSearcher.IndexReader,
                ExamineFieldNames.CategoryFieldName
            );
            if (categories is not null)
            {
                Lucene.Net.Index.TermsEnum iterator = categories.GetEnumerator();
                while (iterator.MoveNext())
                {
                    if (iterator.Term.Utf8ToString() == ContentCategory)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        return validator is not null || IsDeliveryApi(index);
    }

    /// <summary>The documents an index holds for a node.</summary>
    /// <param name="index">The index.</param>
    /// <param name="node">The node.</param>
    /// <returns>The documents (one per culture in the Delivery API index).</returns>
    private static ISearchResults Documents(IIndex index, NodeFacts node)
    {
        string id = node.Id.ToString(CultureInfo.InvariantCulture);
        IQuery query = index.Searcher.CreateQuery();
        IBooleanOperation lookup = IsDeliveryApi(index)
            ? query.Field(DeliveryApiIdField, id)
            : query.Id(id);
        return lookup.Execute(QueryOptions.SkipTake(0, MaxDocuments));
    }

    /// <summary>
    /// The chip that keeps only the node's documents: <c>__NodeId</c>, or the Delivery API index's
    /// <c>id</c> field.
    /// </summary>
    /// <param name="index">The index.</param>
    /// <param name="node">The node.</param>
    /// <param name="schema">The index's fields, to pick an operator the field's type takes; null for the default.</param>
    /// <returns>The chip.</returns>
    private static Filter NodeFilter(IIndex index, NodeFacts node, FieldSchema? schema)
    {
        string id = node.Id.ToString(CultureInfo.InvariantCulture);
        if (!IsDeliveryApi(index))
        {
            return new Filter(
                FilterKind.Field,
                ExamineFieldNames.ItemIdFieldName,
                FilterOperator.Is,
                id
            );
        }

        FieldType type = schema?.Resolve(DeliveryApiIdField)?.Type ?? FieldType.Keyword;
        return new Filter(
            FilterKind.Field,
            DeliveryApiIdField,
            type == FieldType.Text ? FilterOperator.Phrase : FilterOperator.Is,
            id
        );
    }

    /// <summary>Counts the documents a Lucene query finds.</summary>
    /// <param name="searcher">The searcher.</param>
    /// <param name="lucene">The query text.</param>
    /// <returns>The count, or 0 when the query does not parse.</returns>
    private static long Count(ISearcher searcher, string lucene)
    {
        try
        {
            return searcher
                .CreateQuery()
                .NativeQuery(lucene)
                .Execute(QueryOptions.SkipTake(0, 1))
                .TotalItemCount;
        }
        catch (ParseException)
        {
            return 0;
        }
    }

    /// <summary>Whether a document is the given culture's (Delivery API index), so its values are shown.</summary>
    private static bool IsCultureDocument(ISearchResult result, string? culture) =>
        culture is not null
        && string.Equals(
            First(result, DeliveryApiCultureField),
            culture,
            StringComparison.OrdinalIgnoreCase
        );

    /// <summary>
    /// The cultures a variant document holds, from its <c>__Published_{culture}</c> fields: every
    /// one in a draft index, only the published ones in a published-only index.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="publishedOnly">Whether the index holds published content only.</param>
    /// <returns>The cultures, lower case as Umbraco writes them; empty for invariant content.</returns>
    private static string[] CulturesOf(ISearchResult document, bool publishedOnly) =>
        First(document, UmbracoExamineFieldNames.VariesByCultureFieldName) != "y"
            ? []
            :
            [
                .. document
                    .AllValues.Where(pair =>
                        pair.Key.StartsWith(PublishedPrefix, StringComparison.OrdinalIgnoreCase)
                        && (!publishedOnly || (pair.Value.Count > 0 && pair.Value[0] == "y"))
                    )
                    .Select(pair => pair.Key[PublishedPrefix.Length..])
                    .Order(StringComparer.Ordinal),
            ];

    private static bool IsDeliveryApi(IIndex index) =>
        string.Equals(
            index.Name,
            Constants.UmbracoIndexes.DeliveryApiContentIndexName,
            StringComparison.OrdinalIgnoreCase
        );

    private static IContentValueSetValidator? Validator(IIndex index) =>
        (index as BaseIndexProvider)?.ValueSetValidator as IContentValueSetValidator;

    private static long DocumentCount(IIndex index) =>
        index.Searcher.CreateQuery().All().Execute(QueryOptions.SkipTake(0, 1)).TotalItemCount;

    private static string? First(ISearchResult result, string field) =>
        result.AllValues.TryGetValue(field, out IReadOnlyList<string>? values) && values.Count > 0
            ? values[0]
            : null;
}
