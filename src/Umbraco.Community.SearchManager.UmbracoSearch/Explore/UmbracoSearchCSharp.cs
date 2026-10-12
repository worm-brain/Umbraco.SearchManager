using System.Globalization;
using System.Text;
using Umbraco.Cms.Search.Core.Models.Searching.Filtering;
using Umbraco.Community.SearchManager.Core.Explore;
using SearchFilter = Umbraco.Cms.Search.Core.Models.Searching.Filtering.Filter;

namespace Umbraco.Community.SearchManager.UmbracoSearch.Explore;

/// <summary>
/// Writes the <c>ISearcher.SearchAsync</c> call Explore makes for Umbraco Search, for the
/// show-query panel: the same arguments, in C# a developer can paste.
/// </summary>
internal static class UmbracoSearchCSharp
{
    private const string Indent = "    ";

    /// <summary>Writes the call.</summary>
    /// <param name="indexAlias">The index alias.</param>
    /// <param name="compilation">The compiled chips.</param>
    /// <param name="culture">The culture passed, or null.</param>
    /// <param name="newestFirst">Whether results are sorted by update date, newest first.</param>
    /// <param name="skip">Results skipped.</param>
    /// <param name="take">Results taken.</param>
    /// <returns>The code.</returns>
    internal static string For(
        string indexAlias,
        UmbracoSearchCompilation compilation,
        string? culture,
        bool newestFirst,
        int skip = 0,
        int take = 20
    )
    {
        ArgumentNullException.ThrowIfNull(compilation);
        var code = new StringBuilder("var result = await searcher.SearchAsync(\n");
        code.Append(Indent).Append(ExamineCSharp.Literal(indexAlias)).Append(",\n");
        if (compilation.Query is not null)
        {
            code.Append(Indent)
                .Append("query: ")
                .Append(ExamineCSharp.Literal(compilation.Query))
                .Append(",\n");
        }

        if (compilation.Filters.Count > 0)
        {
            code.Append(Indent).Append("filters:\n").Append(Indent).Append("[\n");
            foreach (CompiledFilter filter in compilation.Filters)
            {
                code.Append(Indent)
                    .Append(Indent)
                    .Append(Write(filter.Filter))
                    .Append(',')
                    .Append(filter.Comment is null ? string.Empty : $" // {filter.Comment}")
                    .Append('\n');
            }

            code.Append(Indent).Append("],\n");
        }

        if (newestFirst)
        {
            code.Append(Indent)
                .Append(
                    "sorters: [new DateTimeOffsetSorter(\"Umb_UpdateDate\", Direction.Descending)],\n"
                );
        }

        code.Append(Indent)
            .Append("culture: ")
            .Append(
                culture is null
                    ? "null, // only documents that do not vary by culture"
                    : ExamineCSharp.Literal(culture) + ","
            )
            .Append('\n');
        code.Append(Indent).Append("accessContext: AccessContext.BypassProtection(),\n");
        code.Append(CultureInfo.InvariantCulture, $"{Indent}skip: {skip},\n{Indent}take: {take});");
        return code.ToString();
    }

    /// <summary>Writes one filter as a constructor call.</summary>
    /// <param name="filter">The filter.</param>
    /// <returns>The expression.</returns>
    internal static string Write(SearchFilter filter) =>
        filter switch
        {
            KeywordFilter keyword => Call(
                "KeywordFilter",
                keyword.FieldName,
                List(keyword.Values.Select(ExamineCSharp.Literal)),
                keyword.Negate
            ),
            TextFilter text => Call(
                "TextFilter",
                text.FieldName,
                List(text.Values.Select(ExamineCSharp.Literal)),
                text.Negate
            ),
            IntegerExactFilter integer => Call(
                "IntegerExactFilter",
                integer.FieldName,
                List(integer.Values.Select(value => value.ToString(CultureInfo.InvariantCulture))),
                integer.Negate
            ),
            IntegerRangeFilter range => Call(
                "IntegerRangeFilter",
                range.FieldName,
                List(
                    range.Ranges.Select(r =>
                        $"new IntegerRangeFilterRange({Int(r.MinValue)}, {Int(r.MaxValue)})"
                    )
                ),
                range.Negate
            ),
            DecimalExactFilter exact => Call(
                "DecimalExactFilter",
                exact.FieldName,
                List(exact.Values.Select(Decimal)),
                exact.Negate
            ),
            DecimalRangeFilter range => Call(
                "DecimalRangeFilter",
                range.FieldName,
                List(
                    range.Ranges.Select(r =>
                        $"new DecimalRangeFilterRange({Decimal(r.MinValue)}, {Decimal(r.MaxValue)})"
                    )
                ),
                range.Negate
            ),
            DateTimeOffsetRangeFilter range => Call(
                "DateTimeOffsetRangeFilter",
                range.FieldName,
                List(
                    range.Ranges.Select(r =>
                        $"new DateTimeOffsetRangeFilterRange({Date(r.MinValue)}, {Date(r.MaxValue)})"
                    )
                ),
                range.Negate
            ),
            _ => $"/* {filter.GetType().Name} on {filter.FieldName} */",
        };

    private static string Call(string type, string field, string values, bool negate) =>
        $"new {type}({ExamineCSharp.Literal(field)}, {values}, Negate: {(negate ? "true" : "false")})";

    private static string List(IEnumerable<string> items) => $"[{string.Join(", ", items)}]";

    private static string Int(int? value) =>
        value?.ToString(CultureInfo.InvariantCulture) ?? "null";

    private static string Decimal(decimal value) =>
        value.ToString(CultureInfo.InvariantCulture) + "m";

    private static string Decimal(decimal? value) => value is null ? "null" : Decimal(value.Value);

    private static string Date(DateTimeOffset? value) =>
        value is not { } date
            ? "null"
            : string.Create(
                CultureInfo.InvariantCulture,
                $"new DateTimeOffset({date.Year}, {date.Month}, {date.Day}, 0, 0, 0, TimeSpan.Zero)"
            );
}
