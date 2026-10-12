# Search syntax

What the Explore search box understands. Press Enter and each part becomes a chip. Parts are
separated by spaces, except inside double quotes.

## Forms

| You type | Chip | Matches |
| --- | --- | --- |
| `umbraco examine` | Text | Documents with any field containing the words. All bare words share one chip. |
| `"getting started"` | Phrase | The words in that order, in any field. |
| `-draft` | Text, excluded | Documents without the word. |
| `type:blogPost` | Field | The field has the value. |
| `-type:product` | Field, excluded | The field does not have the value. |
| `name:"getting started"` | Field phrase | A text field contains the phrase. |
| `name:umb*` | Starts with | A text field has a word, or a keyword field a value, starting with `umb`. |
| `name:umbraco~` | Is like | A fuzzy match, allowing small spelling differences. |
| `author:*` | Has a value | The field is present. |
| `level:>2` | Is more than | Numbers and dates. Also `>=`, `<` and `<=`. |
| `updated:2026-10-01` | Is on | A date field falls on that day. |

If you leave a double quote open, the whole input is searched as plain text, and a message says
so.

Chips on different fields must all match. Click a chip to change its operator, value or exclude
setting.

## Dates

Write dates as `yyyy-MM-dd`. You can also write:

| Value | Means |
| --- | --- |
| `today` | Today, on the server. |
| `yesterday` | Yesterday. |
| `-7d` | 7 days ago. Any number of days works. |

So `updated:>-7d` finds documents updated in the last 7 days.

## Field names

Type any field the index has, as listed under **All fields** in the fields panel. A field the
index does not have stays as a chip, struck through with the reason, and is not sent.

The operator follows the field's type, which comes from the index itself:

| Type | Operators |
| --- | --- |
| Text | contains, has the phrase, starts with, is like, is missing |
| Keyword | is, starts with, is missing |
| Number | is, is more than, is at least, is less than, is at most, is missing |
| Date | is on, is on or after, is before, is missing |

## Short names

Short names stand for Umbraco's own fields. Each is offered only when the index has its field.

**Core Examine indexes**

| Short name | Field | Shown as |
| --- | --- | --- |
| `type` | `__NodeTypeAlias` | Document type |
| `kind` | `__IndexType` | Index type |
| `published` | `__Published` | Published |
| `updated` | `updateDate` | Updated |
| `created` | `createDate` | Created |
| `name` | `nodeName` | Name |
| `id` | `__NodeId` | Node id |
| `key` | `__Key` | Key |
| `parent` | `parentID` | Parent id |
| `path` | `__Path` | Path |
| `url` | `urlName` | URL name |
| `author` | `writerName` | Last edited by |
| `creator` | `creatorName` | Created by |
| `varies` | `__VariesByCulture` | Varies by culture |

**Umbraco Search indexes**

| Short name | Shown as |
| --- | --- |
| `type` | Document type. Type the alias; the filter sends the document type's key. |
| `kind` | Object type |
| `name` | Name |
| `updated` | Updated |
| `created` | Created |
| `key` | Key |
| `parent` | Parent key |
| `path` | Path keys |
| `level` | Level |
| `sort` | Sort order |

Short names work in the search box only. In raw Lucene, use the real field name; a warning offers
it as a fix.

## Cultures

With a culture picked, a chip on a field that has culture variants searches the variant, so
`name:home` with **en-US** searches `nodeName_en-us`.

## Related

- [Search an index](../how-to/search-an-index.md)
- [Use raw Lucene](../how-to/use-raw-lucene.md)
