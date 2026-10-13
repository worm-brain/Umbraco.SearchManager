# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow the Umbraco major
(17.x for Umbraco 17, 18.x for Umbraco 18).

## [Unreleased]

### Added

- The rebuild history records core Examine rebuilds started outside Search Manager: Umbraco's
  rebuild of empty indexes after the site starts as **At startup**, and rebuilds from the core
  Examine dashboard, a language change or another package as **Started outside Search Manager**,
  with document counts and the error of a failure.

## [18.0.0] - 2026-10-12

The first release for Umbraco 18, with the same features as 17.0.0.

## [17.0.0] - 2026-10-12

The first release, for Umbraco 17.

### Added

- Settings > Advanced > Search Manager: a workspace with Indexes and Explore views.
- The Indexes view lists every Examine index with its health, documents, deleted documents,
  fields and size on disk. A corrupt index is listed with its error instead of breaking the page,
  and member indexes need Members section access.
- The Explore view searches any Examine index or registered searcher with filter chips: words,
  `field:value`, `-field:value`, `field:>n` and `"phrases"` become chips you can edit (operator by
  field type, value, exclude); numbers and dates match as ranges; a culture picker searches
  culture variants. Results show score, id, name (with a link to the editor), document type,
  cultures and updated, plus any field through the column picker, with paging; a drawer shows
  every stored field with filter for and filter out. The whole view is in the URL.
- Explore suggests fields (with type and coverage) as you type, then the field's most common
  values with counts (relative ranges for dates); arrow keys, Enter and Escape work the list.
  A fields panel shows the top values of document type, index type, published and updated, over
  the whole index or over the results; click a value to filter on it, or its minus to exclude
  it. "All fields" lists every field with its type and coverage and adds a filter on it.
- Explore shows the generated query: the Lucene sent, the query as Lucene parsed it, and C# with
  Examine (fluent, or NativeQuery when a filter has no fluent form), each with Copy. "Lucene"
  edits the query as raw Lucene, checked as you type with the parser's message and the error
  position; warnings offer one-click fixes for number and date terms without a range, lower-case
  operators, unknown fields and leading wildcards. "Back to filters" turns the query into chips
  again. Native mode is only offered for Lucene indexes.
- `Umbraco.Community.SearchManager.UmbracoSearch`: an adapter package that lists Umbraco Search
  indexes beside the Examine indexes, with Umbraco Search's health and document count, a
  "Zero downtime" badge, and fields, deleted documents and size on disk from the Examine
  provider's active physical index. Their physical `Umb_*` indexes are no longer listed twice.
- An engine filter (All, Examine, Umbraco Search) on the Indexes view when more than one engine
  is installed.
- Bulk rebuilds: tick indexes (or "Rebuild all"), confirm what happens to live search for each,
  and choose one at a time or all at once. Rebuilds run in a queue on the server and keep running
  when the browser leaves; queued runs can be cancelled. The Rebuilds panel shows queued, running
  (with an estimated percentage), succeeded, failed (with the error) and cancelled runs. A core
  Examine run is marked done only once Lucene has committed.
- Rebuild history, kept in the database (a package migration adds the
  `searchManagerRebuildHistory` table): the last 20 runs per index with who started them, when,
  how long they took, the outcome, document counts, the server and the error of a failure. The
  Indexes view gets a "Last rebuilt" column and a details sidebar with the history and the
  index's provider properties. Umbraco Search rebuilds started elsewhere are recorded too.
- Live rebuild status: the Rebuilds panel and the Indexes view update from Umbraco server events
  as runs are queued, start, progress and finish, and only poll while the event connection is
  down. Only users with Settings access receive the events (and Members access as well for
  member indexes).
- The Indexes view shows which server you are on. On a load-balanced site, a banner and a line in
  the rebuild confirmation say that core Examine rebuilds only affect this server, while Umbraco
  Search indexes rebuild on every server.
- Explore on Umbraco Search indexes (with the adapter package): the same chips run through
  Umbraco Search's `SearchAsync` with the culture picked. Words become the query; text, keyword,
  number and date chips become typed filters, excluded chips negate them, and a document type
  chip shows the alias and sends the content type key. Chips Umbraco Search cannot run (phrases,
  starts with, fuzzy, is missing, excluded free text, raw Lucene) are struck through with the
  reason and not sent. Results show name, icon, document type, object type and culture; the
  drawer shows the stored fields when the index uses the Examine provider. Show query shows the
  `SearchAsync` call in C#.
- Explore opens on the index a link names with `?index=`.
- The core Examine Management dashboard (and its field modals) is hidden, so Settings has one
  place for indexes. Set `SearchManager:HideCoreExamineDashboard` to `false` to keep it; see
  `docs/reference/configuration.md`.
- Explore results show the document type's icon.
- A Search Manager box on Umbraco Search's index page (with the adapter package): the last
  rebuild, the runs before it and the field count, with links to Explore and the index's history
  in Search Manager.
- An Umbraco Search rebuild with nothing to index (such as `Umb_Members` on a site without
  members) succeeds and says "nothing to index", rather than failing because Umbraco Search kept
  its empty current copy.
