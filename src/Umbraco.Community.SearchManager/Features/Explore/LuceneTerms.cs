using System.Globalization;
using Examine.Lucene.Indexing;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Util;
using Umbraco.Cms.Infrastructure.Examine;
using Umbraco.Community.SearchManager.Core.Explore;

namespace Umbraco.Community.SearchManager.Features.Explore;

/// <summary>
/// Reads a field's terms straight from a Lucene reader (ADR 0004): the most common values for
/// suggestions, and value counts over a set of documents for the fields panel. Numeric fields
/// are read from their full-precision terms and decoded; every read stops at a limit and says so.
/// </summary>
internal static class LuceneTerms
{
    /// <summary>Most terms read for one suggestion request before the result is marked sampled.</summary>
    public const int MaxSuggestionTerms = 50_000;

    /// <summary>Most terms counted per field in the fields panel before the result is marked sampled.</summary>
    public const int MaxFacetTerms = 5_000;

    /// <summary>
    /// The most common values of a field starting with a prefix, by document frequency (which
    /// includes documents deleted but not yet merged away).
    /// </summary>
    /// <param name="reader">The index reader.</param>
    /// <param name="field">The field.</param>
    /// <param name="valueType">How Examine writes the field, to decode numbers.</param>
    /// <param name="prefix">The prefix, compared case-insensitively; empty for all.</param>
    /// <param name="take">How many values to return.</param>
    /// <param name="limit">Most terms to read.</param>
    /// <returns>The values and whether the read stopped at the limit.</returns>
    public static (IReadOnlyList<TermCount> Terms, bool Sampled) Top(
        IndexReader reader,
        string field,
        IIndexFieldValueType? valueType,
        string prefix,
        int take,
        int limit = MaxSuggestionTerms
    )
    {
        var top = new List<TermCount>();
        bool sampled = Scan(
            reader,
            field,
            valueType,
            limit,
            (value, terms) =>
            {
                if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    top.Add(new TermCount(value, terms.DocFreq));
                }
            }
        );
        return (WithDisplay(reader, field, valueType, Order(top, take), null), sampled);
    }

    /// <summary>Counts a field's values over a set of documents.</summary>
    /// <param name="reader">The index reader the document ids belong to.</param>
    /// <param name="field">The field.</param>
    /// <param name="valueType">How Examine writes the field, to decode numbers.</param>
    /// <param name="documents">The documents to count, by top-level document id.</param>
    /// <param name="take">How many values to return.</param>
    /// <returns>The most common values and whether the field had more terms than were counted.</returns>
    public static (IReadOnlyList<TermCount> Values, bool Sampled) Count(
        IndexReader reader,
        string field,
        IIndexFieldValueType? valueType,
        FixedBitSet documents,
        int take
    )
    {
        IBits? live = MultiFields.GetLiveDocs(reader);
        var counts = new List<TermCount>();
        DocsEnum? docs = null;
        bool sampled = Scan(
            reader,
            field,
            valueType,
            MaxFacetTerms,
            (value, terms) =>
            {
                docs = terms.Docs(live, docs, DocsFlags.NONE);
                long count = 0;
                while (docs.NextDoc() != DocIdSetIterator.NO_MORE_DOCS)
                {
                    if (documents.Get(docs.DocID))
                    {
                        count++;
                    }
                }

                if (count > 0)
                {
                    counts.Add(new TermCount(value, count));
                }
            }
        );
        return (WithDisplay(reader, field, valueType, Order(counts, take), documents), sampled);
    }

    /// <summary>Collects the documents a query matches.</summary>
    /// <param name="searcher">The searcher.</param>
    /// <param name="query">The query.</param>
    /// <returns>The matches, by top-level document id.</returns>
    public static FixedBitSet Matches(IndexSearcher searcher, Query query)
    {
        var collector = new BitSetCollector(searcher.IndexReader.MaxDoc);
        searcher.Search(query, collector);
        return collector.Documents;
    }

    // Walks a field's terms, decoded, calling visit for each until the limit; true when stopped there.
    private static bool Scan(
        IndexReader reader,
        string field,
        IIndexFieldValueType? valueType,
        int limit,
        Action<string, TermsEnum> visit
    )
    {
        Terms? terms = MultiFields.GetTerms(reader, field);
        if (terms is null)
        {
            return false;
        }

        TermsEnum iterator = terms.GetEnumerator();
        int read = 0;
        while (iterator.MoveNext())
        {
            if (read++ >= limit)
            {
                return true;
            }

            // Numeric terms are written at several precisions; the full-precision ones sort
            // first, so the first lower-precision term ends the field's values.
            string? value = Decode(iterator.Term, valueType, out bool lowerPrecision);
            if (lowerPrecision)
            {
                break;
            }

            if (value is not null)
            {
                visit(value, iterator);
            }
        }

        return false;
    }

    /// <summary>Turns a term into the value a chip takes.</summary>
    /// <param name="term">The term bytes.</param>
    /// <param name="valueType">How Examine writes the field.</param>
    /// <param name="lowerPrecision">Whether the term is a numeric field's lower-precision term.</param>
    /// <returns>The value, or null when it cannot be read.</returns>
    private static string? Decode(
        BytesRef term,
        IIndexFieldValueType? valueType,
        out bool lowerPrecision
    )
    {
        lowerPrecision = false;
        switch (valueType)
        {
            case Int32Type:
                lowerPrecision = NumericUtils.GetPrefixCodedInt32Shift(term) != 0;
                return NumericUtils.PrefixCodedToInt32(term).ToString(CultureInfo.InvariantCulture);
            case SingleType:
                lowerPrecision = NumericUtils.GetPrefixCodedInt32Shift(term) != 0;
                return NumericUtils
                    .SortableInt32ToSingle(NumericUtils.PrefixCodedToInt32(term))
                    .ToString("R", CultureInfo.InvariantCulture);
            case Int64Type:
                lowerPrecision = NumericUtils.GetPrefixCodedInt64Shift(term) != 0;
                return NumericUtils.PrefixCodedToInt64(term).ToString(CultureInfo.InvariantCulture);
            case DoubleType:
                lowerPrecision = NumericUtils.GetPrefixCodedInt64Shift(term) != 0;
                return NumericUtils
                    .SortableInt64ToDouble(NumericUtils.PrefixCodedToInt64(term))
                    .ToString("R", CultureInfo.InvariantCulture);
            case DateTimeType:
                lowerPrecision = NumericUtils.GetPrefixCodedInt64Shift(term) != 0;
                long ticks = NumericUtils.PrefixCodedToInt64(term);
                return ticks >= DateTime.MinValue.Ticks && ticks <= DateTime.MaxValue.Ticks
                    ? new DateTime(ticks).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    : null;
            default:
                return term.Utf8ToString();
        }
    }

    /// <summary>
    /// Adds the value as an editor wrote it to terms the index lower-cased (ADR 0021): the
    /// <c>__Raw_</c> copy or the stored field of the first live document with the term, when one
    /// of its values equals the term ignoring case but not exactly (<c>blogPost</c> for
    /// <c>blogpost</c>). Analysed text, whose stored value is a whole sentence, keeps none.
    /// </summary>
    /// <param name="reader">The index reader.</param>
    /// <param name="field">The field.</param>
    /// <param name="valueType">How Examine writes the field; numbers and dates are left alone.</param>
    /// <param name="terms">The terms to return.</param>
    /// <param name="documents">The documents counted, to read a document among them; null for any.</param>
    /// <returns>The terms, with <see cref="TermCount.Display"/> where it differs.</returns>
    private static List<TermCount> WithDisplay(
        IndexReader reader,
        string field,
        IIndexFieldValueType? valueType,
        List<TermCount> terms,
        FixedBitSet? documents
    )
    {
        if (valueType is Int32Type or SingleType or Int64Type or DoubleType or DateTimeType)
        {
            return terms;
        }

        IBits? live = MultiFields.GetLiveDocs(reader);
        string raw = UmbracoExamineFieldNames.RawFieldPrefix + field;
        var stored = new HashSet<string>(StringComparer.Ordinal) { raw, field };
        return
        [
            .. terms.Select(term =>
            {
                DocsEnum? docs = MultiFields.GetTermDocsEnum(
                    reader,
                    live,
                    field,
                    new BytesRef(term.Value)
                );
                int doc;
                while (docs is not null && (doc = docs.NextDoc()) != DocIdSetIterator.NO_MORE_DOCS)
                {
                    if (documents is not null && !documents.Get(doc))
                    {
                        continue;
                    }

                    Lucene.Net.Documents.Document document = reader.Document(doc, stored);
                    string? written = document
                        .GetValues(raw, CultureInfo.InvariantCulture)
                        .Concat(document.GetValues(field, CultureInfo.InvariantCulture))
                        .FirstOrDefault(value =>
                            string.Equals(value, term.Value, StringComparison.OrdinalIgnoreCase)
                        );
                    return written is not null && written != term.Value
                        ? term with
                        {
                            Display = written,
                        }
                        : term;
                }

                return term;
            }),
        ];
    }

    private static List<TermCount> Order(List<TermCount> values, int take) =>
        [
            .. values
                // Dates decode to days, so several terms can share one value.
                .GroupBy(value => value.Value, StringComparer.Ordinal)
                .Select(group => new TermCount(group.Key, group.Sum(value => value.Count)))
                .OrderByDescending(value => value.Count)
                .ThenBy(value => value.Value, StringComparer.Ordinal)
                .Take(take),
        ];

    /// <summary>Sets one bit per matching document, by top-level document id.</summary>
    /// <param name="maxDoc">The reader's document count, deleted ones included.</param>
    private sealed class BitSetCollector(int maxDoc) : ICollector
    {
        private int _docBase;

        /// <summary>The matches.</summary>
        public FixedBitSet Documents { get; } = new(maxDoc);

        /// <inheritdoc />
        public bool AcceptsDocsOutOfOrder => true;

        /// <inheritdoc />
        public void Collect(int doc) => Documents.Set(_docBase + doc);

        /// <inheritdoc />
        public void SetNextReader(AtomicReaderContext context) => _docBase = context.DocBase;

        /// <inheritdoc />
        public void SetScorer(Scorer scorer)
        {
            // Scores are not needed to count.
        }
    }
}
