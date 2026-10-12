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
