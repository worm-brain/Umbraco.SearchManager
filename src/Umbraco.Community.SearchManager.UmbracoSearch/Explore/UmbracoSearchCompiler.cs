using System.Globalization;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Search.Core.Models.Searching.Filtering;
using Umbraco.Cms.Search.Provider.Examine.Configuration;
using Umbraco.Community.SearchManager.Core.Explore;
using ExploreFilter = Umbraco.Community.SearchManager.Core.Explore.Filter;
using SearchFieldNames = Umbraco.Cms.Search.Core.Constants.FieldNames;
using SearchFilter = Umbraco.Cms.Search.Core.Models.Searching.Filtering.Filter;

namespace Umbraco.Community.SearchManager.UmbracoSearch.Explore;

/// <summary>An Umbraco Search filter compiled from chips, with a note for the C# (such as the document type alias).</summary>
/// <param name="Filter">The filter.</param>
/// <param name="Comment">A comment for the show-query C#, or null.</param>
internal sealed record CompiledFilter(SearchFilter Filter, string? Comment = null);

/// <summary>What <c>ISearcher.SearchAsync</c> gets from a list of chips.</summary>
/// <param name="Query">The free text (words AND together), or null.</param>
/// <param name="Filters">The typed filters, AND together.</param>
/// <param name="Skipped">Chips left out, with the reason.</param>
internal sealed record UmbracoSearchCompilation(
    string? Query,
    IReadOnlyList<CompiledFilter> Filters,
    IReadOnlyList<SkippedFilter> Skipped
);

/// <summary>
/// Compiles Explore chips into an Umbraco Search request (ADR 0016): free text into
/// <c>query</c>, field chips into typed filters with <c>Negate</c> for excluded chips. Field names
/// are property aliases or <c>Umb_*</c> system fields, never with a culture; the culture goes in
/// <c>SearchAsync</c>'s own argument.
/// <list type="bullet">
/// <item>Include chips on one field with the same matching operator OR together in one filter
/// (its values or ranges), as the Lucene compiler groups them (ADR 0002).</item>
/// <item>Umbraco Search ranges include their minimum and exclude their maximum, so integer
/// <c>&gt; n</c> is <c>[n + 1, ...)</c> and <c>&lt;= n</c> is <c>[..., n + 1)</c>; a date
/// <c>on</c> is <c>[day, next day)</c> at offset zero, as Umbraco Search indexes dates.</item>
/// <item>A document type chip holds the alias and sends the content type key.</item>
/// </list>
/// </summary>
internal static class UmbracoSearchCompiler
{
    /// <summary>The operators Umbraco Search runs per field type.</summary>
    internal static IReadOnlyList<FieldTypeOperators> Operators { get; } =
    [
        new(FieldType.Text, [FilterOperator.Contains]),
        new(FieldType.Keyword, [FilterOperator.Is]),
        new(
            FieldType.Number,
            [
                FilterOperator.Equals,
                FilterOperator.GreaterThan,
                FilterOperator.AtLeast,
                FilterOperator.LessThan,
                FilterOperator.AtMost,
            ]
        ),
        new(FieldType.Date, [FilterOperator.On, FilterOperator.After, FilterOperator.Before]),
    ];

    /// <summary>The chips Umbraco Search cannot run, with the reasons the chips show.</summary>
    internal static IReadOnlyList<UnsupportedFilter> Unsupported { get; } =
    [
        new(
            FilterKind.Lucene,
            [],
            [],
            null,
            "Umbraco Search takes filters, not raw Lucene queries"
        ),
        new(
            FilterKind.Text,
            [],
            [FilterOperator.Phrase],
            null,
            "Umbraco Search matches free text word by word, so it cannot search for a phrase"
        ),
        new(FilterKind.Text, [], [], true, "Umbraco Search cannot exclude free text"),
        new(
            FilterKind.Field,
            [],
            [FilterOperator.Missing],
            null,
            "Umbraco Search has no \"is missing\" filter"
        ),
        new(
            FilterKind.Field,
            [FieldType.Text],
            [FilterOperator.Phrase, FilterOperator.StartsWith, FilterOperator.Fuzzy],
            null,
            "Umbraco Search text filters only match words (contains)"
        ),
        new(
            FilterKind.Field,
            [FieldType.Keyword],
            [FilterOperator.StartsWith],
            null,
            "Umbraco Search keyword filters only match whole values (is)"
        ),
    ];

    // Include chips with these operators on one field OR together; ranges AND.
    private static readonly HashSet<FilterOperator> Alternatives =
    [
        FilterOperator.Contains,
        FilterOperator.Is,
        FilterOperator.Equals,
        FilterOperator.On,
    ];

    /// <summary>Compiles chips into a request.</summary>
    /// <param name="filters">The chips, in order.</param>
    /// <param name="fields">The index's fields.</param>
    /// <param name="today">Today's date, for relative date values.</param>
    /// <param name="contentTypeKeys">
    /// Finds the keys of the document, media or member types with an alias; empty when none has it.
    /// </param>
    /// <returns>The query, filters and the chips left out.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    internal static UmbracoSearchCompilation Compile(
        IReadOnlyList<ExploreFilter> filters,
        UmbracoSearchFields fields,
        DateOnly today,
        Func<string, IReadOnlyList<Guid>> contentTypeKeys
    )
    {
        ArgumentNullException.ThrowIfNull(filters);
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(contentTypeKeys);

        var skipped = new List<SkippedFilter>();
        var words = new List<string>();
        // Filters in chip order; an OR group is one entry that later chips add values to.
        var groups = new List<Group>();
        var open = new Dictionary<(string Field, FilterOperator Op), Group>();

        for (int index = 0; index < filters.Count; index++)
        {
            ExploreFilter filter = filters[index];
            string? reason = WhyNot(filter, fields, today);
            if (reason is not null)
            {
                skipped.Add(new SkippedFilter(index, reason));
                continue;
            }

            if (filter.Kind == FilterKind.Text)
            {
                words.Add(filter.Value.Trim());
                continue;
            }

            string name = fields.Schema.Resolve(filter.Field!)!.Name;
            FieldValues kind = fields.KindOf(name)!.Value;
            Value value;
            if (name.Equals(SearchFieldNames.ContentTypeId, StringComparison.OrdinalIgnoreCase))
            {
                IReadOnlyList<Guid> keys = ContentTypeKeys(filter.Value.Trim(), contentTypeKeys);
                if (keys.Count == 0)
                {
                    skipped.Add(
                        new SkippedFilter(
                            index,
                            $"There is no document, media or member type \"{filter.Value.Trim()}\""
                        )
                    );
                    continue;
                }

                value = new Value(
                    [.. keys.Select(key => key.ToString("D"))],
                    null,
                    filter.Value.Trim()
                );
            }
            else
            {
                Value? read = Read(name, kind, filter, today, out string? why);
                if (read is null)
                {
                    skipped.Add(new SkippedFilter(index, why!));
                    continue;
                }

                value = read;
            }

            if (!filter.Exclude && Alternatives.Contains(filter.Op))
            {
                var key = (name.ToUpperInvariant(), filter.Op);
                if (open.TryGetValue(key, out Group? group))
                {
                    group.Values.Add(value);
                    continue;
                }

                var created = new Group(name, kind, filter.Exclude, [value]);
                open[key] = created;
                groups.Add(created);
                continue;
            }

            groups.Add(new Group(name, kind, filter.Exclude, [value]));
        }

        return new UmbracoSearchCompilation(
            words.Count == 0 ? null : string.Join(' ', words),
            [.. groups.Select(Build)],
            skipped
        );
    }

    /// <summary>
    /// Why Umbraco Search cannot run a chip on this index, or null when it can. The rules in
    /// <see cref="Unsupported"/> come first, so their reason names the engine.
    /// </summary>
    /// <param name="filter">The chip.</param>
    /// <param name="fields">The index's fields.</param>
    /// <param name="today">Today's date, for relative date values.</param>
    /// <returns>The reason, or null.</returns>
    internal static string? WhyNot(ExploreFilter filter, UmbracoSearchFields fields, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(fields);

        IndexField? field =
            filter.Kind == FilterKind.Field && !string.IsNullOrEmpty(filter.Field)
                ? fields.Schema.Resolve(filter.Field)
                : null;
        string? rule = UnsupportedFilter.FirstReason(Unsupported, filter, field?.Type);
        if (rule is not null)
        {
            return rule;
        }

        if (filter.Kind == FilterKind.Text)
        {
            return string.IsNullOrWhiteSpace(filter.Value) ? "There is no text to search for"
                : filter.Op == FilterOperator.Contains ? null
                : $"Free text cannot use {filter.Op}";
        }

        if (field is null)
        {
            return $"There is no field \"{filter.Field}\" in this index";
        }

        if (!Operators.First(entry => entry.Type == field.Type).Operators.Contains(filter.Op))
        {
            return $"{filter.Op} does not apply to {field.Type.ToString().ToLowerInvariant()} fields";
        }

        return string.IsNullOrWhiteSpace(filter.Value) ? "The filter has no value" : null;
    }

    /// <summary>Reads a chip's value for its field's kind; null with the reason when it cannot.</summary>
    private static Value? Read(
        string field,
        FieldValues kind,
        ExploreFilter filter,
        DateOnly today,
        out string? reason
    )
    {
        reason = null;
        string text = filter.Value.Trim();
        switch (kind)
        {
            case FieldValues.Integers:
                if (
                    !long.TryParse(
                        text,
                        NumberStyles.AllowLeadingSign,
                        CultureInfo.InvariantCulture,
                        out long number
                    ) || number is < int.MinValue or >= int.MaxValue
                )
                {
                    reason = $"\"{text}\" is not a whole number, which {field} holds";
                    return null;
                }

                return IntegerValue(filter.Op, (int)number);

            case FieldValues.Decimals:
                if (
                    !decimal.TryParse(
                        text,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out decimal amount
                    )
                )
                {
                    reason = $"\"{text}\" is not a number";
                    return null;
                }

                Value? decimalValue = DecimalValue(filter.Op, amount);
                if (decimalValue is null)
                {
                    reason =
                        "Umbraco Search decimal ranges include their minimum and exclude their maximum, so they cannot run this comparison";
                }

                return decimalValue;

            case FieldValues.DateTimeOffsets:
                if (!FieldOperators.TryReadDate(text, today, out DateOnly day))
                {
                    reason = $"\"{text}\" is not a date (use yyyy-MM-dd, today or -7d)";
                    return null;
                }

                return DateValue(filter.Op, day);

            case FieldValues.Keywords:
                return new Value([KeywordValue(field, text)], null, null);

            default:
                return new Value([text], null, null);
        }
    }

    private static Value IntegerValue(FilterOperator op, int n) =>
        op switch
        {
            FilterOperator.Equals => new Value([n], null, null),
            FilterOperator.GreaterThan => new Value(
                null,
                new IntegerRangeFilterRange(n + 1, null),
                null
            ),
            FilterOperator.AtLeast => new Value(null, new IntegerRangeFilterRange(n, null), null),
            FilterOperator.LessThan => new Value(null, new IntegerRangeFilterRange(null, n), null),
            _ => new Value(null, new IntegerRangeFilterRange(null, n + 1), null),
        };

    // Ranges are [min, max): "greater than" and "at most" have no exact form for decimals.
    private static Value? DecimalValue(FilterOperator op, decimal n) =>
        op switch
        {
            FilterOperator.Equals => new Value([n], null, null),
            FilterOperator.AtLeast => new Value(null, new DecimalRangeFilterRange(n, null), null),
            FilterOperator.LessThan => new Value(null, new DecimalRangeFilterRange(null, n), null),
            _ => null,
        };

    private static Value DateValue(FilterOperator op, DateOnly day)
    {
        DateTimeOffset start = new(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        return op switch
        {
            FilterOperator.On => new Value(
                null,
                new DateTimeOffsetRangeFilterRange(start, start.AddDays(1)),
                null
            ),
            FilterOperator.After => new Value(
                null,
                new DateTimeOffsetRangeFilterRange(start, null),
                null
            ),
            _ => new Value(null, new DateTimeOffsetRangeFilterRange(null, start), null),
        };
    }

    /// <summary>
    /// A keyword as Umbraco Search writes it: keys as lower-case GUIDs, object types by their enum
    /// name (<c>Document</c>); keywords are matched exactly, case included.
    /// </summary>
    private static string KeywordValue(string field, string text)
    {
        if (Guid.TryParse(text, out Guid key))
        {
            return key.ToString("D");
        }

        return
            field.Equals(SearchFieldNames.ObjectType, StringComparison.OrdinalIgnoreCase)
            && Enum.TryParse(text, ignoreCase: true, out UmbracoObjectTypes objectType)
            && !int.TryParse(text, CultureInfo.InvariantCulture, out _)
            ? objectType.ToString()
            : text;
    }

    private static IReadOnlyList<Guid> ContentTypeKeys(
        string value,
        Func<string, IReadOnlyList<Guid>> contentTypeKeys
    ) => Guid.TryParse(value, out Guid key) ? [key] : contentTypeKeys(value);

    /// <summary>Builds the Umbraco Search filter for a group of values on one field.</summary>
    private static CompiledFilter Build(Group group)
    {
        string? comment = group.Values.Any(value => value.Comment is not null)
            ? string.Join(", ", group.Values.Select(value => value.Comment))
            : null;
        IEnumerable<object> exact = group.Values.SelectMany(value => value.Exact ?? []);
        object[] ranges = [.. group.Values.Select(value => value.Range).OfType<object>()];
        SearchFilter filter = group.Kind switch
        {
            FieldValues.Keywords => new KeywordFilter(
                group.Field,
                [.. exact.Cast<string>()],
                group.Exclude
            ),
            FieldValues.Integers when ranges.Length == 0 => new IntegerExactFilter(
                group.Field,
                [.. exact.Cast<int>()],
                group.Exclude
            ),
            FieldValues.Integers => new IntegerRangeFilter(
                group.Field,
                [.. ranges.Cast<IntegerRangeFilterRange>()],
                group.Exclude
            ),
            FieldValues.Decimals when ranges.Length == 0 => new DecimalExactFilter(
                group.Field,
                [.. exact.Cast<decimal>()],
                group.Exclude
            ),
            FieldValues.Decimals => new DecimalRangeFilter(
                group.Field,
                [.. ranges.Cast<DecimalRangeFilterRange>()],
                group.Exclude
            ),
            FieldValues.DateTimeOffsets => new DateTimeOffsetRangeFilter(
                group.Field,
                [.. ranges.Cast<DateTimeOffsetRangeFilterRange>()],
                group.Exclude
            ),
            _ => new TextFilter(group.Field, [.. exact.Cast<string>()], group.Exclude),
        };
        return new CompiledFilter(filter, comment);
    }

    /// <summary>One chip's value: exact values or a range, and a comment for the C#.</summary>
    private sealed record Value(object[]? Exact, object? Range, string? Comment);

    /// <summary>Chips that compile into one filter.</summary>
    private sealed record Group(string Field, FieldValues Kind, bool Exclude, List<Value> Values);
}
