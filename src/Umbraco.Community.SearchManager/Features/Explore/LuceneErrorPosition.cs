using System.Globalization;
using System.Text.RegularExpressions;

namespace Umbraco.Community.SearchManager.Features.Explore;

/// <summary>
/// Finds where in a query Lucene's classic parser stopped. The parser wraps its own
/// <c>ParseException</c> or <c>TokenMgrError</c> in a <c>ParseException("Cannot parse ...")</c>;
/// both say "line L, column C". Lines count from 1; columns count from 0, because the parser's
/// <c>FastCharStream</c> numbers them from the start of its buffer (verified against Lucene.NET 4.8).
/// </summary>
internal static partial class LuceneErrorPosition
{
    /// <summary>The zero-based offset of a parse error.</summary>
    /// <param name="query">The query that failed.</param>
    /// <param name="exception">The parser's exception.</param>
    /// <returns>The offset, clamped to the query's length (an error at the end points just past it); null when the parser gave no position.</returns>
    public static int? Find(string query, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(exception);

        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            Match match = LineColumn().Match(current.Message);
            if (!match.Success)
            {
                continue;
            }

            int line = int.Parse(match.Groups["line"].Value, CultureInfo.InvariantCulture);
            int column = int.Parse(match.Groups["column"].Value, CultureInfo.InvariantCulture);
            return Offset(query, line, column);
        }

        return null;
    }

    private static int Offset(string query, int line, int column)
    {
        int offset = 0;
        for (int i = 1; i < line; i++)
        {
            int next = query.IndexOf('\n', offset);
            if (next < 0)
            {
                break;
            }

            offset = next + 1;
        }

        return Math.Clamp(offset + column, 0, query.Length);
    }

    [GeneratedRegex(@"line (?<line>\d+), column (?<column>\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex LineColumn();
}
