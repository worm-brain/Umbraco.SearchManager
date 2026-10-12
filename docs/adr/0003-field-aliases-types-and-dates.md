# ADR 0003: Field aliases, field types from the index, and dates as yyyy-MM-dd

- Status: Accepted
- Date: 2026-10-12
- Issue: Search an Examine index with filter chips

## Context

Issue #15 left the alias list open. The brief assumed dates in Lucene ranges are written as
ticks (the stored form) and asked to verify it. Field types for chips must come from the index.

## Verified (Examine 3.7.1 source, and tests against a real in-memory `LuceneIndex`)

- `ExamineMultiFieldQueryParser.GetRangeQuery` hands range bounds to the field's
  `IIndexRangeValueType.GetQuery(string, string, ...)`, which converts each bound with a
  `TypeConverter` (`IndexFieldValueTypeBase.TryConvert`). For `DateTimeType` that is
  `DateTimeConverter`, which reads `2026-01-01` (in any server culture) but not ticks: a ticks
  bound fails to convert and is **silently dropped**, so `updateDate:[639... TO *]` matches every
  dated document. Integers convert as expected, so `level:[3 TO 3]` works.
- `SearchContext.GetFieldValueType` registers a full-text type for any field the index has not
  written since the site started, and `LuceneIndex` then keeps writing that field as full text
  (`FieldValueTypeCollection.GetValueType` is get-or-add). After a restart, `__NodeTypeAlias`
  read that way became full text.

## Decision

- **Dates in ranges are `yyyy-MM-dd`.** The compiler writes `On` as `[d TO d+1}`, `After` as
  `[d TO *]` and `Before` as `[* TO d}`. Chip values may also be `today`, `yesterday` or `-Nd`
  (N days ago), resolved against an injected clock on the server.
- **Field types come from the index's own rules**, resolved as `LuceneIndex` resolves them when it
  writes a document: `__NodeId` raw, `__IndexType` and `__NodeTypeAlias` invariant-culture,
  field definitions (Umbraco's include culture variants), then other `__` fields
  invariant-culture, then full text. Registering a type that way is what indexing would do, so it
  changes nothing. Explore never calls `GetFieldValueType`. Mapping: `DateTimeType` is Date, other
  range types Number, raw and generic-analyzer types Keyword, everything else Text.
- **Aliases** (offered only when the field exists in the target): `type` `__NodeTypeAlias`,
  `kind` `__IndexType`, `published` `__Published`, `updated` `updateDate`, `created` `createDate`,
  `name` `nodeName`, `id` `__NodeId`, `key` `__Key`, `parent` `parentID`, `path` `__Path`, `url`
  `urlName`, `author` `writerName`, `creator` `creatorName`, `varies` `__VariesByCulture`.
- **Culture:** with a culture picked, a chip's field is queried in its culture variant
  (`nodeName_da-dk`) when the index has one of the same type.

## Consequences

- Raw Lucene typed in native mode (#17) must use dates too; the native-mode warning for a date
  term without a range offers the `yyyy-MM-dd` form.
- Decimal bounds are converted in the server's culture by Examine; whole numbers are unaffected.
- The brief's "assume ticks" note is replaced by this record.
