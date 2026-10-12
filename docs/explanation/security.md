# The security model

Search Manager shows what is in your search indexes, which can include unpublished content and
member details. This page explains who can see it and what it can do.

## Who can use it

- **Settings section access** is needed for the menu item, the views and every API endpoint. Users
  without it do not see Search Manager at all.
- **Members section access** is needed as well for member indexes, such as `MembersIndex` and
  `Umb_Members`. Without it, those indexes are left out of the table, the Explore index picker, the
  rebuild queue and the history, and their rebuild events are not sent to the browser.

These are the same rules the core Examine Management dashboard follows.

## What it can see

Explore shows the stored fields of any document in an index you can open. `InternalIndex` holds
unpublished content too, so anyone with Settings access can read unpublished content through it,
as they can with the core dashboard. Give Settings access only to people who should see that.

## What it can change

Search Manager reads indexes and rebuilds them. It does not change content, settings or the
index definitions. A rebuild writes the index from the content that is already there.

Rebuilds put load on the site and, for core Examine indexes, make search return partial results
while they run. That is why they need Settings access, and why the confirmation says what each
rebuild does to live search.

## What it stores

The `searchManagerRebuildHistory` table records, for each rebuild, the index, the engine, the
server's machine name, the name of the user who started it, the times, the outcome, the document
counts and the error message of a failure. The newest 20 runs per index are kept.

## Reporting a problem

See [SECURITY.md](../../SECURITY.md) to report a vulnerability privately.

## Related

- [Management API](../reference/api.md)
- [How rebuilds work](how-rebuilds-work.md)
