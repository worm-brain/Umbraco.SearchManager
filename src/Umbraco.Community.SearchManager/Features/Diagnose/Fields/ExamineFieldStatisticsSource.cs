using Examine;
using Examine.Lucene.Providers;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Community.SearchManager.Core.Diagnose;
using Umbraco.Community.SearchManager.Core.Explore;
using Umbraco.Community.SearchManager.Features.Explore;
using Umbraco.Community.SearchManager.Features.Indexes;

namespace Umbraco.Community.SearchManager.Features.Diagnose.Fields;

/// <summary>
/// Field statistics for core Examine indexes (ADR 0018): read from the index's own Lucene reader
/// and cached until it changes. A searcher registered on its own reads several indexes, so it has
/// none; nor has an index that is not a Lucene index.
/// </summary>
/// <param name="examineManager">Examine's indexes.</param>
/// <param name="languageService">The site's cultures, to spot culture variants.</param>
/// <param name="cache">The statistics cache.</param>
internal sealed class ExamineFieldStatisticsSource(
    IExamineManager examineManager,
    ILanguageService languageService,
    FieldStatisticsCache cache
) : IFieldStatisticsSource
{
    private readonly ExamineTargets _targets = new(examineManager);

    /// <summary>Labels of Umbraco's own fields, from the search box's aliases.</summary>
    private static readonly Dictionary<string, string> Labels = ExamineFields
        .Aliases.GroupBy(alias => alias.Field, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(group => group.Key, group => group.First().Label, StringComparer.Ordinal);

    /// <inheritdoc />
    public string Engine => ExamineIndexSource.EngineId;

    /// <inheritdoc />
    public async Task<FieldStatisticsResult> GetFieldStatisticsAsync(
        ExploreTarget target,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.Kind == ExploreTargetKind.Searcher)
        {
            return FieldStatisticsResult.Unavailable(
                target,
                $"{target.Name} is a searcher, and field statistics are read per index. Pick one of the indexes it searches."
            );
        }

        if (_targets.IndexOf(target) is not LuceneIndex { Searcher: BaseLuceneSearcher } index)
        {
            return FieldStatisticsResult.Unavailable(
                target,
                $"{target.Name} is not a Lucene index, so its terms cannot be read."
            );
        }

        IEnumerable<ILanguage> languages = await languageService.GetAllAsync();
        string[] cultures = [.. languages.Select(language => language.IsoCode)];

        LuceneFieldReading reading = await cache.GetAsync(
            target.Name,
            index,
            (reader, token) =>
            {
                var names = new HashSet<string>(
                    Lucene
                        .Net.Index.MultiFields.GetMergedFieldInfos(reader)
                        .Select(info => info.Name),
                    StringComparer.OrdinalIgnoreCase
                );
                return LuceneFieldStatistics.Read(
                    reader,
                    index,
                    name => Identify(name, names, cultures),
                    token
                );
            },
            cancellationToken
        );

        return new FieldStatisticsResult(
            target,
            true,
            null,
            reading.DocumentCount,
            reading.DeletedDocumentCount,
            reading.CoverageExact,
            DocumentsPerCulture: false,
            ExploreCulture: null,
            reading.Fields
        );
    }

    /// <summary>Names a core Examine field: itself, with its group and Umbraco's label for it.</summary>
    /// <param name="name">The field name.</param>
    /// <param name="names">Every field name of the index.</param>
    /// <param name="cultures">The site's cultures.</param>
    /// <returns>The identity.</returns>
    internal static LuceneFieldIdentity Identify(
        string name,
        IReadOnlySet<string> names,
        IEnumerable<string> cultures
    )
    {
        (FieldGroup group, string? culture) = FieldGroups.Classify(name, names, cultures);
        return new LuceneFieldIdentity(name, Labels.GetValueOrDefault(name), group, culture);
    }
}
