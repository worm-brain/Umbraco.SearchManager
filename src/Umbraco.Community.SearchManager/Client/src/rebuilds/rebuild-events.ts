/**
 * The server event sources and types Search Manager sends when a rebuild run changes (they match
 * `RebuildEvents` on the server). An event names the run that changed; the view then re-reads
 * `GET /rebuilds`.
 */
export const REBUILD_EVENT_SOURCES = ["SearchManager:Rebuilds", "SearchManager:MemberRebuilds"];

/** Every rebuild event type: queued, started, progress, succeeded, failed, cancelled. */
export const REBUILD_EVENT_TYPES = [
  "RebuildQueued",
  "RebuildStarted",
  "RebuildProgress",
  "RebuildSucceeded",
  "RebuildFailed",
  "RebuildCancelled",
];
