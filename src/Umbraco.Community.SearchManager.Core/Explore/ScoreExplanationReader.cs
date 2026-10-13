using System.Globalization;
using System.Text.RegularExpressions;

namespace Umbraco.Community.SearchManager.Core.Explore;

/// <summary>
/// Reads a Lucene 4.8 explanation (TF-IDF, <c>DefaultSimilarity</c>) into
/// <see cref="ExplainNode"/>s the Explore view turns into sentences (ADR 0020). It works on the
/// descriptions Lucene prints, which are not an API, so every format is pinned by tests and a
/// description it does not recognise becomes <see cref="ExplainNodeKind.Other"/>, never an error.
/// </summary>
public static partial class ScoreExplanationReader
{
    /// <summary>Reads an explanation.</summary>
    /// <param name="raw">The engine's explanation.</param>
    /// <param name="schema">The target's fields, to tell filters (keyword, number, date) from text.</param>
    /// <param name="filters">The chips the search sent, to point each filter at its chip.</param>
    /// <param name="culture">The culture picked, which moves chips to culture variant fields.</param>
    /// <param name="skipped">Indexes of chips the compiler left out; they cannot be a filter's chip.</param>
    /// <returns>The explanation's root.</returns>
    public static ExplainNode Read(
        RawExplanation raw,
        FieldSchema schema,
        IReadOnlyList<Filter> filters,
        string? culture,
        IReadOnlyCollection<int>? skipped = null
    )
    {
        ArgumentNullException.ThrowIfNull(raw);
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(filters);

        var context = new FilterContext(schema, filters, culture, skipped ?? []);
        ExplainNode root = Convert(raw);

        // The required clauses of the query are the root sum's children; a query of one clause
        // is rewritten to that clause, so the root is then the clause itself.
        return root.Kind == ExplainNodeKind.Sum
            ? root with
            {
                Children = [.. root.Children.Select(context.AsFilter)],
            }
            : context.AsFilter(root);
    }

    /// <summary>
    /// The chips that prohibit documents: excluded chips, and "is missing" (which prohibits any
    /// value; excluding it requires one). They add no score, and Lucene leaves them out of a
    /// matching document's explanation.
    /// </summary>
    /// <param name="filters">The chips the search sent.</param>
    /// <param name="skipped">Indexes of chips the compiler left out.</param>
    /// <returns>The indexes of the prohibiting chips that ran, in order.</returns>
    public static IReadOnlyList<int> MustNotMatch(
        IReadOnlyList<Filter> filters,
        IReadOnlyCollection<int> skipped
    )
    {
        ArgumentNullException.ThrowIfNull(filters);
        ArgumentNullException.ThrowIfNull(skipped);
        return
        [
            .. Enumerable
                .Range(0, filters.Count)
                .Where(index =>
                    !skipped.Contains(index)
                    && filters[index].Exclude != (filters[index].Op == FilterOperator.Missing)
                ),
        ];
    }

    private static ExplainNode Convert(RawExplanation raw)
    {
        string description = raw.Description ?? string.Empty;

        Match weight = WeightPattern().Match(description);
        if (weight.Success)
        {
            (string field, string term, float? boost) = SplitClause(weight.Groups["clause"].Value);
            bool phrase = term.Length > 1 && term[0] == '"' && term[^1] == '"';
            ExplainFactors factors = CollectFactors(raw, new ExplainFactors());
            return Node(
                phrase ? ExplainNodeKind.PhraseMatch : ExplainNodeKind.TermMatch,
                raw,
                field,
                phrase ? term[1..^1] : term,
                boost is null ? factors : factors with { Boost = boost }
            );
        }

        Match constant = ConstantPattern().Match(description);
        if (constant.Success)
        {
            if (!raw.IsMatch)
            {
                return Node(ExplainNodeKind.NoMatch, raw);
            }

            (string field, string term, _) = SplitClause(constant.Groups["clause"].Value);
            return Node(
                ExplainNodeKind.Constant,
                raw,
                field,
                term,
                CollectFactors(raw, new ExplainFactors())
            );
        }

        if (description.StartsWith("MatchAllDocsQuery", StringComparison.Ordinal))
        {
            return Node(
                ExplainNodeKind.MatchAll,
                raw,
                factors: CollectFactors(raw, new ExplainFactors())
            );
        }

        if (!raw.IsMatch || NoMatchPattern().IsMatch(description))
        {
            return Node(ExplainNodeKind.NoMatch, raw, children: Children(raw));
        }

        if (description == "sum of:")
        {
            return Node(ExplainNodeKind.Sum, raw, children: Children(raw));
        }

        // BooleanQuery scales a sum by the share of optional clauses that matched: product of
        // the sum and coord(n/m).
        if (description == "product of:" && raw.Details.Count == 2)
        {
            Match coord = CoordPattern().Match(raw.Details[1].Description ?? string.Empty);
            if (coord.Success)
            {
                return Node(
                    ExplainNodeKind.Coord,
                    raw,
                    factors: new ExplainFactors(
                        Matched: Int(coord.Groups["matched"].Value),
                        Of: Int(coord.Groups["of"].Value)
                    ),
                    children: [Convert(raw.Details[0])]
                );
            }
        }

        return Node(ExplainNodeKind.Other, raw, children: Children(raw));
    }

    // Walks a match's details for the numbers Lucene multiplies; the first of each wins, which
    // is the field weight's for idf (queryWeight repeats it).
    private static ExplainFactors CollectFactors(RawExplanation raw, ExplainFactors factors)
    {
        foreach (RawExplanation detail in raw.Details)
        {
            string description = detail.Description ?? string.Empty;
            Match match;
            if ((match = TfPattern().Match(description)).Success)
            {
                factors = factors with
                {
                    Tf = factors.Tf ?? detail.Value,
                    Freq = factors.Freq ?? Float(match.Groups["freq"].Value),
                };
            }
            else if ((match = IdfPattern().Match(description)).Success)
            {
                factors = factors with
                {
                    Idf = factors.Idf ?? detail.Value,
                    DocFreq = factors.DocFreq ?? Long(match.Groups["docFreq"].Value),
                    MaxDocs = factors.MaxDocs ?? Long(match.Groups["maxDocs"].Value),
                };
                continue;
            }
            else if (description == "idf(), sum of:")
            {
                // A phrase's idf is the sum of its words' idfs.
                factors = factors with
                {
                    Idf = factors.Idf ?? detail.Value,
                };
                continue;
            }
            else if (description.StartsWith("fieldNorm(", StringComparison.Ordinal))
            {
                factors = factors with { FieldNorm = factors.FieldNorm ?? detail.Value };
            }
            else if (description == "queryNorm")
            {
                factors = factors with { QueryNorm = factors.QueryNorm ?? detail.Value };
            }
            else if (description == "boost")
            {
                factors = factors with { Boost = factors.Boost ?? detail.Value };
            }

            factors = CollectFactors(detail, factors);
        }

        return factors;
    }

    // "nodeName:umbraco", "nodeName:\"getting started\"", "nodeName:hoodie^0.6", "level:[2 TO 2]".
    private static (string Field, string Term, float? Boost) SplitClause(string clause)
    {
        float? boost = null;
        Match boosted = BoostPattern().Match(clause);
        if (boosted.Success)
        {
            boost = Float(boosted.Groups["boost"].Value);
            clause = clause[..boosted.Index];
        }

        int colon = clause.IndexOf(':', StringComparison.Ordinal);
        return colon < 0
            ? (string.Empty, clause, boost)
            : (clause[..colon], clause[(colon + 1)..], boost);
    }

    private static IReadOnlyList<ExplainNode> Children(RawExplanation raw) =>
        [.. raw.Details.Select(Convert)];

    private static ExplainNode Node(
        ExplainNodeKind kind,
        RawExplanation raw,
        string? field = null,
        string? term = null,
        ExplainFactors? factors = null,
        IReadOnlyList<ExplainNode>? children = null
    ) =>
        new(
            kind,
            raw.Value,
            field,
            term,
            factors,
            null,
            raw.Description ?? string.Empty,
            children ?? []
        );

    private static float? Float(string value) =>
        float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed)
            ? parsed
            : null;

    private static long? Long(string value) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed)
            ? parsed
            : null;

    private static int? Int(string value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
            ? parsed
            : null;

    [GeneratedRegex(@"^weight\((?<clause>.+) in \d+\) \[[^\]]*\], result of:$")]
    private static partial Regex WeightPattern();

    [GeneratedRegex(@"^ConstantScore\((?<clause>.+)\)(, product of:| doesn't match id \d+)$")]
    private static partial Regex ConstantPattern();

    [GeneratedRegex(@"^coord\((?<matched>\d+)/(?<of>\d+)\)$")]
    private static partial Regex CoordPattern();

    [GeneratedRegex(@"^tf\(freq=(?<freq>[^)]+)\), with freq of:$")]
    private static partial Regex TfPattern();

    [GeneratedRegex(@"^idf\(docFreq=(?<docFreq>\d+), maxDocs=(?<maxDocs>\d+)\)$")]
    private static partial Regex IdfPattern();

    [GeneratedRegex(@"\^(?<boost>[0-9.]+)$")]
    private static partial Regex BoostPattern();

    [GeneratedRegex(
        @"^(no match on required clause|match on prohibited clause|Failure to meet condition|no matching term)"
    )]
    private static partial Regex NoMatchPattern();

    /// <summary>Turns clauses on keyword, number and date fields, and ranges, into filters.</summary>
    private sealed class FilterContext(
        FieldSchema schema,
        IReadOnlyList<Filter> filters,
        string? culture,
        IReadOnlyCollection<int> skipped
    )
    {
        /// <summary>
        /// The clause as a filter when every word, constant or range it matched is on a field
        /// that is not text, or is a range; otherwise the clause unchanged.
        /// </summary>
        public ExplainNode AsFilter(ExplainNode clause)
        {
            ExplainNode[] leaves = [.. Leaves(clause)];
            if (leaves.Length == 0 || !leaves.All(IsFilterLeaf))
            {
                return clause;
            }

            ExplainNode first = leaves[0];
            return clause with
            {
                Kind = ExplainNodeKind.Filter,
                Field = first.Field,
                Term = leaves.Length == 1 ? first.Term : null,
                Factors = leaves.Length == 1 ? first.Factors : null,
                FilterIndex = ChipFor(first.Field!),
                Children = [clause],
            };
        }

        private bool IsFilterLeaf(ExplainNode leaf)
        {
            if (leaf.Kind == ExplainNodeKind.Constant && IsRange(leaf.Term))
            {
                return true;
            }

            IndexField? field = string.IsNullOrEmpty(leaf.Field)
                ? null
                : schema.Resolve(leaf.Field);
            return field is not null && field.Type != FieldType.Text;
        }

        private static bool IsRange(string? term) => term is ['[' or '{', .., ']' or '}'];

        private static IEnumerable<ExplainNode> Leaves(ExplainNode node) =>
            node.Kind
                is ExplainNodeKind.TermMatch
                    or ExplainNodeKind.PhraseMatch
                    or ExplainNodeKind.Constant
                ? [node]
                : node.Children.SelectMany(Leaves);

        // The first include chip that ran and queries this field (after aliases and culture).
        private int? ChipFor(string field)
        {
            for (int index = 0; index < filters.Count; index++)
            {
                Filter filter = filters[index];
                if (
                    filter.Kind != FilterKind.Field
                    || filter.Exclude
                    || filter.Op == FilterOperator.Missing
                    || skipped.Contains(index)
                    || string.IsNullOrEmpty(filter.Field)
                    || schema.Resolve(filter.Field) is not IndexField resolved
                )
                {
                    continue;
                }

                if (
                    string.Equals(
                        schema.ForCulture(resolved, culture).Name,
                        field,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    return index;
                }
            }

            return null;
        }
    }
}
