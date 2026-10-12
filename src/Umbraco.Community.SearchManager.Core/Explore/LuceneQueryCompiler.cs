using System.Globalization;
using System.Text;

namespace Umbraco.Community.SearchManager.Core.Explore;

/// <summary>A filter the compiler left out of the query, and why.</summary>
/// <param name="Index">Position of the filter in the list that was compiled.</param>
/// <param name="Reason">A sentence for the chip's tooltip.</param>
public sealed record SkippedFilter(int Index, string Reason);

/// <summary>The Lucene query compiled from a list of filters.</summary>
/// <param name="Query">The query text; <c>*:*</c> when no filter applies.</param>
/// <param name="Skipped">Filters left out because the index cannot run them.</param>
/// <param name="HasScoringClause">Whether any clause ranks results (free text, text fields, raw Lucene), as opposed to only filtering.</param>
public sealed record LuceneCompilation(
    string Query,
    IReadOnlyList<SkippedFilter> Skipped,
    bool HasScoringClause
);

/// <summary>
/// Compiles Explore filters into Lucene query text for Examine's <c>NativeQuery</c>, which parses
/// it with the index's per-field analyzers and turns ranges on numeric and date fields into
/// numeric range queries (ADR 0002, ADR 0003).
/// <list type="bullet">
/// <item>Each filter is one required (<c>+</c>) or prohibited (<c>-</c>) clause; filters AND together.</item>
/// <item>Include filters on the same field with the same matching operator (not a range) OR
/// together in one clause, so picking two document types finds both.</item>
/// <item>Free text is searched in every field (Examine's default fields), word by word.</item>
/// <item>Numbers and dates compile to ranges; dates are written as <c>yyyy-MM-dd</c>, which
/// Examine converts to the ticks it stores (ADR 0003).</item>
/// <item>With a culture, a field with a culture variant (<c>nodeName_en-us</c>) is queried there.</item>
/// <item>When only prohibited clauses remain, <c>*:*</c> is added: Lucene matches nothing for a
/// query that only excludes.</item>
/// </list>
/// </summary>
public static class LuceneQueryCompiler
{
    /// <summary>The query that matches every document.</summary>
    public const string MatchAll = "*:*";

    // Operators whose include chips on one field OR together. Ranges AND: level>=2 and
    // level<=4 must both hold.
    private static readonly HashSet<FilterOperator> Alternatives =
    [
        FilterOperator.Contains,
        FilterOperator.Phrase,
        FilterOperator.StartsWith,
        FilterOperator.Fuzzy,
        FilterOperator.Is,
        FilterOperator.Equals,
        FilterOperator.On,
    ];

    /// <summary>Compiles filters into one Lucene query.</summary>
    /// <param name="filters">The chips, in order.</param>
    /// <param name="schema">The index's fields.</param>
    /// <param name="culture">The culture whose variant fields to query, or null.</param>
    /// <param name="today">Today's date, for relative date values.</param>
    /// <param name="allowLucene">Whether raw Lucene filters may run (the index is a Lucene index).</param>
    /// <returns>The query and the filters left out.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static LuceneCompilation Compile(
        IReadOnlyList<Filter> filters,
        FieldSchema schema,
        string? culture,
        DateOnly today,
        bool allowLucene = true
    )
    {
        ArgumentNullException.ThrowIfNull(filters);
        ArgumentNullException.ThrowIfNull(schema);

        var skipped = new List<SkippedFilter>();
        // Each clause body without its +/- prefix, keyed so OR groups collect their members.
        var clauses = new List<Clause>();
        var groups = new Dictionary<(string Field, FilterOperator Op), Clause>();
        bool scoring = false;

        for (int index = 0; index < filters.Count; index++)
        {
            Filter filter = filters[index];
            string? reason = WhyNot(filter, schema, today, allowLucene);
            if (reason is not null)
            {
                skipped.Add(new SkippedFilter(index, reason));
                continue;
            }

            if (filter.Kind is FilterKind.Text or FilterKind.Lucene)
            {
                scoring = true;
                clauses.Add(new Clause(filter.Exclude, [CompileFree(filter)]));
                continue;
            }

            IndexField field = schema.ForCulture(schema.Resolve(filter.Field!)!, culture);
            scoring |= field.Type == FieldType.Text && filter.Op != FilterOperator.Missing;

            if (filter.Op == FilterOperator.Missing)
            {
                // Missing is "no value": prohibit any value. Excluding Missing requires one.
                clauses.Add(new Clause(!filter.Exclude, [$"{field.Name}:[* TO *]"]));
                continue;
            }

            string body = CompileField(field, filter, today);
            if (!filter.Exclude && Alternatives.Contains(filter.Op))
            {
                var key = (field.Name.ToUpperInvariant(), filter.Op);
                if (groups.TryGetValue(key, out Clause? group))
                {
                    group.Bodies.Add(body);
                    continue;
                }

                var clause = new Clause(false, [body]);
                groups[key] = clause;
                clauses.Add(clause);
                continue;
            }

            clauses.Add(new Clause(filter.Exclude, [body]));
        }

        return new LuceneCompilation(Join(clauses), skipped, scoring);
    }

    /// <summary>
    /// Why the Lucene compiler cannot run a filter on this schema, or null when it can. The UI
    /// shows the reason on the disabled chip.
    /// </summary>
    /// <param name="filter">The filter.</param>
    /// <param name="schema">The index's fields.</param>
    /// <param name="today">Today's date, for relative date values.</param>
    /// <param name="allowLucene">Whether raw Lucene filters may run.</param>
    /// <returns>The reason, or null.</returns>
    public static string? WhyNot(
        Filter filter,
        FieldSchema schema,
        DateOnly today,
        bool allowLucene = true
    )
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(schema);

        switch (filter.Kind)
        {
            case FilterKind.Text:
                return string.IsNullOrWhiteSpace(filter.Value) ? "There is no text to search for"
                    : filter.Op is FilterOperator.Contains or FilterOperator.Phrase ? null
                    : $"Free text cannot use {filter.Op}";
            case FilterKind.Lucene:
                return !allowLucene ? "This index does not take raw Lucene queries"
                    : string.IsNullOrWhiteSpace(filter.Value) ? "The Lucene query is empty"
                    : null;
        }

        IndexField? field = string.IsNullOrEmpty(filter.Field)
            ? null
            : schema.Resolve(filter.Field);
        if (field is null)
        {
            return $"There is no field \"{filter.Field}\" in this index";
        }

        if (!FieldOperators.For(field.Type).Contains(filter.Op))
        {
            return $"{filter.Op} does not apply to {field.Type.ToString().ToLowerInvariant()} fields";
        }

        if (filter.Op == FilterOperator.Missing)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(filter.Value))
        {
            return "The filter has no value";
        }

        return field.Type switch
        {
            FieldType.Number when !FieldOperators.TryReadNumber(filter.Value, out _) =>
                $"\"{filter.Value}\" is not a number",
            FieldType.Date when !FieldOperators.TryReadDate(filter.Value, today, out _) =>
                $"\"{filter.Value}\" is not a date (use yyyy-MM-dd, today or -7d)",
            _ => null,
        };
    }

    /// <summary>
    /// Escapes Lucene's special characters so a value is read as text: <c>+ - ! ( ) { } [ ] ^ " ~
    /// * ? : \ / &amp; |</c> and whitespace.
    /// </summary>
    /// <param name="value">The raw value.</param>
    /// <returns>The escaped value.</returns>
    public static string Escape(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var builder = new StringBuilder(value.Length);
        foreach (char c in value)
        {
            if (
                c
                    is '\\'
                        or '+'
                        or '-'
                        or '!'
                        or '('
                        or ')'
                        or ':'
                        or '^'
                        or '['
                        or ']'
                        or '"'
                        or '{'
                        or '}'
                        or '~'
                        or '*'
                        or '?'
                        or '|'
                        or '&'
                        or '/'
                || char.IsWhiteSpace(c)
            )
            {
                builder.Append('\\');
            }

            builder.Append(c);
        }

        return builder.ToString();
    }

    private static string CompileFree(Filter filter)
    {
        if (filter.Kind == FilterKind.Lucene)
        {
            return $"({filter.Value.Trim()})";
        }

        if (filter.Op == FilterOperator.Phrase)
        {
            return Quote(filter.Value);
        }

        string[] words = Words(filter.Value);
        return words.Length == 1
            ? Term(words[0])
            : $"({string.Join(' ', words.Select(word => "+" + Term(word)))})";
    }

    private static string CompileField(IndexField field, Filter filter, DateOnly today)
    {
        string name = field.Name;
        string value = filter.Value.Trim();
        return filter.Op switch
        {
            FilterOperator.Contains => Words(value) is [string word]
                ? $"{name}:{Term(word)}"
                : $"{name}:({string.Join(' ', Words(value).Select(w => "+" + Term(w)))})",
            FilterOperator.Phrase => $"{name}:{Quote(value)}",
            FilterOperator.StartsWith => $"{name}:{Escape(value)}*",
            FilterOperator.Fuzzy => $"{name}:{Escape(value)}~",
            FilterOperator.Is => $"{name}:{Keyword(value)}",
            FilterOperator.Equals => $"{name}:[{Number(value)} TO {Number(value)}]",
            FilterOperator.GreaterThan => $"{name}:{{{Number(value)} TO *]",
            FilterOperator.AtLeast => $"{name}:[{Number(value)} TO *]",
            FilterOperator.LessThan => $"{name}:[* TO {Number(value)}}}",
            FilterOperator.AtMost => $"{name}:[* TO {Number(value)}]",
            FilterOperator.On => $"{name}:[{Day(value, today, 0)} TO {Day(value, today, 1)}}}",
            FilterOperator.After => $"{name}:[{Day(value, today, 0)} TO *]",
            _ => $"{name}:[* TO {Day(value, today, 0)}}}",
        };
    }

    private static string[] Words(string value) =>
        value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    // A free-text word; a trailing * stays a prefix wildcard (umbr*), anything else is literal.
    private static string Term(string word)
    {
        if (IsOperatorWord(word))
        {
            return Quote(word);
        }

        return word.Length > 1 && word.EndsWith('*') && !word.StartsWith('*')
            ? Escape(word.TrimEnd('*')) + "*"
            : Escape(word);
    }

    // A keyword is one term: quoting keeps spaces inside it, escaping keeps everything else literal.
    private static string Keyword(string value) =>
        IsOperatorWord(value) || value.Any(char.IsWhiteSpace) ? Quote(value) : Escape(value);

    private static bool IsOperatorWord(string word) => word is "AND" or "OR" or "NOT" or "TO";

    private static string Quote(string value) =>
        "\""
        + value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
        + "\"";

    private static string Number(string value)
    {
        FieldOperators.TryReadNumber(value, out double number);
        return number.ToString("R", CultureInfo.InvariantCulture);
    }

    private static string Day(string value, DateOnly today, int addDays)
    {
        FieldOperators.TryReadDate(value, today, out DateOnly date);
        return date.AddDays(addDays).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static string Join(List<Clause> clauses)
    {
        if (clauses.Count == 0)
        {
            return MatchAll;
        }

        var parts = new List<string>();
        if (clauses.All(clause => clause.Exclude))
        {
            parts.Add(MatchAll);
        }

        foreach (Clause clause in clauses)
        {
            string body =
                clause.Bodies.Count == 1
                    ? clause.Bodies[0]
                    : $"({string.Join(' ', clause.Bodies)})";
            parts.Add((clause.Exclude ? "-" : "+") + body);
        }

        return string.Join(' ', parts);
    }

    private sealed record Clause(bool Exclude, List<string> Bodies);
}
