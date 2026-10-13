using Examine.Lucene.Indexing;
using Examine.Lucene.Providers;
using Lucene.Net.Analysis.Core;
using Lucene.Net.Analysis.Util;
using Lucene.Net.Search;
using Umbraco.Community.SearchManager.Core.Diagnose;
using Umbraco.Community.SearchManager.Core.Explore;

namespace Umbraco.Community.SearchManager.Features.Diagnose.Analysis;

/// <summary>The engine's query for a search term, and what to say about it.</summary>
/// <param name="Query">The query, or null when the engine builds none (nothing left to search for).</param>
/// <param name="Notes">Sentences on the engine's rules that explain the verdict, such as short terms going unanalysed.</param>
public sealed record SearchTermQuery(Query? Query, IReadOnlyList<string> Notes);

/// <summary>
/// Runs text through one field of a Lucene index, stage by stage, and judges a search term against
/// it with the engine's own query (ADR 0023). Shared by both engines; public for the adapter.
/// </summary>
public static class LuceneTextAnalysis
{
    /// <summary>Most tokens shown per stage; the rest are cut and the result says so.</summary>
    public const int MaxTokensPerStage = 500;

    /// <summary>What the view says when only the analyzer's output can be read.</summary>
    public const string OutputOnlyNote =
        "This analyzer's steps cannot be shown; this is its output.";

    /// <summary>What the view says when the analysed text leaves the search nothing to look for.</summary>
    public const string NothingLeftNote =
        "No words are left once the analyzer has run over the search, so it finds nothing.";

    /// <summary>Analyses text for one field and judges the search term.</summary>
    /// <param name="target">The index, for the result.</param>
    /// <param name="index">The Lucene index whose analyzers run.</param>
    /// <param name="field">The field as the view names it.</param>
    /// <param name="physicalField">The field's name in the Lucene index, which picks the analyzer.</param>
    /// <param name="valueType">How the index writes the field; null when it has no value type for it.</param>
    /// <param name="text">The text.</param>
    /// <param name="searchTerm">What a visitor searches for; null or blank for no verdict.</param>
    /// <param name="search">
    /// Builds the engine's query for the search term; null when the field's searches are not judged,
    /// in which case <paramref name="searchUnavailableReason"/> says why.
    /// </param>
    /// <param name="searchUnavailableReason">Why searches on this field are not judged.</param>
    /// <returns>The analysis. Runs synchronously: analyzers reuse their components per thread.</returns>
    public static AnalysisResult Analyze(
        ExploreTarget target,
        LuceneIndex index,
        string field,
        string physicalField,
        IIndexFieldValueType? valueType,
        string text,
        string? searchTerm,
        Func<string, SearchTermQuery>? search,
        string? searchUnavailableReason = null
    )
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(text);

        // Analysed through the index's per-field analyzer, exactly as its writer does.
        IReadOnlyList<RawStage>? raw = TokenStreamStages.TryRead(
            index.FieldAnalyzer,
            physicalField,
            text,
            MaxTokensPerStage,
            out bool truncated
        );
        bool stagesShown = raw is not null;
        raw ??=
        [
            TokenStreamStages.ReadOutput(
                index.FieldAnalyzer,
                physicalField,
                text,
                MaxTokensPerStage,
                out truncated
            ),
        ];

        string? stopDetail = StopDetail(ExamineAnalyzers.AnalyzerOf(valueType, index));
        List<AnalysisStage> stages =
        [
            .. AnalysisStages.Mark([
                .. raw.Select(stage =>
                    stage.Name == nameof(StopFilter) ? stage with { Detail = stopDetail } : stage
                ),
            ]),
        ];

        SearchTermResult? verdict = null;
        if (!string.IsNullOrWhiteSpace(searchTerm) && search is not null)
        {
            SearchTermQuery built = search(searchTerm.Trim());
            IReadOnlyList<SearchTermPart> parts = built.Query is null
                ? []
                : SearchTermProbe.Parts(built.Query);
            bool matches =
                built.Query is not null
                && parts.Count > 0
                && SearchTermProbe.Run(index, valueType, physicalField, text, built.Query);
            verdict = new SearchTermResult(
                matches,
                parts,
                parts.Count == 0 ? [.. built.Notes, NothingLeftNote] : built.Notes
            );
            if (stages.Count > 0)
            {
                stages[^1] = SearchTermMatcher.Highlight(stages[^1], parts);
            }
        }

        return new AnalysisResult(
            target,
            true,
            null,
            field,
            AnalyzerName(index, valueType),
            stagesShown,
            stagesShown ? null : OutputOnlyNote,
            stages,
            verdict,
            string.IsNullOrWhiteSpace(searchTerm) || search is not null
                ? null
                : searchUnavailableReason,
            truncated
        );
    }

    /// <summary>
    /// The value type an index has registered for a field, looked up without registering one (as
    /// <c>GetValueType</c> would) for a field the index has not written since the site started.
    /// </summary>
    /// <param name="index">The index.</param>
    /// <param name="physicalField">The field's name in the Lucene index.</param>
    /// <returns>The value type, or null when the index has none for the field.</returns>
    public static IIndexFieldValueType? RegisteredValueTypeOf(
        LuceneIndex index,
        string physicalField
    )
    {
        ArgumentNullException.ThrowIfNull(index);
        return index.FieldValueTypeCollection.ValueTypes.FirstOrDefault(valueType =>
            string.Equals(valueType.FieldName, physicalField, StringComparison.Ordinal)
        );
    }

    /// <summary>The name of the analyzer a field's values go through, for the view.</summary>
    /// <param name="index">The index.</param>
    /// <param name="valueType">The field's value type, or null for the index's default analyzer.</param>
    /// <returns>For example <c>StandardAnalyzer</c>, or "None (whole value)" for raw fields.</returns>
    public static string AnalyzerName(LuceneIndex index, IIndexFieldValueType? valueType) =>
        ExamineAnalyzers.NameOf(valueType, index) ?? "Unknown";

    /// <summary>What a stop filter removes, when the analyzer says.</summary>
    /// <param name="analyzer">The field's analyzer.</param>
    /// <returns>For example "removes 33 common English words"; a general sentence when the list is private.</returns>
    private static string StopDetail(Lucene.Net.Analysis.Analyzer? analyzer) =>
        analyzer is StopwordAnalyzerBase { StopwordSet: { } set }
            // Analyzers copy the list they are given, so Lucene's English list is recognised by content.
            ? set.Count == StopAnalyzer.ENGLISH_STOP_WORDS_SET.Count
            && StopAnalyzer.ENGLISH_STOP_WORDS_SET.All(word => set.Contains(word))
                ? $"removes {set.Count} common English words"
                : $"removes {set.Count} stop words"
            : "removes stop words";
}
