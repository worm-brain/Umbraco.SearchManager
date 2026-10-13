# How rebuilds work

This page explains what happens between choosing **Rebuild** and seeing **Done**, and why some
rebuilds affect live search while others do not.

## A queue on the server

When you confirm a rebuild, the browser asks the server to queue it and returns straight away. A
background worker on that server runs the queue:

- **One at a time** runs the rebuilds in the order you queued them. This is the default because it
  puts the least load on the database and the site.
- **All at once** starts every rebuild straight away.

The queue does not depend on the browser. You can leave the page, sign out or close the browser,
and the rebuilds carry on. When you come back, the **Rebuilds** panel shows where they are.

The queue refuses an index that is already queued or running, so two people cannot start the same
rebuild twice. A queued run can be cancelled; a running one cannot, because neither Examine nor
Umbraco Search can stop a rebuild part-way and leave the index usable.

The queue is held in memory. A restart forgets queued runs, and a running rebuild that the
shutdown interrupts is recorded as failed with "The site shut down before the rebuild finished."

## When a rebuild counts as done

A rebuild is only **Done** when the index can answer searches with every document in it.

- **Core Examine** indexes write documents in batches in the background, so "the rebuild call
  returned" is not the same as "the index is searchable". Search Manager runs the rebuild with the
  index writing synchronously, then waits for the last commit, so **Done** means every document is
  committed. While a core rebuild runs, content saves to that index are also written synchronously:
  they are slower for the duration, never lost.
- **Umbraco Search** indexes report when a rebuild starts and finishes. Search Manager waits for
  those reports on the server that ran it. With zero downtime it also waits for the new copy to
  replace the old one. A rebuild that does not start within 2 minutes, or whose document count
  stops changing for 10 minutes, is marked as failed.

## What happens to live search

The two engines rebuild differently, and the confirmation says which applies to each index:

- **Cleared first.** Core Examine indexes are emptied and refilled. Until the rebuild finishes,
  searches that use the index return partial results. On a busy site, rebuild these at a quiet
  time.
- **No downtime.** Umbraco Search indexes marked **Zero downtime** build a fresh copy beside the
  current one. Search keeps using the current copy until the new one is ready, then swaps. If the
  new copy comes out empty or unhealthy, Umbraco Search keeps the current one, and Search Manager
  reports the run as failed so you know the index did not change.

The exception is an index with nothing to index, such as `Umb_Members` on a site without
members. The new copy is empty, but so is the current one, so nothing changed: the run succeeds
and the Rebuilds panel says **nothing to index**.

## Progress estimates

While a rebuild runs, Search Manager reads the number of documents written so far once a second.
The percentage compares that with the previous successful rebuild of the same index, or with the
index's size when it was queued if there is no previous run. It is an estimate: an index that has
grown or shrunk since then finishes above or below 100%.

## History

Every rebuild Search Manager runs is recorded when it finishes, in the
`searchManagerRebuildHistory` table that the package adds on first start. Each index keeps its
newest 20 runs.

Rebuilds started elsewhere are recorded too:

- **Core Examine indexes.** Every core rebuild goes through Umbraco's index rebuilder, and Search
  Manager wraps it. Umbraco's own rebuild of empty indexes about a minute after the site starts
  (of every index after a cold boot) is recorded as **At startup**. Rebuilds from the core
  Examine dashboard, a language change or another package are recorded as **Started outside
  Search Manager**. Both carry the document counts before and after, and a failure with its error.
- **Umbraco Search indexes.** Rebuilds started from Umbraco Search's own index page, by a content
  type change or on another server are recorded as **Started outside Search Manager**, always as
  succeeded and without document counts, because Umbraco Search does not announce failures.

Each server records the core Examine rebuilds it runs itself. A rebuild on another server of a
load-balanced site appears with that server's name, and only that server's own copy changed.

Limitation: if another package replaces Umbraco's index rebuilder after Search Manager has
wrapped it, rebuilds started outside Search Manager are not recorded. A warning in the log says so
at startup.

## Load-balanced sites

On a site with more than one server:

- **Core Examine indexes are stored on each server.** A rebuild runs on the server that took your
  request and only rebuilds that server's copy. The other servers keep theirs. The Indexes view
  names the server you are on, and a banner and the confirmation say so.
- **Umbraco Search indexes rebuild on every server,** because Umbraco Search distributes the
  rebuild itself. Search Manager follows the run on the server you are on.
- **The queue and the Rebuilds panel are per server.** The history is in the shared database, so
  every server sees it, with the server each run ran on.

## Related

- [Rebuild indexes](../how-to/rebuild-indexes.md)
- [The security model](security.md)
