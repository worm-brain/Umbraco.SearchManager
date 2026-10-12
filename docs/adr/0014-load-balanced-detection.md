# ADR 0014: "Load balanced" means a subscriber role or more than one active server registration

- Status: Accepted
- Date: 2026-10-12
- Issue: Warn that core Examine rebuilds only affect this server

## Context

Core Examine rebuilds run only on the server that receives them (Umbraco-CMS#8060), while Umbraco
Search's distributed rebuilder rebuilds on every server. The Indexes view must say which server
the user is on and, on a load-balanced site, warn about this. The issue asked (not blocking)
whether `IServerRoleAccessor` plus the server registration table is the best source.

## Findings (verified 2026-10-12 against release-17.0.0 and release-18.0.0)

- `IServerRoleAccessor.CurrentServerRole` (`Unknown`, `Single`, `Subscriber`,
  `SchedulingPublisher`) and `IServerRegistrationService.GetActiveServers(bool refresh = false)`
  have the same shape on 17 and 18.
- The default `ElectedServerRoleAccessor` reports `SchedulingPublisher` on a single-server site
  too (it is the only one registered), and `Unknown` until the first registration touch after
  startup. The role alone cannot tell a single server from a load-balanced one.
- A site with explicit roles (`SingleServerRoleAccessor` or a custom accessor) may not keep the
  registration table current.

## Decision

- `GET /server` returns the machine name (`Environment.MachineName`, the name the history
  records), the role, the active registration count (at least 1) and `isLoadBalanced`.
- `isLoadBalanced` is true when the role is `Subscriber` (a publisher exists elsewhere) or more
  than one server registration is active. A registration table that cannot be read counts as one
  server.
- The Indexes view shows "Server {name} ({role})" in its toolbar; when load balanced, a warning
  banner above the table and a line in the rebuild confirmation (only when a core Examine index
  is in the list). History entries already carry the server (ADR 0012).

## Consequences

- A load-balanced site whose scheduling publisher is set explicitly and that does not register
  its servers shows no banner on the publisher. Subscribers still show it.
