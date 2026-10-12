using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Umbraco.Community.SearchManager.Core.Explore;

/// <summary>What <see cref="SimpleSyntaxParser.Parse"/> made of the search box input.</summary>
/// <param name="Filters">One filter per chip, in input order.</param>
/// <param name="Fallback">Why the input was searched as plain text, or null when it parsed.</param>
public sealed record ParseResult(IReadOnlyList<Filter> Filters, ParseFallback? Fallback);

/// <summary>Explains why the input could not be read as filters and became plain text.</summary>
/// <param name="Code">Machine-readable reason, for example <c>unbalanced_quote</c>.</param>
/// <param name="Message">The sentence the UI shows.</param>
public sealed record ParseFallback(string Code, string Message);

/// <summary>
/// Turns Explore search box input into chips (<see cref="Filter"/>s). The only parser of the
/// syntax; the browser calls it on Enter through <c>POST /explore/{target}/parse</c> (ADR 0002,
/// after LogExplorer's ADR 0005).
/// <para>
/// Input splits on whitespace outside double quotes. <c>[-]field:value</c> is a field chip when
/// the field (or an alias) exists; its operator follows the field type and the value's form:
/// <c>&gt;</c>, <c>&gt;=</c>, <c>&lt;</c>, <c>&lt;=</c> on numbers and dates, a quoted value is a
/// phrase on text, a trailing <c>*</c> is "starts with", a trailing <c>~</c> is fuzzy, and
/// <c>field:*</c> means "has a value". A field the schema does not know is kept as a keyword chip
/// on the name as typed, which the UI shows disabled. <c>"a phrase"</c> is a phrase chip,
/// <c>-word</c> an exclude chip, and the remaining bare words share one text chip.
/// </para>
/// </summary>
public static partial class SimpleSyntaxParser
{
    /// <summary><see cref="ParseFallback.Code"/> when a double quote is left open.</summary>
    public const string UnbalancedQuoteCode = "unbalanced_quote";

    /// <summary><see cref="ParseFallback.Message"/> for <see cref="UnbalancedQuoteCode"/>.</summary>
    public const string UnbalancedQuoteMessage =
        "Unbalanced quote, so this was searched as plain text";

    /// <summary>Parses one search box input.</summary>
    /// <param name="input">What the user typed; empty or whitespace yields no chips.</param>
    /// <param name="schema">The fields of the index being searched.</param>
    /// <returns>
    /// The chips in input order. When a quote is left open, nothing is read as a filter: the
    /// result is one text chip of the whole input without its quotes, plus a fallback saying why.
    /// </returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static ParseResult Parse(string input, FieldSchema schema)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(schema);

        if (!TryTokenise(input, out List<Token> tokens))
        {
            string text = input.Replace("\"", "", StringComparison.Ordinal).Trim();
            return new ParseResult(
                text.Length == 0
                    ? []
                    : [new Filter(FilterKind.Text, null, FilterOperator.Contains, text)],
                new ParseFallback(UnbalancedQuoteCode, UnbalancedQuoteMessage)
            );
        }

        var filters = new List<Filter>();
        var words = new List<string>();
        int textChipIndex = -1;

        foreach (Token token in tokens)
        {
            Filter? filter = ReadPhrase(token) ?? ReadField(token, schema);
            if (filter is not null)
            {
                if (filter.Value.Length > 0 || filter.Op == FilterOperator.Missing)
                {
                    filters.Add(filter);
                }

                continue;
            }

            string word = token.Text;
            if (word.Length > 1 && word[0] == '-')
            {
                filters.Add(
                    new Filter(FilterKind.Text, null, FilterOperator.Contains, word[1..], true)
                );
                continue;
            }

            // Bare words AND together in one chip, which sits where the first word was typed.
            if (textChipIndex < 0)
            {
                textChipIndex = filters.Count;
            }

            words.Add(word);
        }

        if (textChipIndex >= 0)
        {
            filters.Insert(
                textChipIndex,
                new Filter(FilterKind.Text, null, FilterOperator.Contains, string.Join(' ', words))
            );
        }

        return new ParseResult(filters, null);
    }

    // "a phrase" or -"a phrase": a token that is one quoted segment, optionally after a minus.
    private static Filter? ReadPhrase(Token token) =>
        token.Segments switch
        {
            [{ Quoted: true } phrase] => new Filter(
                FilterKind.Text,
                null,
                FilterOperator.Phrase,
                phrase.Text
            ),
            [{ Quoted: false, Text: "-" }, { Quoted: true } phrase] => new Filter(
                FilterKind.Text,
                null,
                FilterOperator.Phrase,
                phrase.Text,
                true
            ),
            _ => null,
        };

    private static Filter? ReadField(Token token, FieldSchema schema)
    {
        if (token.Segments[0].Quoted)
        {
            return null;
        }

        Match match = FieldPattern().Match(token.Segments[0].Text);
        if (!match.Success)
        {
            return null;
        }

        // The value is the rest of the first segment plus any later segments, so field:"a b"
        // reads; only a value that is wholly one quoted segment counts as quoted.
        string rest = match.Groups["rest"].Value;
        bool quoted = rest.Length == 0 && token.Segments is [_, { Quoted: true }];
        string value = rest + string.Concat(token.Segments.Skip(1).Select(s => s.Text));
        if (value.Length == 0 && !quoted)
        {
            return null;
        }

        bool exclude = match.Groups["neg"].Success;
        string name = match.Groups["field"].Value;
        IndexField? field = schema.Resolve(name);
        if (field is null)
        {
            return new Filter(FilterKind.Field, name, FilterOperator.Is, value, exclude);
        }

        if (!quoted && value.Length > 0 && value.All(c => c == '*'))
        {
            // field:* is "has a value", the inverse of Missing.
            return new Filter(FilterKind.Field, field.Name, FilterOperator.Missing, "", !exclude);
        }

        (FilterOperator op, string operand) = field.Type switch
        {
            FieldType.Number => ReadNumber(value, quoted),
            FieldType.Date => ReadDate(value, quoted),
            FieldType.Keyword => ReadKeyword(value, quoted),
            _ => ReadText(value, quoted),
        };
        return new Filter(FilterKind.Field, field.Name, op, operand, exclude);
    }

    private static (FilterOperator, string) ReadText(string value, bool quoted)
    {
        if (quoted)
        {
            return (FilterOperator.Phrase, value);
        }

        if (value.Length > 1 && value.EndsWith('*'))
        {
            return (FilterOperator.StartsWith, value.TrimEnd('*'));
        }

        Match fuzzy = FuzzySuffix().Match(value);
        if (fuzzy.Success && fuzzy.Index > 0)
        {
            return (FilterOperator.Fuzzy, value[..fuzzy.Index]);
        }

        return (FilterOperator.Contains, value);
    }

    private static (FilterOperator, string) ReadKeyword(string value, bool quoted) =>
        !quoted && value.Length > 1 && value.EndsWith('*')
            ? (FilterOperator.StartsWith, value.TrimEnd('*'))
            : (FilterOperator.Is, value);

    private static (FilterOperator, string) ReadNumber(string value, bool quoted)
    {
        (string comparison, string operand) = SplitComparison(value, quoted);
        FilterOperator op = comparison switch
        {
            ">" => FilterOperator.GreaterThan,
            ">=" => FilterOperator.AtLeast,
            "<" => FilterOperator.LessThan,
            "<=" => FilterOperator.AtMost,
            _ => FilterOperator.Equals,
        };
        return (op, operand);
    }

    // After includes its day and Before excludes it, so ">" and "<=" move an absolute date on a
    // day to keep their meaning: updated:>2026-01-01 starts on 2 January.
    private static (FilterOperator, string) ReadDate(string value, bool quoted)
    {
        (string comparison, string operand) = SplitComparison(value, quoted);
        return comparison switch
        {
            ">" => (FilterOperator.After, NextDay(operand)),
            ">=" => (FilterOperator.After, operand),
            "<" => (FilterOperator.Before, operand),
            "<=" => (FilterOperator.Before, NextDay(operand)),
            _ => (FilterOperator.On, operand),
        };
    }

    private static string NextDay(string value) =>
        DateOnly.TryParseExact(
            value,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out DateOnly date
        )
            ? date.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : value;

    private static (string Comparison, string Operand) SplitComparison(string value, bool quoted)
    {
        if (quoted)
        {
            return ("", value);
        }

        foreach (string comparison in (string[])[">=", "<=", ">", "<"])
        {
            if (value.StartsWith(comparison, StringComparison.Ordinal))
            {
                return (comparison, value[comparison.Length..]);
            }
        }

        return ("", value);
    }

    // Splits on whitespace outside quotes. Inside quotes \" is a quote and \\ a backslash; any
    // other backslash is literal. Returns false when the input ends inside a quote.
    private static bool TryTokenise(string input, out List<Token> tokens)
    {
        tokens = [];
        int i = 0;
        while (i < input.Length)
        {
            if (char.IsWhiteSpace(input[i]))
            {
                i++;
                continue;
            }

            var segments = new List<Segment>();
            var text = new StringBuilder();
            while (i < input.Length && !char.IsWhiteSpace(input[i]))
            {
                if (input[i] != '"')
                {
                    text.Append(input[i++]);
                    continue;
                }

                if (text.Length > 0)
                {
                    segments.Add(new Segment(text.ToString(), false));
                    text.Clear();
                }

                i++;
                bool closed = false;
                while (i < input.Length)
                {
                    char c = input[i];
                    if (c == '\\' && i + 1 < input.Length && input[i + 1] is '"' or '\\')
                    {
                        text.Append(input[i + 1]);
                        i += 2;
                    }
                    else if (c == '"')
                    {
                        closed = true;
                        i++;
                        break;
                    }
                    else
                    {
                        text.Append(c);
                        i++;
                    }
                }

                if (!closed)
                {
                    return false;
                }

                segments.Add(new Segment(text.ToString(), true));
                text.Clear();
            }

            if (text.Length > 0)
            {
                segments.Add(new Segment(text.ToString(), false));
            }

            tokens.Add(new Token(segments));
        }

        return true;
    }

    // A field name starts with a letter or underscore and may hold letters, digits, _ . and -
    // (culture variants such as nodeName_en-us).
    [GeneratedRegex(
        """^(?<neg>-)?(?<field>[A-Za-z_][\w.\-]*):(?<rest>.*)$""",
        RegexOptions.Singleline | RegexOptions.CultureInvariant
    )]
    private static partial Regex FieldPattern();

    [GeneratedRegex(@"~\d?$", RegexOptions.CultureInvariant)]
    private static partial Regex FuzzySuffix();

    private readonly record struct Segment(string Text, bool Quoted);

    private sealed record Token(IReadOnlyList<Segment> Segments)
    {
        // The token as a bare word: quote characters dropped, escapes resolved.
        public string Text => string.Concat(Segments.Select(s => s.Text));
    }
}
