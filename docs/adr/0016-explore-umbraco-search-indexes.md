# ADR 0016: Explore compiles chips to Umbraco Search's SearchAsync; filter fields are aliases only

- Status: Accepted
- Date: 2026-10-12
- Issue: Run Explore filters against Umbraco Search indexes

## Context

Explore's chips (ADR 0002) must run against Umbraco Search indexes through
`ISearcher.SearchAsync`, which takes free text, typed filters with `Negate`, a culture and a
segment, and returns only keys and object types. The issue's blocking question: do filters take
property field names as the alias only, or with a culture or segment?

## Findings (verified 2026-10-12 against the v17.2.0 and v18.2.0 tags; identical in both)

- **Field names are the property alias (or an `Umb_*` system field), never with a culture.**
  The Examine provider writes one Lucene document per culture (id `{key}_{culture}`, with
  `Sys_Culture` set to the culture or `none`); invariant values are copied into every culture's
  document. `SearchAsync` picks documents with `Sys_Culture` in `[culture, none]`, or only `none`
  without a culture. A segment only adds `_{segment}` to physical names, and the provider queries
  both when `segment` is passed. Umbraco Search's own `VariantFilterTest` filters
  `new TextFilter("title", ...)` with `culture: "da-DK"`. (Umbraco Search's `CLAUDE.md` says
  otherwise; the code and tests win.)
- Physical names are `Field_{name}_{texts|textsr1|textsr2|textsr3|keywords|integers|decimals|datetimeoffsets}`
  (`FieldNameHelper`), plus `_{segment}`. There is no API that lists an index's fields.
- Filters: `KeywordFilter` (exact, case-sensitive, values OR), `TextFilter` (analysed words, a
  phrase for several words, values OR), `Integer|Decimal|DateTimeOffset` `Exact` and `Range`
  filters. **Ranges include their minimum and exclude their maximum.** Dates are indexed at offset
  zero. `Negate` puts a `Not()` on every value or range.
- Free text is split on spaces; every word must match (analysed or as a prefix) in the aggregated
  text fields. It cannot be negated and has no phrase form.
- `Umb_ContentTypeId` holds the content type **key** (lower-case GUID), `Umb_ObjectType` the
  `UmbracoObjectTypes` name, `Umb_Name` is `textsr1`, dates are `datetimeoffsets`.
- `AccessContext.BypassProtection()` searches protected content too; `null` hides it.
- The provider's document endpoint reads the active physical index by `Field_Umb_Id_keywords`;
  there is no public service behind it.
- On a site with Umbraco Search (Examine 4), `MultiIndexSearcher.Searchers` has another return
  type than on Examine 3, so the core Examine source's compiled call threw
  `MissingMethodException` and Explore on ExternalIndex failed. It is now read by name.

## Decision

- The adapter adds `UmbracoSearchExploreSource`: every content index registration with a
  searcher, kind index, no native query, no term statistics (fields panel values and value
  suggestions stay off in v1).
- **Fields** are Umbraco Search's system fields (always) plus the property fields parsed from the
  active physical index's field names when the index uses the Examine provider. Types: keywords
  Keyword, integers and decimals Number, datetimeoffsets Date, texts Text. Aliases: `type`,
  `kind`, `name`, `updated`, `created`, `key`, `parent`, `path`, `level`, `sort`, `tags`.
- **Compiler** (`UmbracoSearchCompiler`): free text into `query`; Text `contains` to
  `TextFilter`; Keyword `is` to `KeywordFilter`; Number to `IntegerExact` / `IntegerRange`
  (`> n` is `[n + 1, ...)`, `<= n` is `[..., n + 1)`) or `DecimalExact` / `DecimalRange`
  (`>` and `<=` cannot be written as `[min, max)` and are skipped); Date `on` / `after` /
  `before` to `DateTimeOffsetRange` at offset zero. Excluded chips set `Negate`. Include chips on
  one field with the same operator OR into one filter. A document type chip holds the alias and
  sends the key of the document, media or member type with that alias.
- **Unsupported chips** are rules the schema endpoint returns (`ExploreSchema.Unsupported`,
  Core `UnsupportedFilter`: kind, field types, operators, exclude, reason). The client checks them
  before anything else, so the chip is struck through with the engine's reason and never sent;
  the server skips the same chips with the same reason. Umbraco Search's rules: raw Lucene,
  phrase free text, excluded free text, "is missing", text `phrase` / `starts with` / `fuzzy`,
  keyword `starts with`. Its operator lists leave those out.
- **Search**: culture passed as picked (none means invariant documents only, said in the show
  query note); protection bypassed (a Settings tool); without free text, sorted by
  `Umb_UpdateDate` descending (`ExploreResult.NewestFirst`), otherwise Umbraco Search's ranking
  (no scores are returned).
- **Hits**: name (in the culture for variant content), icon and document type from
  `IEntityService`, object type as `content` / `media` / `member` for the editor link, the
  culture searched for variant content. Stored fields come from the active physical index (by
  `Field_Umb_Id_keywords` and `Sys_Culture`), renamed to the names filters take
  (`Field_title_texts` becomes `title`), so the drawer's filter buttons and the column picker work.
- **Show query**: `CompiledQuery.Language` is `C#` and the query is the `SearchAsync` call; the
  panel shows one block for such engines.
- Explore and the Diagnose placeholder read `?index=` (ADR 0015).

## Consequences

- Sites on another Umbraco Search provider get system fields only and no drawer fields.
- Property fields not declared in the provider's `FieldOptions` are indexed as text, so number
  and date chips only apply to declared fields and system fields.
- Brief note for the lead: section 6's Umbraco Search facts gain "filter field names are the
  property alias; culture is `SearchAsync`'s argument and selects per-culture documents; ranges
  are `[min, max)`".
