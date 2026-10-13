using System.Globalization;
using Umbraco.Community.SearchManager.Core.Explore;

namespace Umbraco.Community.SearchManager.Core.Tests.Explore;

/// <summary>
/// Reads Lucene's <c>Explanation.ToString()</c> text back into a <see cref="RawExplanation"/>, so
/// fixtures are pasted as Lucene printed them (two spaces per level, <c>value = description</c>,
/// complex nodes prefixed <c>(MATCH)</c> or <c>(NON-MATCH)</c>).
/// </summary>
internal static class LuceneExplanationText
{
    /// <summary>Parses the text of one explanation.</summary>
    /// <param name="text">The explanation as Lucene printed it.</param>
    /// <returns>The tree.</returns>
    public static RawExplanation Parse(string text)
    {
        string[] lines = text.Replace("\r", "", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);
        int position = 0;
        return Read(lines, ref position, 0);
    }

    private static RawExplanation Read(string[] lines, ref int position, int depth)
    {
        string line = lines[position++].Substring(depth * 2);
        int equals = line.IndexOf(" = ", StringComparison.Ordinal);
        float value = float.Parse(line[..equals], CultureInfo.InvariantCulture);
        string description = line[(equals + 3)..];

        bool isMatch = value > 0;
        if (description.StartsWith("(MATCH) ", StringComparison.Ordinal))
        {
            (isMatch, description) = (true, description["(MATCH) ".Length..]);
        }
        else if (description.StartsWith("(NON-MATCH) ", StringComparison.Ordinal))
        {
            (isMatch, description) = (false, description["(NON-MATCH) ".Length..]);
        }

        var details = new List<RawExplanation>();
        while (position < lines.Length && Depth(lines[position]) > depth)
        {
            details.Add(Read(lines, ref position, depth + 1));
        }

        return new RawExplanation(value, description, isMatch, details);
    }

    private static int Depth(string line) => (line.Length - line.TrimStart(' ').Length) / 2;
}
