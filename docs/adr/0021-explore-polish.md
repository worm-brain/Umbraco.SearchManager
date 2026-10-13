# ADR 0021: Facets without their own chips, display values, Umbraco Search default culture

- Status: Accepted
- Date: 2026-10-13
- Issue: Polish Explore after v1

## Context

Using Explore after v1 showed four rough edges: picking a value in the fields panel made every
other value of that field disappear; keyword values showed as the index lower-cased them
(`blogpost`); Umbraco Search found no variant content until a culture was picked; and chips the
server skipped looked as if they ran.

## Decision

- **Facets.** A field's top values are counted under every chip except that field's own include
  chips (`FacetFilters.Without`), as faceted search does: picking `type is product` still lists
  the other types with their counts, so a second can be added. Exclusions and "is missing" stay,
  so an excluded value stays gone. The total stays the result count. Fields with no chip of their
  own reuse the result set; date ranges are counted from the reduced chips too. Native mode has
  no chips, so nothing is dropped.
- **Display values.** For non-numeric terms, the `__Raw_` copy or the stored field of the first
  live document with the term (within the results for facets) is read; when one of its values
  equals the term ignoring case but not exactly, it is returned as `display`. Display only:
  chips keep sending the indexed term, so matching is unchanged. Analysed text, whose stored
  value is the whole sentence, has no display.
- **Default culture.** `ExploreSchema.defaultCulture` comes from the source
  (`IExploreSource.GetDefaultCultureAsync`, null by default). Umbraco Search returns the site's
  default language, because it keeps one document per culture and finds only invariant
  documents without one; core Examine keeps none. In the URL, no `culture` key means the schema's
  default and an empty `culture=` means explicitly none.
- **Skipped chips.** The client strikes through chips the server skipped, mapping the server's
  indexes (into the sent list) back to chips, only while the result belongs to the chips sent.
  What is sent does not change.

## Consequences

- One extra bit set per faceted field that has its own include chips.
- Display costs one stored-document read per returned value.
