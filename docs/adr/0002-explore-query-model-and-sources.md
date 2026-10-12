# ADR 0002: Explore searches through one flat chip model and an explore source per engine

- Status: Accepted
- Date: 2026-10-12
- Issue: Search an Examine index with filter chips

## Context

Explore must search core Examine indexes now and Umbraco Search indexes later (a second
compiler to `Filter[]`), without the browser knowing which engine it talks to (ADR 0001). The
core Examine dashboard also lists searchers registered on their own (`IExamineManager.RegisteredSearchers`,
for example multi-index searchers); Explore must not lose them. LogExplorer's model (a filter
tree, a simple-syntax parser in Core called over the API on Enter, its ADR 0005) is the pattern
to follow.

## Decision

- **Chips are a flat list of `Filter`s** (Core `Explore/Filter.cs`): `Kind` (Text, Field,
  Lucene), `Field`, `Op`, `Value` (a string, as typed) and `Exclude`. No tree: the search box
  only ever builds a list, and one record keeps the OpenAPI schema simple on both majors.
- **Operators follow the field type** (`FieldOperators`): Text (contains, phrase, starts with,
  fuzzy), Keyword (is, starts with), Number (=, >, >=, <, <=), Date (on, on or after, before),
  and "is missing" on every type. The schema endpoint returns the operators the source runs per
  type, so a source with fewer (Umbraco Search) needs no client change.
- **Combining:** filters AND; include filters on the same field with the same matching operator
  (not a range) OR together, so two document types picked from a list find both (as LogExplorer).
- **One parser, in Core** (`SimpleSyntaxParser`), called on Enter through
  `POST /explore/{target}/parse` with the target's schema, so `level:>2` becomes a number chip
  and `type:` resolves its alias. A field the index does not have becomes a chip on the name as
  typed; the client draws it disabled and does not send it, and the server skips it too.
- **Engines plug in behind `IExploreSource`** (Core), one per engine, combined by
  `IExploreSourceRegistry`: targets, schema, cultures, operators, search. The API is
  `GET /explore/targets`, `GET /explore/{target}/fields`, `POST /explore/{target}/parse` and
  `POST /explore/{target}/search` (`ExploreQuery` in, `ExploreResult` out, with the query the
  engine ran and the filters it skipped with reasons). A target is an index or a registered
  searcher; names are unique across sources.
- **Core Examine** (`ExamineExploreSource`) lists every index no other source claims, then the
  registered searchers, and compiles chips to Lucene text run through `NativeQuery`
  (`LuceneQueryCompiler`), so what Explore shows is exactly what runs.
- **Member data:** a target is member data when it is a member index, or a registered searcher
  that reads one (its own searcher, or one of a multi-index searcher's). Such targets are left
  out of the list and every endpoint answers 403 without Members section access. This is
  stricter than the core dashboard, which only compares names.
- **Results** carry Umbraco's columns read from the stored fields (name in the picked culture,
  document type, `__IndexType`, key, cultures, update date) plus every stored field, which feeds
  the column picker and the document drawer without a second request. Without a ranking clause,
  results are sorted newest first by `updateDate` and carry no score.

## Consequences

- The Umbraco Search compiler (`Filter[]` and `query`) is a second `IExploreSource` in the
  adapter; the client contract stays as it is.
- `ExploreResult.Skipped` and the client's own check overlap on purpose: the client knows at
  once (unknown field, operator, raw Lucene on a non-Lucene index), the server also checks
  values (not a number, not a date).
