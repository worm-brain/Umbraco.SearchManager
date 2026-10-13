# Umbraco Search Manager documentation

Umbraco Search Manager is a search index manager for the Umbraco 17 and 18 backoffice and a
drop-in replacement for the Examine Management dashboard. The pages below follow
[Diataxis](https://diataxis.fr/): pick the kind that matches what you are trying to do.

| If you want to... | Read |
| --- | --- |
| Learn Search Manager by using it once, start to finish | [Tutorial](#tutorial) |
| Get one job done with a feature you already know about | [How-to guides](#how-to-guides) |
| Look up a setting, a search operator or an endpoint | [Reference](#reference) |
| Understand how it works and why it was built that way | [Explanation](#explanation) |

New here? Install the package (see the [README](../README.md#install)), then start with the
tutorial.

## Tutorial

- [Find out why a page is missing from search](tutorials/find-a-missing-page.md): find a page in
  every index, read why it is missing, test the search against it, and rebuild the index if it is
  out of date.

## How-to guides

- [Check index health](how-to/check-index-health.md): the Indexes view, health labels, the
  engine filter and the details sidebar.
- [Rebuild indexes](how-to/rebuild-indexes.md): bulk rebuilds, the confirmation, the Rebuilds
  panel, cancelling and the history.
- [Search an index](how-to/search-an-index.md): chips, suggestions, the fields panel, the
  document drawer, columns and cultures.
- [Use raw Lucene](how-to/use-raw-lucene.md): Show query, raw Lucene mode and its fixes.
- [Read field statistics](how-to/read-field-statistics.md): coverage, distinct and top terms and
  the analyzer of every field, with links into Explore.
- [Find where a page is indexed](how-to/find-where-a-page-is-indexed.md): which indexes hold a
  page and why the others do not, and which part of a search misses it.
- [See how an index analyzes text](how-to/see-how-an-index-analyzes-text.md): an index's
  analyzer step by step, side by side with another, and whether a search finds the text.
- [Use it with Umbraco Search](how-to/use-with-umbraco-search.md): the adapter package, what it
  adds and what Explore can send.
- [Keep the core Examine dashboard](how-to/keep-the-core-examine-dashboard.md): show both tools
  under Settings.

## Reference

- [Configuration](reference/configuration.md): every `SearchManager` setting and its default.
- [Search syntax](reference/search-syntax.md): what the search box understands, and the field
  short names.
- [Management API](reference/api.md): the endpoints behind Search Manager, for scripts and tools.

## Explanation

- [How rebuilds work](explanation/how-rebuilds-work.md): the queue, when a run counts as done,
  live search during a rebuild, and load-balanced sites.
- [The security model](explanation/security.md): who can use Search Manager and what they see.

## Decisions

The [architecture decision records](adr/README.md) explain why the project is built the way it is.
