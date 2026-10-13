using Examine;
using Examine.Lucene.Indexing;
using Examine.Lucene.Providers;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Community.SearchManager.Core.Diagnose;
using Umbraco.Community.SearchManager.Core.Explore;
using Umbraco.Community.SearchManager.Features.Explore;
using Umbraco.Community.SearchManager.Features.Indexes;

namespace Umbraco.Community.SearchManager.Features.Diagnose.Analysis;

/// <summary>
/// The analyzer view for core Examine indexes (ADR 0023). A search term is judged with the query
/// Examine's free-text search builds for the field (<see cref="IIndexFieldValueType.GetQuery"/>),
/// which for full text analyses terms of 4 or more characters and looks for shorter ones exactly
/// as typed.
/// </summary>
/// <param name="examineManager">Examine's indexes.</param>
/// <param name="languageService">The site's cultures, to spot culture variants.</param>
internal sealed class ExamineAnalyzerSource(
    IExamineManager examineManager,
    ILanguageService languageService
) : IAnalyzerSource
{
    /// <summary>The field picked first: the node name every content index has.</summary>
    private const string DefaultField = "nodeName";

    /// <summary>The shortest term Examine's full-text query analyses (see <see cref="FullTextType.GenerateQuery"/>).</summary>
    private const int ShortestAnalysedTerm = 4;

    private readonly ExamineTargets _targets = new(examineManager);

    /// <summary>Labels of Umbraco's own fields, from the search box's aliases.</summary>
    private static readonly Dictionary<string, string> Labels = ExamineFields
        .Aliases.GroupBy(alias => alias.Field, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(group => group.Key, group => group.First().Label, StringComparer.Ordinal);

    /// <inheritdoc />
    public string Engine => ExamineIndexSource.EngineId;

    /// <inheritdoc />
    public async Task<AnalyzerFieldsResult> GetFieldsAsync(
        ExploreTarget target,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(target);
        if (Refusal(target) is { } reason)
        {
            return AnalyzerFieldsResult.Unavailable(target, reason);
        }

        var index = (LuceneIndex)_targets.IndexOf(target)!;
        IEnumerable<ILanguage> languages = await languageService.GetAllAsync();
        string[] cultures = [.. languages.Select(language => language.IsoCode)];
        HashSet<string> names = FieldNames(index);

        AnalyzerField[] fields =
        [
            .. names
                .Select(name => (Name: name, ValueType: ExamineFields.ValueTypeOf(name, [index])))
                .Where(field => field.ValueType is not IIndexRangeValueType)
                .Select(field =>
                {
                    (FieldGroup group, _) = FieldGroups.Classify(field.Name, names, cultures);
                    return new AnalyzerField(
                        field.Name,
                        Labels.GetValueOrDefault(field.Name),
                        ExamineFields.TypeOf(field.ValueType),
                        group,
                        LuceneTextAnalysis.AnalyzerName(index, field.ValueType),
                        null
                    );
                })
                .OrderBy(field => GroupOrder(field.Group))
                .ThenBy(field => field.Name, StringComparer.OrdinalIgnoreCase),
        ];

        return new AnalyzerFieldsResult(
            target,
            true,
            null,
            fields.Any(field => field.Name == DefaultField)
                ? DefaultField
                : fields.FirstOrDefault()?.Name,
            fields
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
        if (Refusal(target) is { } reason)
        {
            return Task.FromResult(AnalysisResult.Unavailable(target, request.Field, reason));
        }

        var index = (LuceneIndex)_targets.IndexOf(target)!;
        if (!FieldNames(index).Contains(request.Field))
        {
            return Task.FromResult(
                AnalysisResult.Unavailable(
                    target,
                    request.Field,
                    $"{target.Name} has no field called {request.Field}."
                )
            );
        }

        IIndexFieldValueType? valueType = ExamineFields.ValueTypeOf(request.Field, [index]);
        if (valueType is IIndexRangeValueType)
        {
            return Task.FromResult(
                AnalysisResult.Unavailable(
                    target,
                    request.Field,
                    NumericReason(request.Field, valueType)
                )
            );
        }

        return Task.FromResult(
            LuceneTextAnalysis.Analyze(
                target,
                index,
                request.Field,
                request.Field,
                valueType,
                request.Text,
                request.SearchTerm,
                term => new SearchTermQuery(
                    valueType?.GetQuery(term),
                    valueType is FullTextType && term.Length < ShortestAnalysedTerm
                        ?
                        [
                            $"Examine does not analyse searches shorter than {ShortestAnalysedTerm} characters, so it looks for the term exactly as typed.",
                        ]
                        : []
                )
            )
        );
    }

    /// <summary>Why a target's analyzers cannot be run, or null when they can.</summary>
    /// <param name="target">The target.</param>
    /// <returns>The reason for a searcher or a non-Lucene index; otherwise null.</returns>
    private string? Refusal(ExploreTarget target)
    {
        if (target.Kind == ExploreTargetKind.Searcher)
        {
            return $"{target.Name} is a searcher, and analyzers belong to indexes. Pick one of the indexes it searches.";
        }

        return _targets.IndexOf(target) is LuceneIndex { Searcher: BaseLuceneSearcher }
            ? null
            : $"{target.Name} is not a Lucene index, so its analyzer cannot be run here.";
    }

    /// <summary>Every field the index has written or defines, without Examine's sort copies.</summary>
    /// <param name="index">The index.</param>
    /// <returns>The names, compared as Lucene compares them (ordinal).</returns>
    private static HashSet<string> FieldNames(LuceneIndex index)
    {
        var names = new HashSet<string>(
            ExamineFields.Read(index.Searcher, [index]).Fields.Select(field => field.Name),
            StringComparer.Ordinal
        );
        names.UnionWith(
            index
                .FieldValueTypeCollection.ValueTypes.Select(valueType => valueType.FieldName)
                .Where(name => !name.StartsWith("__Sort_", StringComparison.Ordinal))
        );
        return names;
    }

    /// <summary>Why a number or date field is not analysed.</summary>
    private static string NumericReason(string field, IIndexFieldValueType valueType) =>
        $"{field} holds {(valueType is DateTimeType ? "dates" : "numbers")}, which are written as numeric terms and not analysed.";

    /// <summary>The picker's order: fields, then system fields, culture variants and raw copies.</summary>
    private static int GroupOrder(FieldGroup group) =>
        group switch
        {
            FieldGroup.Field => 0,
            FieldGroup.System => 1,
            FieldGroup.CultureVariant => 2,
            FieldGroup.RawCopy => 3,
            _ => 4,
        };
}
