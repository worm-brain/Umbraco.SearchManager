using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Umbraco.Community.SearchManager.Core.Explore;

/// <summary>
/// Turns raw Lucene back into chips when the user leaves native mode (issue #17): every
/// top-level clause that is a simple filter becomes its chip, and anything else stays as raw
/// Lucene. Chips AND together, so this only splits a query whose top-level clauses are all
/// required (<c>+</c>) or prohibited (<c>-</c>), or that has a single clause; any other query
/// (default OR between clauses, <c>AND</c>/<c>OR</c>/<c>NOT</c> keywords, boosts) becomes one
/// raw Lucene chip, so its meaning never changes.
/// </summary>
public static partial class LuceneToChips
{
    /// <summary>Reads a query as chips.</summary>
    /// <param name="lucene">The raw Lucene text.</param>
    /// <param name="schema">The index's fields, for each field's type.</param>
    /// <returns>The chips; empty for a blank query or one that matches everything.</returns>
    public static IReadOnlyList<Filter> Read(string lucene, FieldSchema schema)
    {
        ArgumentNullException.ThrowIfNull(lucene);
        ArgumentNullException.ThrowIfNull(schema);

        string text = lucene.Trim();
        if (text.Length == 0)
        {
            return [];
        }

        Filter whole = new(FilterKind.Lucene, null, FilterOperator.Contains, text);
        List<string>? split = SplitTopLevel(text);
        if (split is null)
        {
            return [whole];
        }

        // *:* matches everything; the compiler adds it in front of a query that only excludes.
        List<string> clauses =
        [
            .. split.Where(clause => clause is not (LuceneQueryCompiler.MatchAll or "+*:*")),
        ];
        if (clauses.Count == 0)
        {
            return [];
        }

        bool allSigned = clauses.All(clause => clause[0] is '+' or '-');
        if (
            clauses.Any(clause => clause is "AND" or "OR" or "NOT" or "&&" or "||" or "!")
            || (!allSigned && clauses.Count > 1)
        )
        {
            return [whole];
        }

        var filters = new List<Filter>();
        foreach (string clause in clauses)
        {
            bool exclude = clause[0] == '-';
            string body = clause[0] is '+' or '-' ? clause[1..] : clause;
            if (body == LuceneQueryCompiler.MatchAll)
            {
                continue;
            }

            IReadOnlyList<Filter>? read = ReadClause(body, exclude, schema);
            filters.AddRange(
                read
                    ?? [new Filter(FilterKind.Lucene, null, FilterOperator.Contains, body, exclude)]
            );
        }

        return filters;
    }

    // One clause without its sign; null when it is not a simple filter.
    private static List<Filter>? ReadClause(string body, bool exclude, FieldSchema schema)
    {
        if (body.StartsWith('(') && body.EndsWith(')'))
        {
            return ReadGroup(body[1..^1], exclude, schema);
        }

        if (body.StartsWith('"') && body.EndsWith('"') && body.Length >= 2)
        {
            return
            [
                new Filter(FilterKind.Text, null, FilterOperator.Phrase, Unquote(body), exclude),
            ];
        }

        Match field = FieldClause().Match(body);
        if (!field.Success)
        {
            return IsPlainWord(body)
                ?
                [
                    new Filter(
                        FilterKind.Text,
                        null,
                        FilterOperator.Contains,
                        Unescape(body),
                        exclude
                    ),
                ]
                : null;
        }

        IndexField? known = schema.Fields.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, field.Groups["field"].Value, StringComparison.Ordinal)
        );
        if (known is null)
        {
            return null;
        }

        string value = field.Groups["value"].Value;
        if (value.StartsWith('(') && value.EndsWith(')'))
        {
            // field:(+a +b), as the compiler writes several words on a text field.
            string[]? words = RequiredWords(value[1..^1]);
            return words is not null && known.Type == FieldType.Text
                ? [Field(known, FilterOperator.Contains, string.Join(' ', words), exclude)]
                : null;
        }

        return value[0] is '[' or '{' ? ReadRange(known, value, exclude)
            : ReadTerm(known, value, exclude) is { } filter ? [filter]
            : null;
    }

    // (+a +b) is free text with every word; (f:a f:b) is an OR group, one include chip each.
    private static List<Filter>? ReadGroup(string inner, bool exclude, FieldSchema schema)
    {
        string[]? words = RequiredWords(inner);
        if (words is not null)
        {
            return
            [
                new Filter(
                    FilterKind.Text,
                    null,
                    FilterOperator.Contains,
                    string.Join(' ', words),
                    exclude
                ),
            ];
        }

        if (exclude)
        {
            return null;
        }

        List<string>? members = SplitTopLevel(inner);
        if (members is null || members.Count < 2 || members.Any(member => member[0] is '+' or '-'))
        {
            return null;
        }

        var filters = new List<Filter>();
        foreach (string member in members)
        {
            List<Filter>? read = ReadClause(member, false, schema);
            if (read is not [{ Kind: FilterKind.Field } single])
            {
                return null;
            }

            filters.Add(single);
        }

        // Only chips the compiler ORs again (same field, same matching operator) round-trip.
        return filters.Select(filter => (filter.Field, filter.Op)).Distinct().Count() == 1
            ? filters
            : null;
    }

    private static Filter? ReadTerm(IndexField field, string value, bool exclude)
    {
        bool quoted = value.Length >= 2 && value.StartsWith('"') && value.EndsWith('"');
        if (quoted)
        {
            return Field(
                field,
                field.Type == FieldType.Text ? FilterOperator.Phrase : FilterOperator.Is,
                Unquote(value),
                exclude
            );
        }

        if (field.Type is FieldType.Number or FieldType.Date)
        {
            // A plain term on a number or date field matches nothing; keep it as raw Lucene.
            return null;
        }

        if (
            value.Length > 1
            && value.EndsWith('*')
            && !value.EndsWith("\\*", StringComparison.Ordinal)
        )
        {
            return Field(field, FilterOperator.StartsWith, Unescape(value[..^1]), exclude);
        }

        if (field.Type == FieldType.Text && FuzzySuffix().Match(value) is { Success: true } fuzzy)
        {
            return Field(field, FilterOperator.Fuzzy, Unescape(value[..fuzzy.Index]), exclude);
        }

        return IsPlainWord(value)
            ? Field(
                field,
                field.Type == FieldType.Text ? FilterOperator.Contains : FilterOperator.Is,
                Unescape(value),
                exclude
            )
            : null;
    }

    private static List<Filter>? ReadRange(IndexField field, string value, bool exclude)
    {
        Match range = Range().Match(value);
        if (!range.Success)
        {
            return null;
        }

        string low = range.Groups["low"].Value;
        string high = range.Groups["high"].Value;
        bool lowInclusive = range.Groups["open"].Value == "[";
        bool highInclusive = range.Groups["close"].Value == "]";

        if (low == "*" && high == "*")
        {
            // field:[* TO *] is "has a value" on any field; prohibited, it is Missing.
            return [Field(field, FilterOperator.Missing, "", !exclude)];
        }

        if (field.Type is not (FieldType.Number or FieldType.Date))
        {
            return null;
        }

        if (field.Type == FieldType.Number)
        {
            if (low != "*" && high != "*")
            {
                return low == high && lowInclusive && highInclusive
                    ? [Field(field, FilterOperator.Equals, low, exclude)]
                    : null;
            }

            return low == "*"
                ?
                [
                    Field(
                        field,
                        highInclusive ? FilterOperator.AtMost : FilterOperator.LessThan,
                        high,
                        exclude
                    ),
                ]
                :
                [
                    Field(
                        field,
                        lowInclusive ? FilterOperator.AtLeast : FilterOperator.GreaterThan,
                        low,
                        exclude
                    ),
                ];
        }

        // Dates: the compiler's shapes only, [d TO d+1} (on), [d TO *] (after), [* TO d} (before).
        if (low != "*" && high != "*")
        {
            return lowInclusive && !highInclusive && IsNextDay(low, high)
                ? [Field(field, FilterOperator.On, low, exclude)]
                : null;
        }

        if (low == "*")
        {
            return !highInclusive && IsDay(high)
                ? [Field(field, FilterOperator.Before, high, exclude)]
                : null;
        }

        return lowInclusive && IsDay(low)
            ? [Field(field, FilterOperator.After, low, exclude)]
            : null;
    }

    private static Filter Field(IndexField field, FilterOperator op, string value, bool exclude) =>
        new(FilterKind.Field, field.Name, op, value, exclude);

    private static bool IsDay(string value) =>
        DateOnly.TryParseExact(
            value,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out _
        );

    private static bool IsNextDay(string low, string high) =>
        DateOnly.TryParseExact(
            low,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out DateOnly from
        )
        && DateOnly.TryParseExact(
            high,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out DateOnly to
        )
        && to == from.AddDays(1);

    // "+a +b" inside a group: every member a required plain word.
    private static string[]? RequiredWords(string inner)
    {
        List<string>? members = SplitTopLevel(inner);
        if (members is null || members.Count == 0)
        {
            return null;
        }

        string[] words =
        [
            .. members.Where(member => member.StartsWith('+')).Select(member => member[1..]),
        ];
        return words.Length == members.Count && words.All(IsPlainWord)
            ? [.. words.Select(Unescape)]
            : null;
    }

    // A word with no Lucene syntax left in it once escapes are resolved; a trailing * is a prefix.
    private static bool IsPlainWord(string word) =>
        word.Length > 0
        && PlainWord().IsMatch(word)
        && word is not ("AND" or "OR" or "NOT" or "TO");

    private static string Unescape(string value) => EscapedChar().Replace(value, "$1");

    private static string Unquote(string value) =>
        value[1..^1]
            .Replace("\\\"", "\"", StringComparison.Ordinal)
            .Replace("\\\\", "\\", StringComparison.Ordinal);

    // Splits on whitespace outside quotes, brackets and parentheses; null when they do not balance.
    private static List<string>? SplitTopLevel(string text)
    {
        var parts = new List<string>();
        var current = new StringBuilder();
        int depth = 0;
        bool quoted = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\\' && i + 1 < text.Length)
            {
                current.Append(c).Append(text[++i]);
                continue;
            }

            if (c == '"')
            {
                quoted = !quoted;
            }
            else if (!quoted && c is '(' or '[' or '{')
            {
                depth++;
            }
            else if (!quoted && c is ')' or ']' or '}')
            {
                depth--;
                if (depth < 0)
                {
                    return null;
                }
            }

            if (char.IsWhiteSpace(c) && depth == 0 && !quoted)
            {
                if (current.Length > 0)
                {
                    parts.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            current.Append(c);
        }

        if (depth != 0 || quoted)
        {
            return null;
        }

        if (current.Length > 0)
        {
            parts.Add(current.ToString());
        }

        return parts;
    }

    [GeneratedRegex(
        """^(?<field>[A-Za-z_][\w.\-]*):(?<value>.+)$""",
        RegexOptions.CultureInvariant
    )]
    private static partial Regex FieldClause();

    [GeneratedRegex(
        """^(?<open>[\[{])(?<low>\S+) TO (?<high>\S+)(?<close>[\]}])$""",
        RegexOptions.CultureInvariant
    )]
    private static partial Regex Range();

    [GeneratedRegex(
        """^(?:\\.|[^\s+\-!(){}\[\]^"~*?:\\/&|])+\*?$""",
        RegexOptions.CultureInvariant
    )]
    private static partial Regex PlainWord();

    [GeneratedRegex("""\\(.)""", RegexOptions.CultureInvariant)]
    private static partial Regex EscapedChar();

    [GeneratedRegex("""~\d?$""", RegexOptions.CultureInvariant)]
    private static partial Regex FuzzySuffix();
}
