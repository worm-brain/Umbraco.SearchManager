# ADR 0005: Show query compiles on the server; native Lucene is checked by Examine's own parser

- Status: Accepted
- Date: 2026-10-12
- Issue: Show the generated query and edit it as raw Lucene

## Context

Issue #17 adds the show-query panel and a raw Lucene ("native") mode, after LogExplorer's
ADR 0016. The core dashboard runs raw Lucene silently and answers "Could not parse the query"
without a position.

## Decision

- **`POST /explore/{target}/compile`** returns the Lucene sent, the query as Lucene parsed it
  (`Query.ToString()` of the clause Examine's `NativeQuery` adds, so analysis and the default
  fields of bare words show) and C#. The C# uses the fluent API (`Field`, `GroupedOr`,
  `RangeQuery<int|double|DateTime>`, `And()`, `Not()`, `All()` when the first clause excludes)
  when every chip has a fluent form, otherwise `NativeQuery` with the compiled text. Free text,
  "is missing" and raw Lucene chips have no fluent form. The panel compiles on every change
  (debounced) and lists chips that were not sent with their reason; its open state is `sq=1`.
- **Native mode** is offered only for targets with the `nativeQuery` capability (Lucene
  searchers); otherwise the "Lucene" button is disabled with the reason. It opens with the query
  the last search ran. Searches then send `ExploreQuery.Native`, which the source runs exactly as
  typed. The raw text is in the URL as `native` (empty still means native mode).
- **Validation** (`POST /explore/{target}/validate`) parses with `searcher.CreateQuery().NativeQuery`,
  the same parser a search uses, and returns the parser's message and position. Lucene.NET's
  classic parser reports "line L, column C" with **0-based columns** (its `FastCharStream`),
  verified by tests: the position is the offending token, or the end of the query when it ends
  too soon (an open quote or bracket). A leading wildcard is rejected by Examine's parser with
  no position. The box checks 400 ms after the last keystroke, marks an invalid query and the
  offending character, and never runs it.
- **Warnings** (`NativeQueryLinter`, Core) are a pattern scan with one-click fixes: a number or
  date compared with a plain term (`level:3` matches nothing; fix `level:[3 TO 3]`, or the day's
  range for a date in `yyyy-MM-dd` or ticks, per ADR 0003), lower-case `and`/`or`/`not` (fix to
  capitals), a field the index does not have (an alias is fixed to its field), and a leading
  wildcard (fix: drop it).
- **Back to filters** (`POST /explore/{target}/read-native`, `LuceneToChips` in Core) splits a
  query whose top-level clauses are all `+`/`-` (or a single clause) into chips where each clause
  has a chip form (the shapes the compiler writes), and keeps any other clause as a raw Lucene
  chip. A query with default-OR clauses or `AND`/`OR`/`NOT` becomes one raw Lucene chip, so its
  meaning never changes. Chips that could not run before native mode are kept. Adding a value from
  the fields panel or the drawer in native mode goes back to filters first.

## Consequences

- No code editor component: the box is a single-line `uui-input` (issue non-goal).
- Native mode for Umbraco Search indexes is not offered (issue non-goal); its show-query panel
  will show the `SearchAsync` request from its own source.
