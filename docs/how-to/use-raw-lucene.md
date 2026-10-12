# Use raw Lucene

Every Explore search can show the Lucene query it sends and the C# that runs the same search with
Examine. When chips are not enough, edit the query as raw Lucene, checked as you type.

## See the query your chips build

Choose **Show query**, the button with the angle brackets beside the search box. The panel shows:

- **Lucene query sent:** the query exactly as Explore sends it.
- **As Lucene parsed it:** what Lucene made of it. Bare words are expanded to search every field,
  which is often why a search matches more than you expected.
- **C# with Examine:** code that runs the same search, using the fluent API, or `NativeQuery`
  when a filter has no fluent form.

Each has a **Copy** button. Choose **Hide query** to close the panel.

![The show query panel with the Lucene query sent, the query as Lucene parsed it and the C# NativeQuery call](../images/show-query.png)

## Edit the query as raw Lucene

1. Choose **Lucene** beside the search box. Your chips become the equivalent Lucene query.
2. Edit the query. It is checked as you type.
3. Choose **Run**, or press Enter.

If the query does not parse, the box says **Cannot parse this query**, with Lucene's message and
the character where it failed.

Raw Lucene is only offered for indexes searched by Lucene. For other indexes, such as Umbraco
Search indexes, the **Lucene** button is unavailable and says why.

## Fix common mistakes in one click

Some queries parse but do not do what they look like. Explore warns about them and offers a fix:

| Warning | Fix |
| --- | --- |
| `"and" is searched as a word. Operators must be in capitals.` | **Fix: AND** |
| `level:2 never matches: level is a number field, so it needs a range.` | **Fix: [2 TO 2]** |
| A date term without a range, such as `updateDate:2026-10-01` | The `yyyy-MM-dd` range form |
| A short name such as `type:blogpost`, which is not a Lucene field | The real field name, `__NodeTypeAlias` |
| A field the index does not have | None. Check the field name in **All fields**. |
| A leading wildcard such as `*mug`, which Lucene cannot look up in its term index | None. |

Choose the fix button to apply it.

![Raw Lucene mode with two warnings: a lower-case "and" with Fix: AND, and level:2 with Fix: [2 TO 2]](../images/native-mode.png)

Dates in Lucene ranges must be written as `yyyy-MM-dd`, for example
`updateDate:[2026-10-01 TO *]`. Examine drops a bound it cannot read as a date, so a range in ticks
silently matches every document.

## Go back to chips

Choose **Back to filters**. Explore turns each top-level clause that is a simple filter back into
its chip. A query it cannot split without changing its meaning, such as one that uses `OR` or
boosts, becomes a single **Lucene** chip that you can still edit or remove.

## Related

- [Search an index](search-an-index.md)
- [Search syntax](../reference/search-syntax.md)
