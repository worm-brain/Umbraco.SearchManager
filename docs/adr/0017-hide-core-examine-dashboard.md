# ADR 0017: Hide the core Examine Management dashboard by default, through a settings endpoint

- Status: Accepted
- Date: 2026-10-12
- Issue: Hide the core Examine Management dashboard by default

## Context

Search Manager covers the core Examine Management dashboard's uses (indexes, rebuilds, search,
fields). The brief (section 2) hides the dashboard by default with
`SearchManager:HideCoreExamineDashboard: false` bringing it back, as LogExplorer hides the core
Log Viewer (its ADR 0007 and 0011). The dashboard is a client-side manifest of the backoffice
bundle, so only the client extension registry can remove it, while the setting lives on the server.

## Findings (verified 2026-10-12 in `@umbraco-cms/backoffice` 17.0.0, 17.7.1, 18.0.0 and 18.2.1)

- The dashboard is `Umb.Dashboard.ExamineManagement` (`packages/core/search/manifests.js`); its
  modals are `Umb.Modal.Examine.FieldsSettings` and `Umb.Modal.Examine.FieldsViewer`
  (`examine-management-dashboard/modal/manifests.js`). Same aliases in all four.

## Decision

- `SearchManagerOptions` binds the `SearchManager` section; `HideCoreExamineDashboard` defaults to
  `true`.
- `GET /umbraco/search-manager/api/v1/settings` returns the client-facing settings
  (`{ hideCoreExamineDashboard }`), with the Settings-section policy of every package endpoint.
  New client-facing options are added to this response.
- The entry point fetches it once the auth context is ready and calls
  `extensionRegistry.exclude(...)` for the dashboard and both modals when the flag is on. Any
  failure (network, 403) keeps the dashboard.
- Umbraco Search's own Search workspace is left alone (issue non-goal).

## Consequences

- Exclusion lives in the registry only, so uninstalling the package, or setting the flag to
  `false`, restores the dashboard with no clean-up.
- The setting is documented in `docs/reference/configuration.md`.
