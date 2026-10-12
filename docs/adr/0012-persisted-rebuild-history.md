# ADR 0012: Rebuild history in a package-migrated table, read through Umbraco's scopes

- Status: Accepted
- Date: 2026-10-12
- Issue: Keep a persisted rebuild history per index

## Context

Jack decided the rebuild history is persisted in a database table (brief section 2), last 20
runs per index. The issue asked (not blocking) which hooks catch rebuilds started outside Search
Manager: at startup, from the core dashboard, or from Umbraco Search's own UI.

## Findings (verified 2026-10-12 against Umbraco 17.0.0 / 18.0.0 and Umbraco Search 17.2.0)

- `PackageMigrationPlan` (Umbraco.Cms.Core.Packaging) and `AsyncMigrationBase` exist with the same
  shape on 17 and 18 (`MigrationBase` is obsolete on 17 and gone on 18). On 17 a package plan has
  `IgnoreCurrentState => true`, so its steps rerun from the start and must be idempotent. Plans are
  discovered by type and run unattended at startup by default.
- Core Examine raises no notification when an index is rebuilt. `ExamineIndexRebuilder` runs
  rebuilds (startup's empty-index rebuild, the core dashboard's `POST /indexer/{name}/rebuild`)
  as long-running operations with no event. The only way in is decorating `IIndexRebuilder` or
  `IIndexingRebuilderService`, which would replace a core registration.
- Umbraco Search publishes `IndexRebuildStartingNotification` and
  `IndexRebuildCompletedNotification` around every rebuild (`ContentIndexingService`), whatever
  started it: its own UI, a content type change, its startup rebuild of empty indexes, or another
  server's share of a distributed rebuild. A failure is logged, never announced.

## Decision

- Table `searchManagerRebuildHistory` (DTO `RebuildHistoryDto`), created by
  `SearchManagerMigrationPlan` / `AddRebuildHistoryTable` with Umbraco's `Create.Table<T>()`, so
  the SQL comes from the site's own syntax provider (SQL Server or SQLite). Columns: index,
  engine, server (`Environment.MachineName`), trigger (`User` or `Other`), user name, start, end,
  outcome, documents before and after, error, member flag. Times stored as UTC.
- `RebuildHistoryRepository` (`IRebuildHistory` in Core) uses `IScopeProvider` and NPoco SQL built
  from the DTO; it prunes an index to its newest 20 rows on every insert, and computes "latest per
  index" in memory (at most 20 rows per index) instead of SQL that differs per database.
- Search Manager runs are recorded by `RebuildRunner` when they finish; a history write that fails
  is logged and never changes the run's outcome.
- Umbraco Search rebuilds that no Search Manager run is watching are recorded by the adapter as
  `Other`, always as succeeded, without document counts.
- Core Examine rebuilds started outside Search Manager are not recorded: there is no hook, and
  decorating a core service to get one is out of scope. The trigger value `Startup` is therefore
  not offered yet.
- `GET /rebuilds/history?indexName=` (last 20, newest first) and `GET /rebuilds/history/latest`
  serve the details sidebar and the "Last rebuilt" column; member index entries need Members
  section access.
- Tests run the real migration and repository against a temporary SQLite file built the way
  Umbraco's database factory builds one. SQL Server is not covered by an automated test.

## Consequences

- The brief's trigger list ("user name, startup, other") is met for user and other only; a core
  startup or dashboard rebuild leaves no entry. A follow-up can decorate `IIndexRebuilder` if that
  gap matters.
- Removing the package leaves the table in place (Umbraco has no package uninstall migrations).
