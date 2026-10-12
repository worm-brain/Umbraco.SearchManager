# ADR 0015: The adapter registers the Umbraco Search box server side; links carry `?index=`

- Status: Accepted
- Date: 2026-10-12
- Issue: Add a Search Manager box to Umbraco Search index pages

## Context

Umbraco Search's index page renders every `searchIndexDetailBox` extension in two columns
(`meta.column`: `left` or anything else for right). The box must exist only where Umbraco Search
is installed, link into Search Manager for the same index, and the adapter package has no client
of its own.

## Findings (verified 2026-10-12 against Umbraco Search v17.2.0 / v18.2.0 and Umbraco 17.0 / 18.0)

- `searchIndexDetailBox` (`ManifestElement` with `meta: { label, column }`) and the index
  workspace context (`UMB_SEARCH_WORKSPACE_CONTEXT`, entity type `search-index`, the alias as the
  workspace's `unique`) are the same in 17.2 and 18.2.
- `Umbraco.Cms.Infrastructure.Manifest.IPackageManifestReader` (`ReadPackageManifestsAsync`) and
  `PackageManifest` have the same shape on 17.0 and 18.0; `PackageManifestService` reads every
  registered reader.
- `UMB_WORKSPACE_VIEW_PATH_PATTERN` exists from 17.0.

## Decision

- The main package's client builds a second module, `umbraco-search-index-box.js`, holding the
  box element. It is not in the main bundle's manifests.
- The adapter registers the box through an `IPackageManifestReader` (type
  `searchIndexDetailBox`, column right). Without the adapter, and so without Umbraco Search,
  nothing is registered.
- The box finds its index through the generic workspace context (entity type `search-index`,
  `unique`), rather than importing `@umbraco-cms/search/settings`, so the main client needs no
  Umbraco Search types.
- Links open Search Manager views with `?index=<name>`. The Indexes view opens that index's
  details sidebar. Explore and Diagnose should select that index when they read the parameter
  (lane B / later slices); until then the links open those views unfocused.

## Consequences

- Removing Umbraco Search but keeping the adapter is not supported anyway (the adapter depends on
  it).
- The `?index=` convention is shared by every view that can focus one index.
