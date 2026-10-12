# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow the Umbraco major
(17.x for Umbraco 17, 18.x for Umbraco 18).

## [Unreleased]

### Added

- Settings > Advanced > Search Manager: a workspace with Indexes, Explore and Diagnose views.
- The Indexes view lists every Examine index with its health, documents, deleted documents,
  fields and size on disk. A corrupt index is listed with its error instead of breaking the page,
  and member indexes need Members section access.
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
- A Search Manager box on Umbraco Search's index page (with the adapter package): the last
  rebuild, the runs before it and the field count, with links to Explore, field statistics and
  the index's history in Search Manager.
