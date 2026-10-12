# ADR 0010: The Umbraco Search adapter reads the Examine provider's active index

- Status: Accepted
- Date: 2026-10-12
- Issue: List Umbraco Search indexes beside the Examine indexes

## Context

The adapter package (`Umbraco.Community.SearchManager.UmbracoSearch`) lists Umbraco Search's
content index registrations. The issue asked two questions:

- What differs in Umbraco Search's public API between 17.2 and 18.2?
- Should the physical `Umb_*` indexes be hidden from the core Examine source always, or only with
  the adapter installed?

The issue also wants fields, deleted documents and size on disk from the physical Lucene index
"via `IActiveIndexManager`", which lives in `Umbraco.Cms.Search.Provider.Examine`, not in
`Umbraco.Cms.Search.Core`.

## Findings (verified 2026-10-12 against the v17.2.0 and v18.2.0 tags and the restored packages)

- `Umbraco.Cms.Search.Core` and `Umbraco.Cms.Search.Provider.Examine` differ between 17.2.0 and
  18.2.0 only in how they register their own OpenAPI documents (Swashbuckle vs
  `AddBackOfficeOpenApiDocument`), a `HasFlag` fix in `RebuildIndexesNotificationHandler`, a
  migration's `RebuildCache = false` and a poll interval in `IndexCommitMonitor`. Every type the
  adapter uses is identical: `IndexOptions.GetContentIndexRegistrations()`,
  `ContentIndexRegistration`, `IIndexerResolver`, `IIndexer.GetMetadataAsync` / `IndexMetadata` /
  `HealthStatus`, `IDistributedContentIndexRebuilder`, `IndexRebuildStartingNotification` /
  `IndexRebuildCompletedNotification`, `IPublishedContentChangeStrategy` /
  `IDraftContentChangeStrategy`, `IExamineIndexer`, `IActiveIndexManager` and
  `ExamineSearchProviderSettings`. The adapter therefore needs no `*.V17.cs` / `*.V18.cs` pairs.
- `Umbraco.Cms.Search.Core` does not depend on Examine; `Umbraco.Cms.Search.Provider.Examine`
  depends on Examine 4.0.0. Examine 4 keeps the Examine 3 members the main package uses
  (`LuceneIndex`, `GetLuceneDirectory`, `BaseLuceneSearcher.GetSearchContext`), and the main
  package runs with it on both Umbraco Search sample sites.
- `IActiveIndexManager` is registered only by `AddExamineSearchProvider()`: `ActiveIndexManager`
  with zero-downtime indexing (physical `{alias}_a` / `{alias}_b`), `NoopActiveIndexManager`
  without it (physical index named as the alias).

## Decision

- The adapter references `Umbraco.Cms.Search.Core` and `Umbraco.Cms.Search.Provider.Examine`, both
  with the `[17.2.0, 18.0.0)` / `[18.2.0, 19.0.0)` range. `IIndexerResolver` and
  `IActiveIndexManager` are optional constructor dependencies: a site without `AddSearchCore()`
  lists nothing, and a site on another provider gets Umbraco Search's metadata only.
- A registration is Examine-backed when its indexer type implements `IExamineIndexer`. For those,
  the adapter reads the active physical index (via `IActiveIndexManager`) for fields, deleted
  documents, size on disk and provider properties, and claims the alias, `{alias}_a` and
  `{alias}_b`, so the core Examine source leaves them out.
- The physical indexes are hidden only when the adapter is installed (the issue's proposal):
  without it the main package knows nothing of Umbraco Search and lists them as plain Examine
  indexes, as the core dashboard does.
- "Zero downtime" is shown when the registration is Examine-backed and
  `Umbraco:CMS:Search:Examine:ZeroDowntimeIndexing` is on.
- Capabilities: rebuild when the indexer resolves; search and native query off until the Explore
  slice for Umbraco Search; term statistics when the physical index is a Lucene index.

## Consequences

- Installing the adapter brings the Examine provider package (and Examine 4) to a site that uses
  Umbraco Search with another provider. Such sites are expected to be rare; if one appears, the
  physical-index reading can move to a second adapter package.
- Brief note for the lead: section 6's "Verify the Umbraco Search 17.2 vs 18.2 public API diff" is
  answered above (no difference in the types the adapter touches), and section 2's floor line
  should read "Adapter compiles against `Umbraco.Cms.Search.Core` and
  `Umbraco.Cms.Search.Provider.Examine` 17.2.0 / 18.2.0".
