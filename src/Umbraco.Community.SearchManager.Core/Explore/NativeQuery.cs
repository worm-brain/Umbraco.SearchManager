using System.Globalization;
using System.Text.RegularExpressions;

namespace Umbraco.Community.SearchManager.Core.Explore;

/// <summary>A likely mistake in a raw Lucene query, with an optional one-click fix.</summary>
/// <param name="Code">
/// What is wrong: <c>needs_range</c> (a number or date term), <c>lowercase_operator</c>,
/// <c>unknown_field</c> or <c>leading_wildcard</c>.
/// </param>
/// <param name="Message">The sentence the UI shows.</param>
/// <param name="Start">Zero-based position of the text the warning is about.</param>
/// <param name="Length">Length of that text.</param>
/// <param name="Replacement">What the fix puts in its place, or null when there is no fix.</param>
public sealed record NativeWarning(
    string Code,
    string Message,
    int Start,
    int Length,
    string? Replacement
);

/// <summary>The verdict on a raw Lucene query.</summary>
/// <param name="Valid">Whether the engine's parser accepts it.</param>
/// <param name="Error">The parser's message when it does not.</param>
/// <param name="Position">Zero-based position of the error, when the parser gives one.</param>
/// <param name="Warnings">Likely mistakes in a query that parses (or not), with fixes.</param>
public sealed record NativeValidation(
    bool Valid,
    string? Error,
    int? Position,
    IReadOnlyList<NativeWarning> Warnings
);

/// <summary>
/// Finds likely mistakes in a raw Lucene query for an index (issue #17): a number or date
/// compared with a plain term (Examine indexes them as numbers, so only a range matches), an
/// operator in lower case (searched as a word), a field the index does not have (an alias is
/// fixed to its field) and a leading wildcard. A pattern scan, not a parser: it never rejects a
/// query, it only advises.
/// </summary>
public static partial class NativeQueryLinter
{
    /// <summary>Lints a query.</summary>
    /// <param name="query">The raw Lucene text.</param>
    /// <param name="schema">The index's fields.</param>
    /// <returns>The warnings, in text order.</returns>
    public static IReadOnlyList<NativeWarning> Lint(string query, FieldSchema schema)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(schema);

        var warnings = new List<NativeWarning>();
        foreach (Match clause in FieldClause().Matches(query))
        {
            Group name = clause.Groups["field"];
            Group value = clause.Groups["value"];
            IndexField? byName = schema.Fields.FirstOrDefault(field =>
                string.Equals(field.Name, name.Value, StringComparison.OrdinalIgnoreCase)
            );

            if (byName is null)
            {
                IndexField? aliased = schema.Resolve(name.Value);
                warnings.Add(
                    aliased is null
                        ? new NativeWarning(
                            "unknown_field",
                            $"There is no field \"{name.Value}\" in this index, so this clause matches nothing.",
                            name.Index,
                            name.Length,
                            null
                        )
                        : new NativeWarning(
                            "unknown_field",
                            $"\"{name.Value}\" is a Search Manager alias, not a Lucene field. In Lucene it is {aliased.Name}.",
                            name.Index,
                            name.Length,
                            aliased.Name
                        )
                );
                continue;
            }

            if (
                byName.Type is FieldType.Number or FieldType.Date
                && value.Value[0] is not ('[' or '{')
                && value.Value != "*"
            )
            {
                warnings.Add(NeedsRange(name.Value, byName.Type, value));
            }
        }

        foreach (Match word in LowerCaseOperator().Matches(query))
        {
            Group op = word.Groups["op"];
            warnings.Add(
                new NativeWarning(
                    "lowercase_operator",
                    $"\"{op.Value}\" is searched as a word. Operators must be in capitals.",
                    op.Index,
                    op.Length,
                    op.Value.ToUpperInvariant()
                )
            );
        }

        foreach (Match wildcard in LeadingWildcard().Matches(query))
        {
            Group term = wildcard.Groups["term"];
            warnings.Add(
                new NativeWarning(
                    "leading_wildcard",
                    $"\"{term.Value}\" starts with a wildcard, which Lucene cannot look up in its term index.",
                    term.Index,
                    term.Length,
                    term.Value.TrimStart('*', '?')
                )
            );
        }

        return [.. warnings.OrderBy(warning => warning.Start)];
    }

    // level:3 is a term query on a field Examine writes as a number, so it never matches; the
    // fix is the one-value range. A date term becomes its day's range, from yyyy-MM-dd or ticks.
    private static NativeWarning NeedsRange(string field, FieldType type, Group value)
    {
        string text = value.Value.Trim('"');
        string? range = type switch
        {
            FieldType.Number when FieldOperators.TryReadNumber(text, out _) =>
                $"[{text} TO {text}]",
            FieldType.Date => DayRange(text),
            _ => null,
        };
        string kind = type == FieldType.Number ? "number" : "date";
        return new NativeWarning(
            "needs_range",
            $"{field}:{value.Value} never matches: {field} is a {kind} field, so it needs a range.",
            value.Index,
            value.Length,
            range
        );
    }

    private static string? DayRange(string text)
    {
        DateOnly day;
        if (
            !DateOnly.TryParseExact(
                text,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out day
            )
        )
        {
            // Examine stores dates as ticks; a ticks term is turned into its day (ADR 0003).
            if (
                !long.TryParse(
                    text,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out long ticks
                )
                || ticks > DateTime.MaxValue.Ticks
            )
            {
                return null;
            }

            day = DateOnly.FromDateTime(new DateTime(ticks));
        }

        string from = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        string to = day.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return $"[{from} TO {to}}}";
    }

    // field:value, field:"a phrase", field:[a TO b], field:{a TO b} and mixed brackets.
    [GeneratedRegex(
        """(?<![\w.\\-])(?<field>[A-Za-z_][\w.\-]*):(?<value>[\[{][^\]}]*[\]}]|"[^"]*"|[^\s()]+)""",
        RegexOptions.CultureInvariant
    )]
    private static partial Regex FieldClause();

    // and / or / not on their own between other words: Lucene reads them as terms.
    [GeneratedRegex("""(?<=^|[\s(])(?<op>and|or|not)(?=[\s)]|$)""", RegexOptions.CultureInvariant)]
    private static partial Regex LowerCaseOperator();

    // A term starting with * or ? (but not *:* or [* TO ...]): after a space, a bracket, a sign or a colon.
    [GeneratedRegex(
        """(?<=^|[\s(+\-:])(?<term>[*?]+[^\s()*:\]}]+[^\s()]*)""",
        RegexOptions.CultureInvariant
    )]
    private static partial Regex LeadingWildcard();
}
