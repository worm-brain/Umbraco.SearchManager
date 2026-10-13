# ADR 0020: Score explanations come from Lucene's Explain, read into nodes in Core

- Status: Accepted
- Date: 2026-10-13
- Issue: Explain a search result's score on demand

## Context

Explore ranks results but gave no way to see why one document outscored another. Lucene can
explain any document's score under a query (`IndexSearcher.Explain`), but as a tree of
descriptions written for people, not an API. Umbraco Search returns no scores at all.

## Verified

Against a real in-memory `LuceneIndex` on the dev Examine (3.10.0, Lucene.NET 4.8.0-beta00018)
for both Umbraco majors; the package's floor resolves beta00017, whose descriptions were not run
but come from the same Lucene 4.8.0 port.

- Examine 3.7.1 and 3.10 use Lucene's `DefaultSimilarity` (classic TF-IDF); there is no custom
  similarity.
- `LuceneSearchQueryBase.Query` is the parsed query; searching and explaining rewrite away the
  single-clause `BooleanQuery` Examine wraps it in.
- `IndexSearcher.Search(query, new QueryWrapperFilter(new TermQuery(__NodeId:id)), n)` returns
  the same score as the unfiltered search, so the document can be found by id without changing
  its score.
- The descriptions (`weight(field:term in N) [DefaultSimilarity], result of:`,
  `tf(freq=F), with freq of:`, `idf(docFreq=D, maxDocs=M)`, `idf(), sum of:` for phrases,
  `fieldNorm(doc=N)`, `queryNorm`, `ConstantScore(q), product of:`, `coord(n/m)`,
  `MatchAllDocsQuery, product of:`, `no match on required clause (...)`,
  `Failure to meet condition(s) of required/prohibited clause(s)`) are pinned by
  `ExamineExploreSourceExplainTests` and the fixtures in `ScoreExplanationReaderTests`.
- Required clauses on keyword, number and date fields add score: a term clause by TF-IDF, a range
  as a constant (`queryNorm`). Prohibited clauses never appear in a matching document's
  explanation.
- A search with filters only is sorted newest first and returns no score.

## Decision

- `POST /explore/{target}/explain` takes the chips, culture or raw query the search sent and one
  or two ids, behind a new `explainScore` capability (a Lucene searcher). The source compiles and
  parses the query exactly as the search does (one shared path), finds each document by
  `__NodeId` while it still matches, and explains it. A multi-index searcher can hold an id
  twice; the highest-scoring copy is explained, the one the search would rank first.
- The Lucene `Explanation` is copied into an engine-neutral `RawExplanation`, and
  `ScoreExplanationReader` in Core reads it into `ExplainNode`s: word and phrase matches with
  their TF-IDF factors, constants (prefix, wildcard), coord, match-all, no-match, and `Other` for
  anything it does not recognise, which keeps Lucene's own words and never fails.
- A required clause whose matches are all on non-text fields, or a range, becomes a `Filter`
  pointing at the first include chip that ran on that field (aliases and culture variants
  resolved). It is labelled "must match" with its real contribution, because it does add score;
  the compiler is unchanged.
- Excluded chips ("must not match", adds no score) are listed from the chips, since Lucene does
  not report them; none in native mode.
- Found but no longer matching (the index changed since the search) and not in the index are
  told apart. Umbraco Search refuses with "does not return scores".
- The client turns nodes into sentences and compares two explanations field by field.

## Consequences

- A Lucene upgrade that rewords descriptions degrades to `Other` rows, caught by the pinned
  tests rather than by users.
- A custom similarity on a site's index explains under its own descriptions, read as `Other`.
- Explaining costs two searches per id, on request only.
