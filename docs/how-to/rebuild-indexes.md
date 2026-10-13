# Rebuild indexes

Rebuild one index, several, or all of them from the **Indexes** view. Rebuilds run in a queue on
the server, so you can keep working, leave the page or close the browser while they run.

## Rebuild one index

1. Open **Settings > Advanced > Search Manager**.
2. In the index's row, choose the rebuild button in the **Actions** column.
3. Read the confirmation and choose **Rebuild index**.

## Rebuild several indexes at once

1. Tick the indexes you want, or tick the box in the header to select every index that can be
   rebuilt. To rebuild everything, choose **Rebuild all** instead.
2. Choose **Rebuild 2 indexes**, or whatever the button says for your selection.
3. Read the confirmation. For each index it says what happens to live search while it rebuilds:
   - **Cleared first:** searches on this index return partial results until it finishes. Core
     Examine indexes rebuild this way.
   - **No downtime:** search keeps using the current copy until the new one is ready, then swaps.
     Umbraco Search indexes marked **Zero downtime** rebuild this way.
4. Leave **Rebuild one at a time** ticked to put less load on the site, or clear it to start every
   rebuild straight away.
5. Choose **Rebuild 2 indexes**.

![The rebuild confirmation for ExternalIndex, which is cleared first, and Umb_PublishedContent, which rebuilds with no downtime](../images/rebuild-confirm.png)

A row whose checkbox and rebuild button are dimmed cannot be rebuilt from Search Manager. Hover
the button to see why. An index that is already queued or running cannot be queued again.

## Follow the rebuilds

The **Rebuilds** panel opens above the table and updates live from the server:

| Status | Meaning |
| --- | --- |
| **Waiting, next in line** | Queued behind other one-at-a-time rebuilds. |
| **Starting** | Picked up by the queue. |
| **About 1,200 of 2,400 documents (50%), estimated from the last run** | Running. The estimate compares the documents written so far with the previous successful run. |
| **Building a fresh copy. Search keeps using the current one.** | A zero-downtime rebuild is running. |
| **Done in 4 s, 189 documents** | Finished, and the index is searchable. |
| **Done in 30 s, nothing to index** | Finished, and there was nothing to put in the index, such as `Umb_Members` on a site without members. |
| **Failed after 30 s: ...** | Stopped, with the error. |
| **Cancelled before it started** | You cancelled it while it was queued. |

![The Rebuilds panel with three finished rebuilds above the indexes table](../images/rebuilds.png)

The **Health** column shows **Queued for rebuild** or **About 50% (estimate)** for the same runs.

- To cancel a rebuild that has not started, choose **Cancel** in its row. A running rebuild
  cannot be cancelled.
- To empty the panel, choose **Clear finished**. The runs stay in each index's history.

## See past rebuilds

The **Last rebuilt** column shows the latest recorded rebuild of each index. Choose the
information button in the **Actions** column to see the last 20 rebuilds of that index, with who
started each one, how long it took, the document counts before and after, the server, and the
error of a failure.

Rebuilds started elsewhere are recorded too. Umbraco's rebuild of empty indexes shortly after the
site starts shows as **At startup**. Rebuilds from the core Examine dashboard, from Umbraco
Search's own index page, or from another package show as **Started outside Search Manager**. A
core Examine rebuild on another server of a load-balanced site is recorded by that server, with
its name.

## Troubleshooting

- **An Umbraco Search rebuild failed with "kept the current copy".** With zero downtime, Umbraco
  Search keeps the current copy when the new one comes out empty or unhealthy, so search still
  uses the old data. Check the log for the reason. An index with nothing to index at all, such as
  `Umb_Members` on a site without members, is not a failure: it shows **nothing to index**.
- **A rebuild only changed one server.** Core Examine indexes are stored per server. See
  [Load-balanced sites](../explanation/how-rebuilds-work.md#load-balanced-sites).
- **The site restarted during a rebuild.** The queue lives in memory, so queued runs are lost and
  the running one is recorded as failed. Start them again.

## Related

- [How rebuilds work](../explanation/how-rebuilds-work.md)
- [Check index health](check-index-health.md)
- [Management API](../reference/api.md#rebuilds)
