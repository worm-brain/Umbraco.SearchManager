using Examine.Lucene.Indexing;
using Examine.Lucene.Providers;
using Lucene.Net.Analysis;

namespace Umbraco.Community.SearchManager.Features.Diagnose;

/// <summary>
/// The analyzer an Examine index runs over a field's values when it writes them, resolved from the
/// field's value type as <see cref="LuceneIndex"/> resolves it. Shared by the Diagnose tools.
/// </summary>
internal static class ExamineAnalyzers
{
    /// <summary>What the view shows for numbers and dates, which are written as numeric terms.</summary>
    public const string Numeric = "None (numeric terms)";

    /// <summary>What the view shows for raw fields, which are written as one unchanged term.</summary>
    public const string WholeValue = "None (whole value)";

    /// <summary>The analyzer a field's values go through.</summary>
    /// <param name="valueType">The field's value type, or null when unknown.</param>
    /// <param name="index">The index, whose default analyzer applies when the type names none.</param>
    /// <returns>The analyzer, or null for numeric fields and an unknown type without an index.</returns>
    public static Analyzer? AnalyzerOf(IIndexFieldValueType? valueType, LuceneIndex? index) =>
        valueType switch
        {
            // Numbers and dates are written as trie terms; no analyzer touches them.
            IIndexRangeValueType => null,
            { Analyzer: { } analyzer } => analyzer,
            _ => index?.DefaultAnalyzer,
        };

    /// <summary>The name of the analyzer a field's values go through, for the view.</summary>
    /// <param name="valueType">The field's value type, or null when unknown.</param>
    /// <param name="index">The index, whose default analyzer applies when the type names none.</param>
    /// <returns>
    /// The analyzer's type name (for example <c>StandardAnalyzer</c>), <see cref="Numeric"/>,
    /// <see cref="WholeValue"/> for raw fields, or null when unknown.
    /// </returns>
    public static string? NameOf(IIndexFieldValueType? valueType, LuceneIndex? index) =>
        valueType switch
        {
            IIndexRangeValueType => Numeric,
            RawStringType => WholeValue,
            _ => AnalyzerOf(valueType, index)?.GetType().Name,
        };
}
