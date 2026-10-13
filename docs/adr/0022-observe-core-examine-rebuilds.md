# ADR 0022: Observe core Examine rebuilds by decorating IIndexRebuilder

- Status: Accepted
- Date: 2026-10-13
- Issues: Record core Examine rebuilds started outside Search Manager; show Incomplete health
  after a failed core rebuild

## Context

ADR 0012 left a gap: core Examine rebuilds started outside Search Manager (Umbraco's startup
rebuild, the core Examine dashboard, a language change, another package) leave no history entry,
so the history and the "Last rebuilt" column miss them, and the trigger `Startup` the brief lists
was never offered. The brief also lists an **Incomplete** health for an index whose last rebuild
failed part-way, which needs to know about every failed core rebuild, wherever it started.

## Findings (verified 2026-10-13 against Umbraco 17.0.0, 17.3.0, 17.7.1, 18.0.0 and 18.2.1)

- Every core rebuild goes through `IIndexRebuilder`: `RebuildOnStartupHandler` (first request, up
  to 17.5 / 18.0) and `RebuildOnStartedHandler` (`UmbracoApplicationStartedNotification`, 17.6 /
  18.1 and later), the core dashboard through `IndexingRebuilderService`,
  `LanguageIndexingNotificationHandler`, `Suspendable.ExamineEvents.ResumeIndexers`, and Search
  Manager's own `ExamineIndexSource`. Umbraco Search never uses it.
- `ExamineIndexRebuilder` changed in 17.3: up to 17.2 it tracks rebuilds as long-running
  operations (`RebuildExamineIndex-{name}`, `RebuildAllExamineIndexes`) and answers
  `AlreadyRebuilding`; from 17.3 (and in all of 18) it keeps an in-memory set plus
  `IBackgroundTaskQueue`, and a second single-index rebuild logs a warning, returns `Success` and
  does nothing. The core dashboard, from 17.3, wraps its call in its own long-running operation
  (`ExamineIndexRebuild:{name}`) and asks for a foreground rebuild, the same call Search Manager
  makes, so `useBackgroundThread: false` cannot tell them apart.
- The startup rebuild is the only multi-index rebuild with a start delay (always one minute, on
  every version checked); everything else passes no delay.
- A rebuild of several indexes catches and logs every populator failure; a single-index rebuild
  lets it propagate (wrapped in an `AggregateException` on 17.0 to 17.2).
- `AddExamineComposer` registers the rebuilder: on 17.0 three times (no-op singleton, Examine
  singleton, then Examine transient, which wins); from 17.7.1 and on 18 once, as a singleton.
  `ExamineIndexRebuilder` takes `IEnumerable<IIndexPopulator>` on every version checked, and is
  the only consumer of the populators.
- Examine's `LuceneIndex.WaitForChanges()` waits for the near-real-time reader to reach the last
  write already made; it does not drain the asynchronous write queue (Examine.Lucene 3.7.1).

## Decision

- `CoreRebuildRecordingComposer` (`[ComposeAfter(typeof(AddExamineComposer))]`) replaces the last
  `IIndexRebuilder` registration with `RecordingIndexRebuilder` around it, keeping its lifetime,
  and builds the inner `ExamineIndexRebuilder` with every registered populator wrapped in
  `RecordingIndexPopulator`. Umbraco's no-op rebuilder is left alone. At startup a warning is
  logged if the resolved rebuilder is not the decorator, and an information line if the populators
  could not be wrapped.
- Search Manager's own rebuilds set an `AsyncLocal` marker (`SearchManagerRebuildScope`) and pass
  straight through, as do calls Umbraco would refuse (not the main domain, not running), calls for
  an index already rebuilding, and indexes without a populator.
- Any other call is run by the decorator: it starts tracking (`CoreRebuildRecorder.Begin`), asks
  the inner rebuilder for a foreground rebuild, and records the run when it returns. A background
  request returns success at once and does this on the thread pool, without the caller's
  execution context. A refused attempt is discarded; a throw is recorded as failed (unwrapped) and
  rethrown.
- Trigger: `Startup` for the first multi-index rebuild with a delay in the process, `Other` for
  everything else. A new `RebuildTrigger.Startup` value; the column is a string, so no migration.
- A successful startup run that went from 0 to 0 documents is not recorded: an index with nothing
  to index (`MembersIndex` on a site without members) is "empty" on every start, so Umbraco
  rebuilds it every time, and those rows would push real runs out of its 20. Failed startup runs,
  and any run with documents, are recorded.
- While tracked, a Lucene index runs in synchronous mode, as Search Manager's own runs do (ADR
  0011), so the document count read at the end is final. `IndexingError` and populator failures
  (through the wrapper) fail the run with the first message.
- The decorator's `IsRebuildingAsync` is also true while it tracks a run, which makes the startup
  rebuild visible per index on 17.0 to 17.2 too.
- Incomplete (no migration): an Examine index is Incomplete when the latest history entry for it
  **on this server** failed with a document count. A failure Search Manager refused before
  anything changed (`RebuildRefusedException`) is recorded without a count, as are v1's failed
  rows, so neither turns an index Incomplete. Precedence: Rebuilding, Corrupted, Unhealthy,
  Incomplete, then Healthy or Empty. `IndexSummary.FailedRebuild` carries the end time, error and
  count; rebuild stays available.

## Consequences

- Accepted risk: on 17.3 and later a core rebuild run by the decorator in the background is no
  longer cancelled at shutdown (Umbraco's foreground rebuild takes no token); only its start delay
  is. On 17.0 to 17.2 the inner rebuild still runs as a long-running operation.
- A background request returns `Success` before Umbraco would have answered `AlreadyRebuilding`
  or `NotAllowedToRun`; the decorator repeats those checks first, and again after the delay.
- The set of indexes a rebuild-all tracks is computed the way Umbraco computes it, a moment
  before Umbraco does; an index that changes in between can be tracked wrongly.
- During a tracked run other writes to that index are synchronous too (as in ADR 0011).
- Another package that replaces `IIndexRebuilder` after this composer bypasses the recording
  (logged at startup). ExamineX for 17 was not checked.
- Each server records its own core rebuilds, and Incomplete is per server; on hosts whose machine
  name changes per instance (Azure App Service scale-out) an old instance's failure is not shown.
- The Indexes view does not refresh by itself when an external rebuild ends; it shows the result
  on the next reload.
- Known core bug, not worked around: a failed long-running operation stays "running" until it
  expires (about 5 minutes; `LongRunningOperationService` reads the faulted task's result and the
  status update rolls back). Up to 17.2 that hits every failed core rebuild; from 17.3 a failed
  core-dashboard rebuild (`ExamineIndexRebuild:{name}`), seen through
  `IndexingRebuilderService.IsRebuildingAsync`. The index shows Rebuilding, and Search Manager
  refuses to rebuild it, for that long; then it shows Incomplete. Observed on 17.7.1.
