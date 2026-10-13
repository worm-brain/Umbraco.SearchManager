using Examine.Lucene.Indexing;
using Examine.Lucene.Providers;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Util;
using Umbraco.Community.SearchManager.Core.Diagnose;
using Umbraco.Community.SearchManager.Core.Explore;
using Umbraco.Community.SearchManager.Features.Explore;

namespace Umbraco.Community.SearchManager.Features.Diagnose.Fields;

/// <summary>Who a physical Lucene field is, as the field statistics show it.</summary>
/// <param name="Name">The name a chip takes for the field.</param>
/// <param name="Label">A human name, or null.</param>
/// <param name="Group">What kind of field it is.</param>
/// <param name="Culture">The culture of a culture variant, or null.</param>
public sealed record LuceneFieldIdentity(
    string Name,
    string? Label,
    FieldGroup Group,
    string? Culture
);

/// <summary>Every field's statistics read from one Lucene reader.</summary>
/// <param name="DocumentCount">Live documents.</param>
/// <param name="DeletedDocumentCount">Documents deleted but not yet merged away.</param>
/// <param name="CoverageExact">Whether every field's coverage is exact (see <see cref="FieldStatisticsResult.CoverageExact"/>).</param>
/// <param name="Fields">One row per field, sorted by name.</param>
public sealed record LuceneFieldReading(
    long DocumentCount,
    long DeletedDocumentCount,
    bool CoverageExact,
    IReadOnlyList<FieldStatistic> Fields
);

/// <summary>
/// Reads the statistics of every field of a Lucene index straight from its reader (ADR 0018):
/// coverage, distinct terms, top terms and, for dates, the range. Public so source packages (the
/// Umbraco Search adapter) read their physical indexes the same way.
/// </summary>
/// <remarks>
/// Lucene's per-field document count includes documents deleted but not yet merged away, so when
/// the reader has deletions the postings are walked over live documents instead, which makes the
/// coverage equal to what an Explore "is missing" chip counts.
/// </remarks>
public static class LuceneFieldStatistics
{
    /// <summary>Most terms read per field before its counts are marked sampled.</summary>
    public const int MaxTermsPerField = 200_000;

    /// <summary>How many top terms each field reports.</summary>
    public const int TopTermCount = 5;

    /// <summary>The prefix of Examine's sort copies, which are not fields to search.</summary>
    private const string SortPrefix = "__Sort_";

    /// <summary>Reads every field's statistics.</summary>
    /// <param name="reader">The index's reader.</param>
    /// <param name="index">The index, for each field's value type and analyzer.</param>
    /// <param name="identify">Names a physical field, or returns null to leave it out.</param>
    /// <param name="cancellationToken">Stops the read between terms.</param>
    /// <param name="termLimit">Most terms read per field.</param>
    /// <returns>The statistics.</returns>
    /// <exception cref="OperationCanceledException">The read was cancelled.</exception>
    public static LuceneFieldReading Read(
        IndexReader reader,
        LuceneIndex index,
        Func<string, LuceneFieldIdentity?> identify,
        CancellationToken cancellationToken,
        int termLimit = MaxTermsPerField
    )
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(identify);

        LuceneIndex[] indexes = [index];
        // With deletions, Lucene's counts include deleted documents; the live ones are counted
        // from the postings instead.
        IBits? live = reader.NumDeletedDocs > 0 ? MultiFields.GetLiveDocs(reader) : null;
        bool coverageExact = true;
        var fields = new List<FieldStatistic>();

        foreach (FieldInfo info in MultiFields.GetMergedFieldInfos(reader))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (info.Name.StartsWith(SortPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            LuceneFieldIdentity? identity = identify(info.Name);
            if (identity is null)
            {
                continue;
            }

            IIndexFieldValueType? valueType = ExamineFields.ValueTypeOf(info.Name, indexes);
            FieldType type = ExamineFields.TypeOf(valueType);
            string? physical = string.Equals(identity.Name, info.Name, StringComparison.Ordinal)
                ? null
                : info.Name;
            string? analyzer = ExamineAnalyzers.NameOf(valueType, index);

            if (!info.IsIndexed)
            {
                // Stored for display only: nothing to count and nothing a query can find.
                fields.Add(
                    new FieldStatistic(
                        identity.Name,
                        identity.Label,
                        type,
                        identity.Group,
                        identity.Culture,
                        analyzer,
                        null,
                        null,
                        null,
                        false,
                        [],
                        false,
                        null,
                        true,
                        physical
                    )
                );
                continue;
            }

            FieldTerms terms = ReadTerms(
                reader,
                info.Name,
                valueType,
                type,
                live,
                termLimit,
                cancellationToken
            );
            coverageExact &= terms.CoverageExact;
            long documents = Math.Min(terms.Documents, reader.NumDocs);
            fields.Add(
                new FieldStatistic(
                    identity.Name,
                    identity.Label,
                    type,
                    identity.Group,
                    identity.Culture,
                    analyzer,
                    documents,
                    reader.NumDocs - documents,
                    terms.Unique,
                    terms.Sampled,
                    terms.Top,
                    type == FieldType.Text,
                    terms.Range,
                    false,
                    physical
                )
            );
        }

        return new LuceneFieldReading(
            reader.NumDocs,
            reader.NumDeletedDocs,
            coverageExact,
            [
                .. fields
                    .OrderBy(field => field.Name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(field => field.Physical, StringComparer.Ordinal),
            ]
        );
    }

    /// <summary>Walks one field's terms.</summary>
    /// <param name="reader">The reader.</param>
    /// <param name="field">The physical field.</param>
    /// <param name="valueType">How Examine writes it, to decode numbers.</param>
    /// <param name="type">Its Explore type: dates get a range instead of top terms.</param>
    /// <param name="live">The live documents when the reader has deletions; null when it has none.</param>
    /// <param name="limit">Most terms to read.</param>
    /// <param name="cancellationToken">Stops the walk.</param>
    /// <returns>What was counted.</returns>
    private static FieldTerms ReadTerms(
        IndexReader reader,
        string field,
        IIndexFieldValueType? valueType,
        FieldType type,
        IBits? live,
        int limit,
        CancellationToken cancellationToken
    )
    {
        Terms? terms = MultiFields.GetTerms(reader, field);
        if (terms is null)
        {
            return new FieldTerms(0, 0, false, true, [], null);
        }

        // Most documents first; at equal counts the alphabetically first value wins, so the heap
        // evicts the lowest count and, among equals, the alphabetically last value.
        var top = new PriorityQueue<TermCount, TermCount>(TopTermComparer.Instance);
        FixedBitSet? seen = live is null ? null : new FixedBitSet(reader.MaxDoc);
        DocsEnum? docs = null;
        string? min = null;
        string? max = null;
        long unique = 0;
        bool sampled = false;

        TermsEnum iterator = terms.GetEnumerator();
        while (iterator.MoveNext())
        {
            if (unique >= limit)
            {
                sampled = true;
                break;
            }

            if ((unique & 0x3FF) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            // Numeric terms are written at several precisions; the full-precision ones sort first,
            // in numeric order, so the first lower-precision term ends the field's values.
            string? value = LuceneTerms.Decode(iterator.Term, valueType, out bool lowerPrecision);
            if (lowerPrecision)
            {
                break;
            }

            long count;
            if (seen is null)
            {
                count = iterator.DocFreq;
            }
            else
            {
                count = 0;
                docs = iterator.Docs(live, docs, DocsFlags.NONE);
                while (docs.NextDoc() != DocIdSetIterator.NO_MORE_DOCS)
                {
                    seen.Set(docs.DocID);
                    count++;
                }

                // A term left only in deleted documents is gone as far as searches go.
                if (count == 0)
                {
                    continue;
                }
            }

            unique++;
            if (value is null)
            {
                continue;
            }

            if (type == FieldType.Date)
            {
                min ??= value;
                max = value;
                continue;
            }

            var term = new TermCount(value, count);
            top.Enqueue(term, term);
            if (top.Count > TopTermCount)
            {
                top.Dequeue();
            }
        }

        // Without deletions Lucene's per-field count is exact; with them the live postings were
        // unioned, unless the walk stopped early, when the per-field count is the best estimate.
        long documents =
            seen is not null && !sampled ? seen.Cardinality : Math.Max(terms.DocCount, 0);
        return new FieldTerms(
            documents,
            unique,
            sampled,
            CoverageExact: seen is null || !sampled,
            [.. top.UnorderedItems.Select(item => item.Element).Order(TopTermComparer.Descending)],
            min is null || max is null ? null : new ValueRange(min, max)
        );
    }

    /// <summary>What one field's term walk counted.</summary>
    /// <param name="Documents">Live documents with a term.</param>
    /// <param name="Unique">Distinct live terms read.</param>
    /// <param name="Sampled">Whether the walk stopped at its limit.</param>
    /// <param name="CoverageExact">Whether <paramref name="Documents"/> is exact.</param>
    /// <param name="Top">The most common terms.</param>
    /// <param name="Range">A date field's lowest and highest value.</param>
    private sealed record FieldTerms(
        long Documents,
        long Unique,
        bool Sampled,
        bool CoverageExact,
        IReadOnlyList<TermCount> Top,
        ValueRange? Range
    );

    /// <summary>
    /// Orders terms for the bounded heap: fewer documents first, and at equal counts the
    /// alphabetically last value first, so dequeuing drops the term least worth showing.
    /// </summary>
    private sealed class TopTermComparer : IComparer<TermCount>
    {
        /// <summary>The heap order.</summary>
        public static readonly TopTermComparer Instance = new();

        /// <summary>The display order: most documents first, then alphabetically.</summary>
        public static readonly IComparer<TermCount> Descending = Comparer<TermCount>.Create(
            (left, right) => Instance.Compare(right, left)
        );

        /// <inheritdoc />
        public int Compare(TermCount? x, TermCount? y)
        {
            ArgumentNullException.ThrowIfNull(x);
            ArgumentNullException.ThrowIfNull(y);
            int byCount = x.Count.CompareTo(y.Count);
            return byCount != 0 ? byCount : string.CompareOrdinal(y.Value, x.Value);
        }
    }
}
