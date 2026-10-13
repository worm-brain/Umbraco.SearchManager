# ADR 0018: Field statistics are read per index from its Lucene reader and cached per reader version

- Status: Accepted
- Date: 2026-10-13
- Issue: Show field statistics for each index

## Context

The Diagnose view's first tool answers "which fields does this index have, and how are they
filled?": per field its type, analyzer, coverage, distinct terms, top terms and the documents
missing it. ADR 0004 already reads terms from a searcher's Lucene reader for Explore, with caps.
Field statistics walk every term of every field, which costs far more than one suggestion, and
both engines (core Examine and Umbraco Search's Examine provider) must be covered.

## Verified

- On Examine 3.10 (dev) and 4.0 (adapter tests), `MultiFields.GetMergedFieldInfos`,
  `MultiFields.GetTerms`, `Terms.DocCount`, `TermsEnum.DocFreq`, `TermsEnum.Docs` and
  `MultiFields.GetLiveDocs` work through `BaseLuceneSearcher.GetSearchContext()`, as in ADR 0004.
- `Terms.DocCount` and `DocFreq` count documents deleted but not yet merged away. With deletions,
  walking the postings over the live documents gives the count an Explore "is missing" chip
  finds (tested: delete one of two documents with a field, coverage drops to one).
- The searcher's reader is a `DirectoryReader`. Its `Version` changes after a save and after
  `CreateIndex()` (a rebuild clears the index first) once the near-real-time reader reopens,
  which `LuceneIndex.WaitForChanges()` waits for (tested).
- `IIndexFieldValueType.Analyzer` is the analyzer each field is written with: `FullTextType`
  takes the index's default analyzer, `RawStringType` a `KeywordAnalyzer`, the generic-analyzer
  types their own; numeric and date types have none. The same member exists on Examine 4.
- Umbraco Search's Examine provider declares its physical fields as field definitions
  (`keywords` raw, `texts` full text, `integers` int, `decimals` double, `datetimeoffsets`
  datetime); property fields not in its `FieldOptions` are not declared and are written as full
  text. It keeps one document per culture (`Sys_Culture`, `none` for invariant ones).

## Decision

- Each engine has a small `IFieldStatisticsSource` keyed by `ExploreTarget.Engine`; the endpoint
  `GET /diagnose/{target}/fields` reuses Explore's targets, so an unknown index is a 404 and a
  member index needs Members access. An engine or target without statistics answers 200 with
  `available: false` and the reason: a registered searcher (statistics are per index), an index
  that is not a Lucene index, or an Umbraco Search provider other than Examine.
- `LuceneFieldStatistics` reads every field except Examine's `__Sort_` copies: documents and
  missing (over live documents), distinct terms (full-precision terms for numbers and dates),
  the top five terms by document count, and for dates the lowest and highest value instead of
  top terms. A field that is stored but not indexed is reported as stored only. Each field's
  walk stops at 200,000 terms and says it was sampled; when that happens while the index has
  deletions, coverage falls back to Lucene's own count and the result says coverage is an
  estimate.
- Rows are grouped (field, culture variant, raw copy, segment, system) so the view can hide the
  copies. For Umbraco Search each physical field is shown under the name filters take
  (`Field_title_texts` is `title`), segments as their own rows, and the counts are said to be
  of culture documents; links into Explore carry the default culture when the index holds
  variant documents, because Explore without a culture only finds invariant ones there.
- `FieldStatisticsCache` keeps one result per target, keyed by the physical index name, the
  reader's version and `MaxDoc`. A changed index opens a new reader, so a stale result is never
  served. Reads are single-flight: callers asking while a read runs wait for it. The read takes
  its own reference on the reader, so a caller that stops waiting does not close it underneath.
- The per-field analyzer lookup lives in `ExamineAnalyzers`, for the Analyzer tool to reuse.

## Consequences

- The first request on a large index walks every term once; later ones are free until the
  index changes. A busy index that changes constantly is read on most visits.
- Coverage on an index with deletions costs a postings walk per term. Merges remove deletions
  over time, after which the cheap count applies again.
- Explore's `termStatistics` capability for Umbraco Search indexes stays off; only Diagnose
  reads their physical index.
