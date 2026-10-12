# Find out why a page is missing from search

An editor says a page does not show up in the site search. In about ten minutes, this tutorial
takes you through finding out why with Search Manager: whether the page is in the index at all,
whether it is published, what the index holds for it, and how to bring the index up to date.

You need a site with Search Manager installed, and a user with access to the Settings section. The
steps use `ExternalIndex` and `InternalIndex`, the core Examine indexes every Umbraco site has.
Most site searches query `ExternalIndex`.

## 1. Look the page up in the public index

1. Open **Settings > Advanced > Search Manager** and choose the **Explore** tab.
2. Make sure **Index** says `ExternalIndex`.
3. In the search box, type `name:` and the first word of the page's name, then press Enter. For
   a page called "Getting started", type `name:getting`.

A chip appears, such as **Name contains getting**, and the results list the documents that match.

![The Explore view with filter chips and the matching results](../images/explore.png)

- **The page is listed?** It is in the index. Go to [step 3](#3-check-what-the-index-holds).
- **It is not listed?** Carry on with step 2.

## 2. Check whether it is published

`ExternalIndex` only holds published content. `InternalIndex` holds unpublished content too.

1. Change **Index** to `InternalIndex`. Your chip stays.
2. If the page is listed now, choose its name to open the document drawer.
3. Find `__Published` under **System**. A value of `n` means the page is not published, so it is
   not in `ExternalIndex` and no site search will find it. Publish it and you are done.

If the page is listed in neither index, or it is published but missing from `ExternalIndex`, the
index is out of date. Go to [step 4](#4-rebuild-the-index).

## 3. Check what the index holds

The page is in the index, so the question is whether the site search asks for what the index
holds.

1. Choose the page's name to open the document drawer. It lists every stored field: its name,
   document type, path and every property the index stores.
2. Check that the field the site search reads holds the text the editor searched for. A property
   that is not indexed, or a value in a different culture, is a common cause.
3. Choose **Filter for** beside a value to add it as a chip. The results show the other documents
   that share it.

![The document drawer beside the results, with filter buttons on each field](../images/document-drawer.png)

To see the query in a form you can compare with the site's search code, choose **Show query**. It
shows the Lucene sent and the C# with Examine that runs the same search.

## 4. Rebuild the index

1. Choose the **Indexes** tab.
2. Tick `ExternalIndex` and choose **Rebuild 1 index**.
3. Read the confirmation. `ExternalIndex` is **Cleared first**, so site searches return partial
   results until the rebuild finishes. On a busy site, do this at a quiet time.
4. Choose **Rebuild index**.

The **Rebuilds** panel follows the rebuild. You can leave the page while it runs. When it says
**Done in**, every document is committed and searchable.

![The Rebuilds panel with finished rebuilds above the indexes table](../images/rebuilds.png)

Go back to **Explore** and search again. The address bar kept your chip, so the browser's back
button gets you there.

## What you learned

- Explore finds a document by any field, in any index, without writing Lucene.
- `InternalIndex` and the `__Published` field tell an unpublished page from a missing one.
- The document drawer shows exactly what the index stored for a page.
- A rebuild runs on the server, and **Done** means the index is ready to search.

## Next steps

- [Search an index](../how-to/search-an-index.md): every way to filter and narrow results.
- [Rebuild indexes](../how-to/rebuild-indexes.md): bulk rebuilds and the history.
- [How rebuilds work](../explanation/how-rebuilds-work.md): what a rebuild does to live search.
