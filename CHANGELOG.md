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
- The Explore view searches any Examine index or registered searcher with filter chips: words,
  `field:value`, `-field:value`, `field:>n` and `"phrases"` become chips you can edit (operator by
  field type, value, exclude); numbers and dates match as ranges; a culture picker searches
  culture variants. Results show score, id, name (with a link to the editor), document type,
  cultures and updated, plus any field through the column picker, with paging; a drawer shows
  every stored field with filter for and filter out. The whole view is in the URL.
