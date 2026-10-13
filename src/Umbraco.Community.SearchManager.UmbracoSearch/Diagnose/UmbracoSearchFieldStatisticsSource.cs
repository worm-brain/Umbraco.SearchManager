using Examine;
using Examine.Lucene.Providers;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Search.Core.Models.Configuration;
using Umbraco.Cms.Search.Core.Services;
using Umbraco.Cms.Search.Provider.Examine.Services;
using Umbraco.Community.SearchManager.Core.Diagnose;
using Umbraco.Community.SearchManager.Core.Explore;
using Umbraco.Community.SearchManager.Features.Diagnose.Fields;
using Umbraco.Community.SearchManager.UmbracoSearch.Explore;
using Umbraco.Community.SearchManager.UmbracoSearch.Indexes;
using IndexOptions = Umbraco.Cms.Search.Core.Configuration.IndexOptions;

namespace Umbraco.Community.SearchManager.UmbracoSearch.Diagnose;

/// <summary>
/// Field statistics for Umbraco Search indexes (ADR 0018): read from the Examine provider's active
/// physical index, with each physical field (<c>Field_title_texts</c>) shown under the name
/// filters take (<c>title</c>). The provider keeps one document per culture, so the counts are of
/// culture documents. Other providers expose no Lucene index and have none.
/// </summary>
internal sealed class UmbracoSearchFieldStatisticsSource : IFieldStatisticsSource
{
    /// <summary>The provider's field holding each document's culture, and its value for invariant ones.</summary>
    private const string CultureField = "Sys_Culture";

    private const string Invariant = "none";

    /// <summary>The prefix of the provider's own fields, such as <c>Sys_Culture</c>.</summary>
    private const string ProviderPrefix = "Sys_";

    private readonly ContentIndexRegistration[] _registrations;
    private readonly IExamineManager _examineManager;
    private readonly ILanguageService _languageService;
    private readonly FieldStatisticsCache _cache;
    private readonly IActiveIndexManager? _activeIndexManager;

    /// <summary>Labels of Umbraco Search's system fields, from the search box's aliases.</summary>
    private static readonly Dictionary<string, string> Labels = UmbracoSearchFields
        .Aliases.GroupBy(alias => alias.Field, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(group => group.Key, group => group.First().Label, StringComparer.Ordinal);

    /// <summary>Creates the source.</summary>
    /// <param name="indexOptions">Umbraco Search's index registrations.</param>
    /// <param name="examineManager">Finds the Examine provider's physical indexes.</param>
    /// <param name="languageService">The default culture, for links into Explore.</param>
    /// <param name="cache">The statistics cache.</param>
    /// <param name="indexerResolver">Umbraco Search's indexer resolver; null without <c>AddSearchCore()</c>, when nothing is listed.</param>
    /// <param name="activeIndexManager">The Examine provider's slot manager; null without the provider.</param>
    public UmbracoSearchFieldStatisticsSource(
        IOptions<IndexOptions> indexOptions,
        IExamineManager examineManager,
        ILanguageService languageService,
        FieldStatisticsCache cache,
        IIndexerResolver? indexerResolver = null,
        IActiveIndexManager? activeIndexManager = null
    )
    {
        ArgumentNullException.ThrowIfNull(indexOptions);
        _registrations = indexerResolver is null
            ? []
            : indexOptions.Value.GetContentIndexRegistrations();
        _examineManager = examineManager;
        _languageService = languageService;
        _cache = cache;
        _activeIndexManager = activeIndexManager;
    }

    /// <inheritdoc />
    public string Engine => UmbracoSearchIndexSource.EngineId;

    /// <inheritdoc />
    public async Task<FieldStatisticsResult> GetFieldStatisticsAsync(
        ExploreTarget target,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(target);
        ContentIndexRegistration? registration = _registrations.FirstOrDefault(candidate =>
            string.Equals(candidate.IndexAlias, target.Name, StringComparison.OrdinalIgnoreCase)
        );
        if (registration is null)
        {
            return FieldStatisticsResult.Unavailable(
                target,
                $"Umbraco Search has no index called {target.Name}."
            );
        }

        if (!UmbracoSearchPhysicalIndexes.IsExamineBacked(registration))
        {
            return FieldStatisticsResult.Unavailable(
                target,
                $"Umbraco Search's {registration.Indexer.Name} provider does not expose a Lucene index, so its terms cannot be read."
            );
        }

        if (
            UmbracoSearchPhysicalIndexes.Find(
                _registrations,
                target.Name,
                _activeIndexManager,
                _examineManager
            )
            is not LuceneIndex { Searcher: BaseLuceneSearcher } physical
        )
        {
            return FieldStatisticsResult.Unavailable(
                target,
                $"The Lucene index behind {target.Name} could not be found."
            );
        }

        LuceneFieldReading reading = await _cache.GetAsync(
            target.Name,
            physical,
            (reader, token) => LuceneFieldStatistics.Read(reader, physical, Identify, token),
            cancellationToken
        );

        // Without a culture Explore only finds invariant documents, so links into it pick the
        // default culture when the index holds variant ones.
        FieldStatistic? cultures = reading.Fields.FirstOrDefault(field =>
            field.Name == CultureField
        );
        bool variant =
            cultures is not null
            && (
                cultures.UniqueTerms > 1
                || cultures.TopTerms.Any(term =>
                    !string.Equals(term.Value, Invariant, StringComparison.Ordinal)
                )
            );

        return new FieldStatisticsResult(
            target,
            true,
            null,
            reading.DocumentCount,
            reading.DeletedDocumentCount,
            reading.CoverageExact,
            DocumentsPerCulture: true,
            ExploreCulture: variant ? await _languageService.GetDefaultIsoCodeAsync() : null,
            reading.Fields
        );
    }

    /// <summary>
    /// Names a physical field of the Examine provider: <c>Field_title_texts</c> is <c>title</c>,
    /// <c>Field_title_texts_mobile</c> its <c>mobile</c> segment, and the provider's and Examine's
    /// own fields (<c>Sys_*</c>, <c>__*</c>) are system fields under their physical name.
    /// </summary>
    /// <param name="physical">The physical field name.</param>
    /// <returns>The identity.</returns>
    internal static LuceneFieldIdentity Identify(string physical)
    {
        ArgumentNullException.ThrowIfNull(physical);
        if (UmbracoSearchFields.TryParse(physical, out string? name, out _, out string? segment))
        {
            return segment is null
                ? new LuceneFieldIdentity(
                    name,
                    Labels.GetValueOrDefault(name),
                    FieldGroup.Field,
                    null
                )
                : new LuceneFieldIdentity(name, $"{segment} segment", FieldGroup.Segment, null);
        }

        bool system =
            physical.StartsWith(ProviderPrefix, StringComparison.Ordinal)
            || physical.StartsWith(FieldGroups.SystemPrefix, StringComparison.Ordinal);
        return new LuceneFieldIdentity(
            physical,
            null,
            system ? FieldGroup.System : FieldGroup.Field,
            null
        );
    }
}
