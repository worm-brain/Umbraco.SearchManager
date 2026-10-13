using Examine;
using Examine.Lucene.Indexing;
using Examine.Lucene.Providers;
using Examine.Lucene.Search;
using Examine.Search;
using Lucene.Net.Index;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Search.Core.Models.Configuration;
using Umbraco.Cms.Search.Core.Services;
using Umbraco.Cms.Search.Provider.Examine.Configuration;
using Umbraco.Cms.Search.Provider.Examine.Services;
using Umbraco.Community.SearchManager.Core.Diagnose;
using Umbraco.Community.SearchManager.Core.Explore;
using Umbraco.Community.SearchManager.Features.Diagnose.Analysis;
using Umbraco.Community.SearchManager.UmbracoSearch.Explore;
using Umbraco.Community.SearchManager.UmbracoSearch.Indexes;
using IndexOptions = Umbraco.Cms.Search.Core.Configuration.IndexOptions;

namespace Umbraco.Community.SearchManager.UmbracoSearch.Diagnose;

/// <summary>
/// The analyzer view for Umbraco Search indexes (ADR 0023), run on the Examine provider's active
/// physical index. Search terms are judged with the query shapes the provider's searcher builds
/// (copied from 17.2.0 / 18.2.0, where they are identical): the free-text query on the aggregated
/// text field, and a text filter on a text field. Keyword filters compare whole values, so their
/// fields are analysed but not judged. Other providers expose no Lucene index.
/// </summary>
internal sealed class UmbracoSearchAnalyzerSource : IAnalyzerSource
{
    /// <summary>
    /// The field free-text searches read, picked first. The provider's constant for it is internal,
    /// so the name is copied (verified against 17.2.0 and 18.2.0).
    /// </summary>
    internal const string AggregatedField = "Sys_aggregated_texts";

    private readonly ContentIndexRegistration[] _registrations;
    private readonly IExamineManager _examineManager;
    private readonly IActiveIndexManager? _activeIndexManager;

    /// <summary>Labels of Umbraco Search's system fields, from the search box's aliases.</summary>
    private static readonly Dictionary<string, string> Labels = UmbracoSearchFields
        .Aliases.GroupBy(alias => alias.Field, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(group => group.Key, group => group.First().Label, StringComparer.Ordinal);

    /// <summary>Creates the source.</summary>
    /// <param name="indexOptions">Umbraco Search's index registrations.</param>
    /// <param name="examineManager">Finds the Examine provider's physical indexes.</param>
    /// <param name="indexerResolver">Umbraco Search's indexer resolver; null without <c>AddSearchCore()</c>, when nothing is listed.</param>
    /// <param name="activeIndexManager">The Examine provider's slot manager; null without the provider.</param>
    public UmbracoSearchAnalyzerSource(
        IOptions<IndexOptions> indexOptions,
        IExamineManager examineManager,
        IIndexerResolver? indexerResolver = null,
        IActiveIndexManager? activeIndexManager = null
    )
    {
        ArgumentNullException.ThrowIfNull(indexOptions);
        _registrations = indexerResolver is null
            ? []
            : indexOptions.Value.GetContentIndexRegistrations();
        _examineManager = examineManager;
        _activeIndexManager = activeIndexManager;
    }

    /// <inheritdoc />
    public string Engine => UmbracoSearchIndexSource.EngineId;

    /// <inheritdoc />
    public Task<AnalyzerFieldsResult> GetFieldsAsync(
        ExploreTarget target,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(target);
        (LuceneIndex? physical, string? reason) = Physical(target);
        if (physical is null)
        {
            return Task.FromResult(AnalyzerFieldsResult.Unavailable(target, reason!));
        }

        AnalyzerField[] fields = [.. Fields(physical).Values];
        return Task.FromResult(
            new AnalyzerFieldsResult(target, true, null, fields.FirstOrDefault()?.Name, fields)
        );
    }

    /// <inheritdoc />
    public Task<AnalysisResult> AnalyzeAsync(
        ExploreTarget target,
        AnalyzeRequest request,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(request);
        (LuceneIndex? physical, string? reason) = Physical(target);
        if (physical is null)
        {
            return Task.FromResult(AnalysisResult.Unavailable(target, request.Field, reason!));
        }

        if (!Fields(physical).TryGetValue(request.Field, out AnalyzerField? field))
        {
            return Task.FromResult(
                AnalysisResult.Unavailable(
                    target,
                    request.Field,
                    $"{target.Name} has no text or keyword field called {request.Field}."
                )
            );
        }

        string physicalField = field.Physical ?? field.Name;
        Func<string, SearchTermQuery>? search = field.Type switch
        {
            _ when field.Name == AggregatedField => term => new SearchTermQuery(
                FreeTextQuery(physical, physicalField, term),
                []
            ),
            FieldType.Text => term => new SearchTermQuery(
                TextFilterQuery(physical, physicalField, term),
                []
            ),
            _ => null,
        };

        return Task.FromResult(
            LuceneTextAnalysis.Analyze(
                target,
                physical,
                field.Name,
                physicalField,
                LuceneTextAnalysis.RegisteredValueTypeOf(physical, physicalField),
                request.Text,
                request.SearchTerm,
                search,
                "Keyword filters compare whole values, so a search is not judged on this field."
            )
        );
    }

    /// <summary>
    /// The provider's free-text query on one field: the search split on spaces, every word
    /// required, each as the word itself or the start of a word (<c>Searcher.CreateAggregatedTextQuery</c>).
    /// </summary>
    /// <param name="physical">The physical index, whose searcher parses the words.</param>
    /// <param name="field">The physical field.</param>
    /// <param name="term">The search.</param>
    /// <returns>The query, or null when the search has no words.</returns>
    internal static Lucene.Net.Search.Query? FreeTextQuery(
        LuceneIndex physical,
        string field,
        string term
    )
    {
        IQuery query = physical.Searcher.CreateQuery();
        IBooleanOperation? operation = null;
        foreach (string word in term.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            INestedBooleanOperation Group(INestedQuery nested) =>
                nested
                    .Field(field, word.Boost(1f))
                    .Or()
                    .Field(field, word.MultipleCharacterWildcard().WithBoost(1f));
            operation = operation is null
                ? query.Group(Group, BooleanOperation.Or)
                : operation.And().Group(Group, BooleanOperation.Or);
        }

        return operation is null ? null : ((LuceneSearchQueryBase)query).Query;
    }

    /// <summary>
    /// The provider's text filter on one field: the whole value as typed, or the start of a term
    /// (<c>Searcher.AddFilters</c> for a <c>TextFilter</c>, whose obsolete <c>Explicit</c> parses as <c>Default</c> does).
    /// </summary>
    /// <param name="physical">The physical index, whose searcher parses the value.</param>
    /// <param name="field">The physical field.</param>
    /// <param name="term">The value.</param>
    /// <returns>The query.</returns>
    internal static Lucene.Net.Search.Query TextFilterQuery(
        LuceneIndex physical,
        string field,
        string term
    )
    {
        IQuery query = physical.Searcher.CreateQuery();
        query.GroupedOr(
            [field],
            ExamineValue.Create(Examineness.Default, term),
            ExamineValue.Create(Examineness.ComplexWildcard, term)
        );
        return ((LuceneSearchQueryBase)query).Query;
    }

    /// <summary>The physical index behind a target, or why there is none.</summary>
    /// <param name="target">The target.</param>
    /// <returns>The index, or null and the reason for the view.</returns>
    private (LuceneIndex? Index, string? Reason) Physical(ExploreTarget target)
    {
        ContentIndexRegistration? registration = _registrations.FirstOrDefault(candidate =>
            string.Equals(candidate.IndexAlias, target.Name, StringComparison.OrdinalIgnoreCase)
        );
        if (registration is null)
        {
            return (null, $"Umbraco Search has no index called {target.Name}.");
        }

        if (!UmbracoSearchPhysicalIndexes.IsExamineBacked(registration))
        {
            return (
                null,
                $"Umbraco Search's {registration.Indexer.Name} provider does not expose a Lucene index, so its analyzer cannot be run."
            );
        }

        return
            UmbracoSearchPhysicalIndexes.Find(
                _registrations,
                target.Name,
                _activeIndexManager,
                _examineManager
            )
                is LuceneIndex { Searcher: BaseLuceneSearcher } physical
            ? (physical, null)
            : (null, $"The Lucene index behind {target.Name} could not be found.");
    }

    /// <summary>
    /// The text and keyword fields of a physical index under the names filters take, with the
    /// aggregated text field first, keyed by name.
    /// </summary>
    /// <param name="physical">The physical index.</param>
    /// <returns>The fields, in picker order.</returns>
    private static Dictionary<string, AnalyzerField> Fields(LuceneIndex physical)
    {
        string[] names = PhysicalNames(physical);
        UmbracoSearchFields kinds = UmbracoSearchFields.FromPhysicalNames(names);
        var fields = new Dictionary<string, AnalyzerField>(StringComparer.OrdinalIgnoreCase);

        if (names.Contains(AggregatedField, StringComparer.Ordinal))
        {
            fields[AggregatedField] = new AnalyzerField(
                AggregatedField,
                "All text (what a search query reads)",
                FieldType.Text,
                FieldGroup.System,
                Analyzer(physical, AggregatedField),
                null
            );
        }

        foreach (string physicalName in names.Order(StringComparer.OrdinalIgnoreCase))
        {
            // One row per field, from the physical field of the kind filters use; segments are left out.
            if (
                !UmbracoSearchFields.TryParse(
                    physicalName,
                    out string? name,
                    out FieldValues values,
                    out string? segment
                )
                || segment is not null
                || kinds.KindOf(name) != values
                || UmbracoSearchFields.TypeOf(values) is not (FieldType.Text or FieldType.Keyword)
                || fields.ContainsKey(name)
            )
            {
                continue;
            }

            fields[name] = new AnalyzerField(
                name,
                Labels.GetValueOrDefault(name),
                UmbracoSearchFields.TypeOf(values),
                FieldGroup.Field,
                Analyzer(physical, physicalName),
                physicalName
            );
        }

        return fields;
    }

    /// <summary>The analyzer name of one physical field.</summary>
    private static string Analyzer(LuceneIndex physical, string physicalField) =>
        LuceneTextAnalysis.AnalyzerName(
            physical,
            LuceneTextAnalysis.RegisteredValueTypeOf(physical, physicalField)
        );

    /// <summary>Every field name of the physical index's reader.</summary>
    /// <param name="physical">The physical index; its searcher is a Lucene searcher.</param>
    /// <returns>The names.</returns>
    private static string[] PhysicalNames(LuceneIndex physical)
    {
        using ISearcherReference reference = ((BaseLuceneSearcher)physical.Searcher)
            .GetSearchContext()
            .GetSearcher();
        return
        [
            .. MultiFields
                .GetMergedFieldInfos(reference.IndexSearcher.IndexReader)
                .Select(info => info.Name),
        ];
    }
}
