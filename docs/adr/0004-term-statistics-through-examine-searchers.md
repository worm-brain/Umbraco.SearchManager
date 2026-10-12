# ADR 0004: Term statistics come from the searcher's Lucene reader, counted with caps

- Status: Accepted
- Date: 2026-10-12
- Issue: Suggest fields and values, and show top values in a fields panel

## Context

Issue #16 asked to verify, before building, that term enumeration works through Examine's
public API on Examine 3.7.1 (the package's floor) and Examine 4.0 (which Umbraco Search
brings). Suggestions need a field's most common values; the fields panel needs value counts
over the whole index or over the current results; the field list needs coverage.

## Verified

- Examine 3.7.1 and 4.0.0 (source at the `v3.7.1` and `v4.0.0` tags, and 4.0's
  `PublicAPI.Shipped.txt`) both expose `BaseLuceneSearcher.GetSearchContext()`,
  `ISearchContext.GetSearcher()` and `ISearcherReference.IndexSearcher`, which is a Lucene
  `IndexSearcher` with its `IndexReader`. `MultiIndexSearcher` returns a `MultiSearchContext`
  over its searchers, so the same path covers multi-index searchers. Disposing the reference
  releases the reader back to the searcher manager.
- From the reader, Lucene.NET 4.8 gives `MultiFields.GetMergedFieldInfos` (field names),
  `MultiFields.GetTerms(reader, field)` (terms with `DocFreq`, and `DocCount` for coverage) and
  `TermsEnum.Docs` (postings). Numeric fields are written at several precisions; the
  full-precision terms (`NumericUtils.GetPrefixCoded*Shift == 0`) sort first and decode with
  `PrefixCodedToInt32/Int64` (`SortableInt*To*` for floats and doubles).
- `LuceneSearchQueryBase.Query` (public in both) is the parsed `BooleanQuery`, so the query the
  search runs can also be collected into a bit set.
- Covered by tests against a real in-memory `LuceneIndex` on the dev Examine (3.10.0); 4.0 is
  verified from source only, because the main package does not reference it.

## Decision

- `GET /explore/{target}/terms?field&prefix&take` returns a field's most common values by
  document frequency, prefix compared case-insensitively. It reads at most 50,000 terms and says
  `sampled` when it stopped there. Text fields return analysed terms, labelled as terms.
  Document frequency counts documents deleted but not yet merged away.
- `POST /explore/{target}/facets` collects the documents the chips match (all live documents
  when none runs, reported as `wholeIndex`) into a bit set and counts each requested field's
  values over it, reading at most 5,000 terms per field (`sampled` beyond). Date fields count
  the relative ranges `-7d`, `-30d` and `-365d` instead of single days.
- The field list carries each field's `documents` (Lucene's per-field document count) and the
  schema the index's document count, from which the client shows coverage.
- Both endpoints need the `termStatistics` capability (a Lucene searcher) and answer 400
  otherwise; the panel and value suggestions are then not shown.

## Consequences

- Exact facet counts through Examine 4's facet API stay out of scope (issue non-goal).
- The Umbraco Search adapter can offer the same endpoints for its Examine provider's physical
  index, or leave `termStatistics` off.
