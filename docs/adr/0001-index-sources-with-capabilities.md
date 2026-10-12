# ADR 0001: Index sources with capabilities, one per search engine

- Status: Accepted
- Date: 2026-10-12
- Issue: Show every Examine index in a Search Manager workspace

## Context

A site on Umbraco 17 or 18 can run core Examine only, or core Examine and Umbraco Search side by
side; Umbraco Search may become core from Umbraco 19. Some indexes are Lucene indexes (term
statistics, raw Lucene), others are not (ExamineX and other providers).

## Decision

- Core defines `IIndexSource` (one per engine) and `IIndexSourceRegistry`, which combines them.
  Each index is an `IndexSummary` carrying its health, counts (null when unknown), properties and
  `IndexCapabilities` (rebuild, search, native query, term statistics). The UI hides or explains
  what a capability set lacks.
- A source can claim Examine index names (`ClaimedExamineIndexNames`) so another source leaves
  them out; the Umbraco Search adapter claims its physical Lucene indexes.
- The health vocabulary is Umbraco Search's (Healthy, Rebuilding, Corrupted, Empty, Unknown) plus
  core Examine's Unhealthy and the package's Incomplete.
- The core Examine source follows the core dashboard's rules (rebuilding, then corrupt on any
  diagnostics exception, then `IsHealthy`), and adds deleted documents and size on disk for
  Lucene indexes. Umbraco's generic diagnostics' -1 counts become null.
- Member indexes need Members section access, enforced in the package
  (`IMemberIndexAccess`) because `IMemberIndexAuthorizer` only exists from 17.6.2 / 18.1.1.

## Consequences

- New engines are added as packages that register an `IIndexSource`; the main package never
  references them.
- Every view reads capabilities rather than engine names.
