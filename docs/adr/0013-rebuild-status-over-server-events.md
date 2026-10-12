# ADR 0013: Rebuild status pushed as routed Umbraco server events with our own authorizer

- Status: Accepted
- Date: 2026-10-12
- Issue: Push rebuild status to the browser as it changes

## Context

The Rebuilds panel polled `GET /rebuilds`. Umbraco 17+ has server events over SignalR. The issue
asked to verify the `IEventSourceAuthorizer` shape on 17.0 and 18.0 (blocking), and wants events
limited to users with Settings access (plus Members access for member indexes).

## Findings (verified 2026-10-12 against the release-17.0.0 and release-18.0.0 tags)

- `Umbraco.Cms.Core.ServerEvents.IEventSourceAuthorizer` is identical on 17.0 and 18.0:
  `IEnumerable<string> AuthorizableEventSources { get; }` and
  `Task<bool> AuthorizeAsync(ClaimsPrincipal principal, string eventSource)`. Authorizers are
  added with `builder.EventSourceAuthorizers().Append<T>()`. `IServerEventRouter`
  (`RouteEventAsync`, `NotifyUserAsync`, `BroadcastEventAsync`) and `ServerEvent` (`EventType`,
  `EventSource`, `Key`) are unchanged too.
- When a user connects to `/umbraco/serverEventHub`, Umbraco asks every authorizer for every
  source and adds the connection to a group per authorized source. `RouteEventAsync` sends to that
  group only; `BroadcastEventAsync` sends to every connection.
- A `ServerEvent` carries no payload beyond its key.
- Umbraco Search broadcasts `IndexRebuildStarting` / `IndexRebuildCompleted` to every connection
  (seen in the browser by a Writers user with no Settings access). That is Umbraco Search's
  behaviour, not ours.
- The backoffice client exposes `UMB_MANAGEMENT_API_SERVER_EVENT_CONTEXT` (from
  `@umbraco-cms/backoffice/management-api`) on 17.0 and 18.0, with `isConnected` and
  `byEventSourcesAndEventTypes`.

## Decision

- Two event sources: `SearchManager:Rebuilds` and `SearchManager:MemberRebuilds`.
  `RebuildEventAuthorizer` admits Settings section access to the first, and Settings plus Members
  section access to the second. Events are routed, never broadcast.
- Event types `RebuildQueued`, `RebuildStarted`, `RebuildProgress`, `RebuildSucceeded`,
  `RebuildFailed`, `RebuildCancelled`, keyed by the run id. A hosted `RebuildEventBroadcaster`
  turns the queue's `RunChanged` into events; progress is sent at most every 2 seconds per run.
- The browser treats an event as "re-read `GET /rebuilds`" (which applies the member rule again),
  folding events that arrive during a fetch into one more fetch. It polls only while the hub is
  not connected, and fetches once when the connection comes back.
- Rows never move: the runs keep the server's queue order and the index table the registry's
  order.

## Consequences

- With live events, a watching browser makes one request per event (fewer than one per second)
  instead of polling every 2 seconds for the length of a run.
- In a load-balanced backoffice, events reach only browsers connected to the server that runs the
  queue (SignalR groups are per server); that matches the in-memory queue, which is per server too.
