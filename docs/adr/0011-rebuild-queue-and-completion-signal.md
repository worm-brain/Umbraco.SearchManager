# ADR 0011: Rebuilds run through an in-memory server queue and finish on commit

- Status: Accepted
- Date: 2026-10-12
- Issue: Rebuild selected indexes through a server-side queue

> **Correction (2026-10-13, ADR 0022):** the findings below hold for Umbraco 17.0 to 17.2 only.
> From 17.3, and in every 18 release, `ExamineIndexRebuilder` no longer uses a long-running
> operation: it tracks rebuilds in memory and queues background work on `IBackgroundTaskQueue`, and
> a second rebuild of a running index logs a warning and returns `Success` without running, so
> `AlreadyRebuilding` is never returned there. The core dashboard wraps its call in its own
> long-running operation (`ExamineIndexRebuild:{name}`) instead. The decision is unchanged: Search
> Manager checks `IIndexingRebuilderService.IsRebuildingAsync` before it starts.

## Context

Bulk rebuilds must run on the server (so they survive the browser leaving), refuse duplicates,
and only be marked succeeded once the rebuilt index is searchable. The issue asked:

- What is the best completion signal for a core Examine run: `LuceneIndex.WaitForChanges()`,
  `IndexCommitted`, or the document count settling? (blocking)
- On a load-balanced backoffice, where does the queue run? (not blocking)

## Findings (verified 2026-10-12 against Umbraco 17.0.0 / 18.2.1 and Examine 3.7.1 / 3.10.0)

- `IIndexingRebuilderService.TryRebuild` calls `IIndexRebuilder.RebuildIndexAsync(name)` with the
  default `useBackgroundThread: true`. `ExamineIndexRebuilder` runs the rebuild as a long-running
  operation (`ILongRunningOperationService`, type `RebuildExamineIndex-{name}`,
  `allowConcurrentExecution: false`); `IsRebuildingAsync` reads that operation. The rebuild
  calls `index.CreateIndex()` (clears it) and then every populator.
- In Examine's `LuceneIndex`, asynchronous mode (the default) queues writes and calls
  `ScheduleCommit()`: the populators, and so the long-running operation, return before Lucene
  has committed. In synchronous mode (`RunAsync == false`), each batch calls
  `_committer.CommitNow()` (which raises `IndexCommitted`) and then `WaitForChanges()` (the
  searcher sees the new generation) before returning. `LuceneIndex.WithThreadingMode(...)` forces
  a mode until disposed.
- Umbraco Search runs its rebuilds in the background and announces only
  `IndexRebuildStartingNotification` and `IndexRebuildCompletedNotification` on the server that
  runs them; a failure is logged, not announced. Its Examine provider's `IIndexCommitMonitor`
  waits for a physical index's commit, and with zero-downtime indexing `IActiveIndexManager`
  swaps the new copy in after the commit (or keeps the old copy when the new one is empty or
  unhealthy).

## Decision

- Core Examine: call `IIndexRebuilder.RebuildIndexAsync(name, TimeSpan.Zero,
  useBackgroundThread: false)` from the queue's own background task, with a Lucene index forced
  into synchronous mode for the duration, then `WaitForChanges()` once more. When the call
  returns, every batch is committed and searchable. Going through `IIndexRebuilder` keeps
  Umbraco's long-running operation, so the core dashboard still shows the index as rebuilding and
  refuses a concurrent rebuild. `IndexingError` raised during the run fails it with its message.
  The document count settling was rejected (no reliable end), and `IndexCommitted` alone fires
  per batch, not at the end.
- Umbraco Search: `IDistributedContentIndexRebuilder.Rebuild(alias)`, then wait for the
  notifications on this server. No start within 2 minutes, or no document-count change for 10
  minutes, fails the run. With the Examine provider: zero downtime waits for the swap (and fails
  when the old copy was kept), otherwise waits for `IIndexCommitMonitor` and `WaitForChanges()`.
- The queue (`RebuildQueue`, Core) holds status in memory; a hosted `RebuildWorker` drains it on
  the server that received the request. One-at-a-time runs go in queue order; all-at-once runs
  start straight away. Queued runs can be cancelled; running ones cannot.
- Progress: a poller reads the document count of the index being written once a second; the
  estimate's total is the previous successful run's final count, or the index's count when
  queued.
- Load balancing (non-blocking question): the queue runs on whichever backoffice server took the
  request. Core Examine rebuilds only affect that server (warned about separately); Umbraco
  Search's rebuilder is distributed and rebuilds on every server, but the run tracks this one.

## Consequences

- While a core Examine run is going, other writes to that index (content saves) are also
  processed synchronously; saves are slower for the duration, never lost.
- Status is lost on restart until persisted history lands; a run interrupted by shutdown is
  recorded as failed.
