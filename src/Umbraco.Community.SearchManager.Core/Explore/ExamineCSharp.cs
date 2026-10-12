using System.Globalization;
using System.Text;

namespace Umbraco.Community.SearchManager.Core.Explore;

/// <summary>The C# that runs a query with Examine, for the show-query panel.</summary>
/// <param name="Code">The code.</param>
/// <param name="Fluent">Whether it uses Examine's fluent API rather than <c>NativeQuery</c>.</param>
public sealed record CSharpQuery(string Code, bool Fluent);

/// <summary>
/// Writes the Examine C# for a list of filters (issue #17): the fluent API when every filter has
/// a fluent form (<c>Field</c>, <c>GroupedOr</c>, <c>RangeQuery</c>, with <c>And()</c> and
/// <c>Not()</c>), otherwise <c>NativeQuery</c> with the compiled Lucene, which is what Search
/// Manager itself runs. Free text, "is missing" and raw Lucene have no fluent form.
/// </summary>
public static class ExamineCSharp
{
    private const string Indent = "    ";

    /// <summary>Writes the code for filters.</summary>
    /// <param name="filters">The filters that run (none skipped).</param>
    /// <param name="schema">The index's fields.</param>
    /// <param name="culture">The culture picked, or null.</param>
    /// <param name="today">Today's date, for relative date values.</param>
    /// <param name="lucene">The compiled Lucene, used when a filter has no fluent form.</param>
    /// <param name="take">The page size the example executes with.</param>
    /// <returns>The code.</returns>
    public static CSharpQuery For(
        IReadOnlyList<Filter> filters,
        FieldSchema schema,
        string? culture,
        DateOnly today,
        string lucene,
        int take = 20
    )
    {
        ArgumentNullException.ThrowIfNull(filters);
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(lucene);

        List<(bool Exclude, string Call)>? calls = FluentCalls(filters, schema, culture, today);
        if (calls is null || calls.Count == 0)
        {
            return new CSharpQuery(Native(lucene, take), false);
        }

        var code = new StringBuilder("var results = searcher.CreateQuery()");
        for (int i = 0; i < calls.Count; i++)
        {
            (bool exclude, string call) = calls[i];
            string join = (i, exclude) switch
            {
                // A query cannot start with a prohibited clause: match everything, then exclude.
                (0, true) => $"\n{Indent}.All()\n{Indent}.Not().",
                (0, false) => $"\n{Indent}.",
                (_, true) => $"\n{Indent}.Not().",
                _ => $"\n{Indent}.And().",
            };
            code.Append(join).Append(call);
        }

        code.Append(
            CultureInfo.InvariantCulture,
            $"\n{Indent}.Execute(QueryOptions.SkipTake(0, {take}));"
        );
        return new CSharpQuery(code.ToString(), true);
    }

    /// <summary>Writes the <c>NativeQuery</c> form for raw Lucene.</summary>
    /// <param name="lucene">The Lucene text.</param>
    /// <param name="take">The page size the example executes with.</param>
    /// <returns>The code.</returns>
    public static string Native(string lucene, int take = 20)
    {
        ArgumentNullException.ThrowIfNull(lucene);
        return $"var results = searcher.CreateQuery()\n{Indent}.NativeQuery({Literal(lucene)})\n{Indent}.Execute(QueryOptions.SkipTake(0, {take.ToString(CultureInfo.InvariantCulture)}));";
    }

    /// <summary>A C# string literal.</summary>
    /// <param name="value">The text.</param>
    /// <returns>The quoted, escaped literal.</returns>
    public static string Literal(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return "\""
            + value
                .Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("\"", "\\\"", StringComparison.Ordinal)
                .Replace("\n", "\\n", StringComparison.Ordinal)
                .Replace("\r", "\\r", StringComparison.Ordinal)
            + "\"";
    }

    // The fluent calls in filter order, include groups merged as the compiler ORs them; null when
    // any filter has no fluent form.
    private static List<(bool, string)>? FluentCalls(
        IReadOnlyList<Filter> filters,
        FieldSchema schema,
        string? culture,
        DateOnly today
    )
    {
        var calls = new List<(bool, string)>();
        var groups = new Dictionary<(string, FilterOperator), List<string>>();
        var groupCalls = new Dictionary<(string, FilterOperator), int>();

        foreach (Filter filter in filters)
        {
            if (filter.Kind != FilterKind.Field || filter.Op == FilterOperator.Missing)
            {
                return null;
            }

            IndexField? resolved = schema.Resolve(filter.Field ?? string.Empty);
            if (resolved is null)
            {
                return null;
            }

            IndexField field = schema.ForCulture(resolved, culture);
            string? value = FluentValue(field, filter, today);
            if (value is null)
            {
                return null;
            }

            bool groupable = filter.Op is FilterOperator.Is or FilterOperator.Contains;
            if (!filter.Exclude && groupable)
            {
                var key = (field.Name, filter.Op);
                if (groups.TryGetValue(key, out List<string>? values))
                {
                    values.Add(Literal(filter.Value.Trim()));
                    calls[groupCalls[key]] = (
                        false,
                        $"GroupedOr(new[] {{ {Literal(field.Name)} }}, {string.Join(", ", values)})"
                    );
                    continue;
                }

                groups[key] = [Literal(filter.Value.Trim())];
                groupCalls[key] = calls.Count;
            }

            calls.Add((filter.Exclude, value));
        }

        return calls;
    }

    private static string? FluentValue(IndexField field, Filter filter, DateOnly today)
    {
        string name = Literal(field.Name);
        string value = filter.Value.Trim();
        switch (filter.Op)
        {
            case FilterOperator.Is:
            case FilterOperator.Contains:
                return $"Field({name}, {Literal(value)})";
            case FilterOperator.Phrase:
                return $"Field({name}, {Literal(value)}.Escape())";
            case FilterOperator.StartsWith:
                return $"Field({name}, {Literal(value)}.MultipleCharacterWildcard())";
            case FilterOperator.Fuzzy:
                return $"Field({name}, {Literal(value)}.Fuzzy())";
        }

        if (field.Type == FieldType.Number)
        {
            if (!FieldOperators.TryReadNumber(value, out double number))
            {
                return null;
            }

            bool whole = number == Math.Floor(number) && Math.Abs(number) <= int.MaxValue;
            string type = whole ? "int" : "double";
            string n = number.ToString("R", CultureInfo.InvariantCulture);
            return filter.Op switch
            {
                FilterOperator.Equals => $"RangeQuery<{type}>(new[] {{ {name} }}, {n}, {n})",
                FilterOperator.AtLeast => $"RangeQuery<{type}>(new[] {{ {name} }}, {n}, null)",
                FilterOperator.GreaterThan =>
                    $"RangeQuery<{type}>(new[] {{ {name} }}, {n}, null, minInclusive: false)",
                FilterOperator.AtMost => $"RangeQuery<{type}>(new[] {{ {name} }}, null, {n})",
                FilterOperator.LessThan =>
                    $"RangeQuery<{type}>(new[] {{ {name} }}, null, {n}, maxInclusive: false)",
                _ => null,
            };
        }

        if (
            field.Type == FieldType.Date
            && FieldOperators.TryReadDate(value, today, out DateOnly day)
        )
        {
            string from = DateLiteral(day);
            return filter.Op switch
            {
                FilterOperator.On =>
                    $"RangeQuery<DateTime>(new[] {{ {name} }}, {from}, {DateLiteral(day.AddDays(1))}, maxInclusive: false)",
                FilterOperator.After => $"RangeQuery<DateTime>(new[] {{ {name} }}, {from}, null)",
                FilterOperator.Before =>
                    $"RangeQuery<DateTime>(new[] {{ {name} }}, null, {from}, maxInclusive: false)",
                _ => null,
            };
        }

        return null;
    }

    private static string DateLiteral(DateOnly day) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"new DateTime({day.Year}, {day.Month}, {day.Day})"
        );
}
