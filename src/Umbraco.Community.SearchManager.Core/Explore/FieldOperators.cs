using System.Globalization;
using System.Text.RegularExpressions;

namespace Umbraco.Community.SearchManager.Core.Explore;

/// <summary>Which operators apply to which field type, and how chip values are read.</summary>
public static partial class FieldOperators
{
    private static readonly FilterOperator[] TextOperators =
    [
        FilterOperator.Contains,
        FilterOperator.Phrase,
        FilterOperator.StartsWith,
        FilterOperator.Fuzzy,
        FilterOperator.Missing,
    ];

    private static readonly FilterOperator[] KeywordOperators =
    [
        FilterOperator.Is,
        FilterOperator.StartsWith,
        FilterOperator.Missing,
    ];

    private static readonly FilterOperator[] NumberOperators =
    [
        FilterOperator.Equals,
        FilterOperator.GreaterThan,
        FilterOperator.AtLeast,
        FilterOperator.LessThan,
        FilterOperator.AtMost,
        FilterOperator.Missing,
    ];

    private static readonly FilterOperator[] DateOperators =
    [
        FilterOperator.On,
        FilterOperator.After,
        FilterOperator.Before,
        FilterOperator.Missing,
    ];

    /// <summary>Every field type with its operators, as the Lucene compiler runs them.</summary>
    public static IReadOnlyList<FieldTypeOperators> All { get; } =
    [
        new(FieldType.Text, TextOperators),
        new(FieldType.Keyword, KeywordOperators),
        new(FieldType.Number, NumberOperators),
        new(FieldType.Date, DateOperators),
    ];

    /// <summary>The operators a field of <paramref name="type"/> offers, in display order.</summary>
    /// <param name="type">The field type.</param>
    /// <returns>The operators; the first is the default for a new chip.</returns>
    public static IReadOnlyList<FilterOperator> For(FieldType type) =>
        type switch
        {
            FieldType.Keyword => KeywordOperators,
            FieldType.Number => NumberOperators,
            FieldType.Date => DateOperators,
            _ => TextOperators,
        };

    /// <summary>
    /// Reads a date chip value: <c>yyyy-MM-dd</c>, <c>today</c>, <c>yesterday</c>, or a number of
    /// days back such as <c>-7d</c>.
    /// </summary>
    /// <param name="value">The chip value.</param>
    /// <param name="today">Today's date, so relative values resolve against an injected clock.</param>
    /// <param name="date">The day the value names.</param>
    /// <returns><see langword="true"/> when the value is a date.</returns>
    public static bool TryReadDate(string value, DateOnly today, out DateOnly date)
    {
        ArgumentNullException.ThrowIfNull(value);
        string text = value.Trim();

        if (text.Equals("today", StringComparison.OrdinalIgnoreCase))
        {
            date = today;
            return true;
        }

        if (text.Equals("yesterday", StringComparison.OrdinalIgnoreCase))
        {
            date = today.AddDays(-1);
            return true;
        }

        Match relative = RelativeDays().Match(text);
        if (
            relative.Success
            && int.TryParse(
                relative.Groups["days"].Value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int days
            )
            && days <= 36_500
        )
        {
            date = today.AddDays(-days);
            return true;
        }

        return DateOnly.TryParseExact(
            text,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out date
        );
    }

    /// <summary>Reads a number chip value in the invariant culture.</summary>
    /// <param name="value">The chip value.</param>
    /// <param name="number">The number.</param>
    /// <returns><see langword="true"/> when the value is a finite number.</returns>
    public static bool TryReadNumber(string value, out double number)
    {
        ArgumentNullException.ThrowIfNull(value);
        return double.TryParse(
                value.Trim(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out number
            ) && double.IsFinite(number);
    }

    [GeneratedRegex(@"^-(?<days>\d{1,5})d$", RegexOptions.CultureInvariant)]
    private static partial Regex RelativeDays();
}
