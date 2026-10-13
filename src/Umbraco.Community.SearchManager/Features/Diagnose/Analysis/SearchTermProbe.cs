using Examine;
using Examine.Lucene.Indexing;
using Examine.Lucene.Providers;
using Examine.Lucene.Search;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Umbraco.Community.SearchManager.Core.Diagnose;

namespace Umbraco.Community.SearchManager.Features.Diagnose.Analysis;

/// <summary>
/// Decides whether a search finds a text by running the engine's own query against a throwaway
/// one-document index written with the live index's analyzers (ADR 0023), so the verdict follows
/// the engine's rules rather than a copy of them. Public for the Umbraco Search adapter.
/// </summary>
public static class SearchTermProbe
{
    /// <summary>Indexes the text as the index would and runs the query over it.</summary>
    /// <param name="index">The live index, whose per-field analyzer writes the document; never disposed here.</param>
    /// <param name="valueType">How the index writes the field, or null to write it as analysed text.</param>
    /// <param name="field">The physical field the text goes into.</param>
    /// <param name="text">The text.</param>
    /// <param name="query">The engine's query for the search term.</param>
    /// <returns>Whether the query finds the document.</returns>
    public static bool Run(
        LuceneIndex index,
        IIndexFieldValueType? valueType,
        string field,
        string text,
        Query query
    )
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(query);
        using var directory = new RAMDirectory();
        // Disposing the writer leaves its analyzer alone; the live index goes on using it.
        using (
            var writer = new IndexWriter(
                directory,
                new IndexWriterConfig(LuceneInfo.CurrentVersion, index.FieldAnalyzer)
            )
        )
        {
            var document = new Document();
            if (valueType is null)
            {
                document.Add(new TextField(field, text, Field.Store.NO));
            }
            else
            {
                valueType.AddValue(document, text);
            }

            writer.AddDocument(document);
            writer.Commit();
        }

        using DirectoryReader reader = DirectoryReader.Open(directory);
        return new IndexSearcher(reader).Search(query, 1).TotalHits > 0;
    }

    /// <summary>
    /// The query's clauses, read from its leaves without rewriting it (a rewrite against a
    /// one-document reader turns prefixes into whatever terms that document has).
    /// </summary>
    /// <param name="query">The query.</param>
    /// <returns>
    /// One group per top-level clause, required when the clause must match (or the query is a
    /// single clause); prohibited clauses and empty phrases are left out.
    /// </returns>
    public static IReadOnlyList<SearchTermPart> Parts(Query query)
    {
        ArgumentNullException.ThrowIfNull(query);
        var parts = new List<SearchTermPart>();
        if (Unwrap(query) is BooleanQuery boolean)
        {
            int group = 0;
            foreach (BooleanClause clause in boolean.Clauses)
            {
                if (clause.Occur == Occur.MUST_NOT)
                {
                    continue;
                }

                int before = parts.Count;
                Leaves(clause.Query, clause.Occur == Occur.MUST, group, parts);
                if (parts.Count > before)
                {
                    group++;
                }
            }
        }
        else
        {
            Leaves(query, true, 0, parts);
        }

        return parts;
    }

    /// <summary>Adds every leaf under a clause to one group.</summary>
    private static void Leaves(Query query, bool required, int group, List<SearchTermPart> parts)
    {
        switch (Unwrap(query))
        {
            case BooleanQuery boolean:
                foreach (BooleanClause clause in boolean.Clauses)
                {
                    if (clause.Occur != Occur.MUST_NOT)
                    {
                        Leaves(clause.Query, required, group, parts);
                    }
                }

                break;
            case TermQuery term:
                parts.Add(new(SearchTermPartKind.Word, term.Term.Text, required, group));
                break;
            case PrefixQuery prefix:
                parts.Add(new(SearchTermPartKind.Prefix, prefix.Prefix.Text, required, group));
                break;
            case FuzzyQuery fuzzy:
                parts.Add(new(SearchTermPartKind.Fuzzy, fuzzy.Term.Text, required, group));
                break;
            case WildcardQuery wildcard:
                string pattern = wildcard.Term.Text;
                // "word*" is a prefix search however it was written.
                bool prefixOnly =
                    pattern.Length > 1
                    && pattern.EndsWith('*')
                    && pattern[..^1].IndexOfAny(['*', '?', '\\']) < 0;
                parts.Add(
                    prefixOnly
                        ? new(SearchTermPartKind.Prefix, pattern[..^1], required, group)
                        : new(SearchTermPartKind.Wildcard, pattern, required, group)
                );
                break;
            case PhraseQuery phrase:
                Term[] terms = phrase.GetTerms();
                if (terms.Length > 0)
                {
                    parts.Add(
                        new(
                            terms.Length == 1 ? SearchTermPartKind.Word : SearchTermPartKind.Phrase,
                            string.Join(' ', terms.Select(term => term.Text)),
                            required,
                            group
                        )
                    );
                }

                break;
            case { } other:
                parts.Add(new(SearchTermPartKind.Other, other.ToString(), required, group));
                break;
        }
    }

    /// <summary>The query an Examine late-bound query stands for.</summary>
    private static Query Unwrap(Query query) =>
        query is LateBoundQuery late ? Unwrap(late.Wrapped) : query;
}
