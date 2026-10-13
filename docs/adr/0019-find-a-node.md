# ADR 0019: Find a node: reasons from index validators, clause by clause by restricted queries

- Status: Accepted
- Date: 2026-10-13
- Issue: Show which indexes hold a node and why a query misses it

## Context

"Why isn't my page found?" has two halves: is the page in the index at all, and if it is, which
part of the query misses it. The Diagnose view's second tool answers both for one document, in
every index the user may see, and a content tree action ("Show in search indexes") opens the
same answer from the page itself. It covers documents only; media and members are out of scope.

## Verified

- Umbraco 17.0.0 and 18.0.0: `IEntityService.Get(int|Guid, UmbracoObjectTypes)`,
  `IEntitySearchService.Search(UmbracoObjectTypes, string, int, int)` (the only overload on 17.0),
  `IContentService.GetById(Guid)`, `IPublicAccessService.IsProtected(IContent)`, and on `IContent`
  `Published`, `PublishDate`, `IsCulturePublished`, `IsCultureEdited`, `GetPublishDate`,
  `GetUpdateDate` and `AvailableCultures` (moved to `IPublishableContentBase` on 18, still
  source compatible).
- Core indexes expose their rules through `BaseIndexProvider.ValueSetValidator`: Umbraco's
  `ContentValueSetValidator` (`PublishedValuesOnly`, `SupportProtectedContent`, `ParentId`,
  `ValidatePath`, `ValidateRecycleBin`) on top of `ValueSetValidator` (`IncludeItemTypes`,
  `ExcludeItemTypes`). The Delivery API index has no validator; it is published-only through
  `IUmbracoIndex.PublishedValuesOnly`, keeps one document per culture and keys them by an `id`
  field (`UmbracoExamineFieldNames.DeliveryApiContentIndex.Id` is not used, so older 17.x
  builds work).
- `ContentValueSetBuilder` writes `updateDate` as the node's update date in every index, and
  `updateDate_{culture}` as the culture's publish date in published-only indexes and its update
  date otherwise. Saving a draft does not re-index a published-only index.
- Umbraco Search writes `Umb_UpdateDate` as the node's update date, the same on every culture
  document, stored by the Examine provider as the ticks of the clock time. Its draft index keeps
  trashed content under the recycle bin; the published index drops it on unpublish.

## Decision

- `INodePresenceSource` per engine, keyed by `ExploreTarget.Engine`, next to Explore's
  `IExploreSource`. `GET /diagnose/nodes?query` finds documents by id, key or name;
  `GET /diagnose/nodes/{key}` returns the document's facts and one entry per index the user may
  see (member indexes need Members access); `POST /diagnose/{target}/nodes/{key}/match` tests a
  query.
- Presence: core indexes look the node up by `__NodeId` (the Delivery API index by `id`);
  Umbraco Search's Examine provider by `Field_Umb_Id_keywords` in its active physical index,
  other providers through `SearchAsync` with a key filter per culture. A missing node is
  explained by `PresenceRules`, which checks in order: the index does not hold documents (not
  applicable), the document type is left out (not applicable), the index is empty, the page is
  not published in a published-only index, it is in the recycle bin, it is outside the index's
  parent, it is protected and the index leaves protected content out; otherwise the index is out
  of date.
- Stale: an index copy more than one second older than the node is flagged. Published-only
  indexes are compared with the publish date (a draft save never reaches them), others with the
  update date; variant content per culture in core indexes. Indexes that record no date
  (the Delivery API index, Umbraco Search providers other than Examine) are not checked.
- Matching: each chip is compiled together with a chip that keeps only the node (`__NodeId`, or
  Umbraco Search's `Umb_Id` keyword filter) and run with one result; a hit means the node matches
  that chip. The whole query is run the same way for the overall verdict, which the view always
  shows, because chips on the same field OR together and can disagree with chip-by-chip
  results. Chips the engine cannot run are reported as skipped with the compiler's reason. The
  node's stored values of each chip's field are shown beside it.
- The content tree action is a `default` entity action on documents, shown to users with
  Settings section access, opening a sidebar with the same presence table and a link into the
  Diagnose view.

## Consequences

- The reasons follow Umbraco's own validators, so a custom index with its own validator type
  only gets the rules it shares with `ContentValueSetValidator`.
- A missing node with no rule against it is reported as "out of date"; a rebuild is the remedy.
- Matching runs one query per chip; a long chip list costs a query each, which stays cheap
  because every query is restricted to one node.
