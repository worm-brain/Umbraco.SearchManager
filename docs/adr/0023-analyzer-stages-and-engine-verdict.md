# ADR 0023: Analyzer: real analyzers stage by stage, verdict from the engine's own query

- Status: Accepted
- Date: 2026-10-13
- Issue: Diagnose > Analyzer

## Context

"Why doesn't my search find this page?" is often an analyzer question: `ExternalIndex` keeps
the accent in `café` while `InternalIndex` folds it to `cafe`, stop words vanish, `2026:` loses
its colon. The prototype tokenized in the browser and got it wrong (it split on spaces, while
`CultureInvariantWhitespaceAnalyzer` splits on anything that is not a letter or digit). The view
has to show what the index's own analyzer does, and whether a search finds the result, without
re-implementing either.

## Verified

By decompiling Examine 3.7.1 / 4.0.0, Lucene.NET 4.8.0-beta00017 / beta00018 and the Umbraco
Search Examine provider 17.2.0 / 18.2.0, and by tests on real analyzers and in-memory indexes
for both Umbraco majors.

- `LuceneIndex.FieldAnalyzer` is the per-field analyzer the index writer uses; asking it for a
  token stream for a field dispatches exactly as writing does. `DefaultAnalyzer` and each value
  type's `Analyzer` name it.
- Lucene keeps no public list of an analyzer's stages (`CreateComponents` is protected). The only
  link from a filter to the stream before it is the protected `TokenFilter.m_input`, the same in
  beta00017 and beta00018. Asking the analyzer for its stream again on the same thread hands back
  the same reused components with the new text, so each stage can be read by consuming the chain
  up to it.
- Chains: `StandardAnalyzer` = StandardTokenizer, StandardFilter, LowerCaseFilter, StopFilter (33
  English words, no folding); `CultureInvariantWhitespaceAnalyzer` = LetterOrDigitTokenizer,
  LowerCaseFilter, ASCIIFoldingFilter; `CultureInvariantStandardAnalyzer` adds ASCIIFoldingFilter
  before its StopFilter and keeps its stop list private.
- Core Examine's free-text search asks each field's value type for a query
  (`IIndexFieldValueType.GetQuery`). For full text (`FullTextType.GenerateQuery`): a term shorter
  than 4 characters becomes one unanalysed phrase term, case-sensitive; otherwise an optional
  exact phrase (boost 20) plus, per analysed token, a required `token^10 OR token*` (plain term
  under 3 characters).
- The Umbraco Search provider's free text (`Searcher.CreateAggregatedTextQuery`) splits the
  search on spaces and requires every word as `Field(f, word) OR Field(f, word*)` on the
  `Sys_aggregated_texts*` fields; a `TextFilter` is `GroupedOr([fields], value, value*)` on the
  field's `texts*` fields; a `KeywordFilter` compares whole values on `keywords`. Same in 17.2.0
  and 18.2.0. The provider's field-name constants are internal, so the names are copied.
- An `IndexWriter` disposed over a `RAMDirectory` leaves its analyzer alone, so the live
  `FieldAnalyzer` can write a throwaway document.

## Decision

- **Stages** are read by `TokenStreamStages`: walk `m_input` from the outermost stream to the
  tokenizer, then read each stage in turn. Anything in the chain that is neither a `TokenFilter`
  nor a `Tokenizer`, or any failure, falls back to the analyzer's output as one stage with the
  note "This analyzer's steps cannot be shown". The walk and the probe are synchronous: analyzers
  keep their components per thread. Char filters are not shown (none of Umbraco's or Examine's
  analyzers use one).
- **Marking** is pure Core logic (`AnalysisStages.Mark`): tokens are matched across stages by
  their text offsets. Same span and text is kept, same span and other text is changed, no
  overlap with the previous stage is added, and a previous token no token overlaps is shown as
  removed in the stage that dropped it only. Position increments become positions.
- **The verdict comes from the engine's own query**, never from a copy of its rules: the source
  builds the query the engine would (core: the field's `GetQuery`; Umbraco Search: the provider's
  shapes through Examine's fluent API, read with `LuceneSearchQueryBase.Query`), writes the text
  into a one-document `RAMDirectory` index with the live `FieldAnalyzer` and the field's value
  type, and runs the query. A query with no clauses left after analysis finds nothing, and says so.
- **Highlights** come from the query's leaves read without rewriting (a rewrite against a
  one-document reader expands prefixes into that document's terms): word, prefix (a trailing-`*`
  wildcard counts), wildcard pattern and phrase terms are marked on the final stage. Display only.
  Each top-level clause is a group of alternatives, required or only ranking.
- Umbraco Search: the aggregated field is listed first as "All text (what a search query reads)"
  and judged with the free-text shape; text fields with the text-filter shape; keyword fields are
  analysed but not judged, because keyword filters compare whole values.
- Not offered: searchers (several indexes), non-Lucene indexes, number and date fields (numeric
  terms, no analyzer), other Umbraco Search providers. Each says why.
- Limits: text up to 2,000 characters and search terms up to 200 (400 otherwise); at most 500
  tokens per stage, flagged as truncated.
- Endpoints: `GET /diagnose/{target}/analyzer/fields` and `POST /diagnose/{target}/analyzer`,
  behind Explore's target lookup (404 unknown, 403 member data without Members access).

## Consequences

- A Lucene.NET upgrade that renames `m_input` degrades to output-only; `TokenStreamStagesTests`
  pins the name so it fails in CI first.
- Custom analyzers built from `AnalyzerWrapper.WrapComponents` or caching filters may show output
  only. Custom char filters are invisible.
- The Umbraco Search query shapes are copied from the provider's code and could drift; the probe
  still runs Examine's own parser, so a drift changes the shape, not how it is evaluated.
- A core Examine field that the index has not written since the site started is given the value
  type Examine would register for it (as Explore does, ADR 0003).
- Explore's "contains" chip (QueryParser semantics) is not judged here; the view answers what a
  site's free-text search does.
