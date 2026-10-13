# Read field statistics

**Diagnose > Fields** shows how every field of an index is filled: how many documents have it,
how many are missing it, how many distinct terms it holds and which are the most common. Use it
when a search misses pages and you suspect the field is empty, analysed differently than you
thought, or holds other values than you expected.

## Open the statistics

1. Go to **Settings > Advanced > Search Manager** and open the **Diagnose** tab.
2. Choose **Fields**, then pick the index.

You can also get there from an index: the details sidebar in the **Indexes** view, and the
Search Manager box on Umbraco Search's index page, both have a **Field statistics** button.

The first visit reads the whole index, which takes a few seconds on a large one. The result is
kept until the index changes, so later visits are instant.

## Read the table

| Column | What it tells you |
| --- | --- |
| Field | The field name, Umbraco's name for it (such as "Document type") and, for Umbraco Search, the field's name in the Lucene index. |
| Type | Text, keyword, number or date: what a chip on the field does. |
| Analyzer | What the index does to a value before storing its terms. `StandardAnalyzer` splits and lower-cases words; "None (whole value)" keeps the value as one term; "None (numeric terms)" is a number or a date. |
| Coverage | The share of documents with a value, and how many are missing it. |
| Unique terms | How many distinct terms the field holds. "at least" means counting stopped at 200,000. |
| Top terms | The five most common terms with their counts. Date fields show their earliest and latest value instead. |

Text fields list analysed terms, not whole values: "Getting started" is stored as `getting` and
`started`. A field marked **Stored only, not searchable** is kept for display and no query can
find it.

Use the filter box to find a field by name. **Show system fields, culture variants and raw
copies** adds the fields Umbraco writes for itself (`__Key`, `__Path`), the per-culture copies
(`nodeName_en-us`) and the unanalysed copies of rich text (`__Raw_bodyText`).

## Search from the table

- **Click a top term** to open **Explore** with a chip for it: "contains" for text, "is" for
  keywords, "=" for numbers.
- **Click "N missing"** to open **Explore** with an "is missing" chip, which lists the documents
  without the field. Umbraco Search cannot search for missing values, so its missing counts are
  not links.

## Umbraco Search indexes

With the adapter package, Umbraco Search indexes that use the Examine provider have statistics
too. Umbraco Search keeps one document per culture, so a page in two languages counts twice;
the summary says so. Fields are shown under the names filters take (`title`, not
`Field_title_texts`), segments as their own rows, and links into Explore pick the site's default
culture, because without a culture Umbraco Search only finds pages that do not vary.

Indexes that are not Lucene indexes, searchers that read several indexes, and Umbraco Search
providers other than Examine show why they have no statistics instead.

## Related

- [Search an index](search-an-index.md)
- [ADR 0018: field statistics per reader version](../adr/0018-field-statistics-per-reader-version.md)
