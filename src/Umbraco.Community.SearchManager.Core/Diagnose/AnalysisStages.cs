using System.Text.RegularExpressions;

namespace Umbraco.Community.SearchManager.Core.Diagnose;

/// <summary>
/// Marks what each analyzer stage did to the tokens of the stage before it (ADR 0023). Tokens are
/// matched across stages by their place in the original text (offsets), which every Lucene filter
/// carries through unchanged.
/// </summary>
public static class AnalysisStages
{
    /// <summary>Marks every stage's tokens.</summary>
    /// <param name="stages">The stages as the token streams emitted them, tokenizer first.</param>
    /// <returns>
    /// One stage per input stage. A token of stage k-1 whose text span no token of stage k
    /// overlaps is listed in stage k as <see cref="TokenState.Removed"/>, and in no later stage.
    /// </returns>
    public static IReadOnlyList<AnalysisStage> Mark(IReadOnlyList<RawStage> stages)
    {
        ArgumentNullException.ThrowIfNull(stages);
        var marked = new List<AnalysisStage>(stages.Count);
        AnalysisToken[]? previous = null;

        foreach (RawStage stage in stages)
        {
            AnalysisToken[] current = Positioned(stage.Tokens);
            if (previous is null)
            {
                marked.Add(new AnalysisStage(stage.Name, stage.Detail, current));
                previous = current;
                continue;
            }

            AnalysisToken[] before = previous;
            var tokens = current
                .Select(token =>
                    token with
                    {
                        State = before.Where(old => Overlaps(old, token)).ToArray() switch
                        {
                            [] => TokenState.Added,
                            var olds
                                when olds.Any(old =>
                                    old.Text == token.Text && SameSpan(old, token)
                                ) => TokenState.Kept,
                            _ => TokenState.Changed,
                        },
                    }
                )
                .ToList();

            // Removed tokens slot in by their place in the text; a later stage starts from what
            // this one emitted, so they are not carried on.
            tokens.AddRange(
                before
                    .Where(old => !current.Any(token => Overlaps(old, token)))
                    .Select(old => old with { State = TokenState.Removed })
            );
            marked.Add(
                new AnalysisStage(
                    stage.Name,
                    stage.Detail,
                    [.. tokens.OrderBy(token => token.StartOffset).ThenBy(token => token.Position)]
                )
            );
            previous = current;
        }

        return marked;
    }

    /// <summary>Turns position increments into positions, the first token at 0.</summary>
    /// <param name="tokens">The stage's tokens.</param>
    /// <returns>The tokens, all <see cref="TokenState.Kept"/> until compared.</returns>
    private static AnalysisToken[] Positioned(IReadOnlyList<RawToken> tokens)
    {
        int position = -1;
        return
        [
            .. tokens.Select(token =>
            {
                position += token.PositionIncrement;
                return new AnalysisToken(
                    token.Text,
                    Math.Max(position, 0),
                    token.StartOffset,
                    token.EndOffset,
                    TokenState.Kept,
                    TokenMatch.None
                );
            }),
        ];
    }

    /// <summary>Whether two tokens come from overlapping text; an empty span overlaps only itself.</summary>
    private static bool Overlaps(AnalysisToken a, AnalysisToken b) =>
        SameSpan(a, b) || (a.StartOffset < b.EndOffset && b.StartOffset < a.EndOffset);

    /// <summary>Whether two tokens come from exactly the same text.</summary>
    private static bool SameSpan(AnalysisToken a, AnalysisToken b) =>
        a.StartOffset == b.StartOffset && a.EndOffset == b.EndOffset;
}

/// <summary>
/// Marks the final tokens a search's clauses look for, so the view can highlight them. Display
/// only: whether the search matches is decided by running the engine's query (ADR 0023).
/// </summary>
public static class SearchTermMatcher
{
    /// <summary>Marks the tokens of the final stage that any part looks for.</summary>
    /// <param name="final">The last stage: what the index stores.</param>
    /// <param name="parts">The search's clauses.</param>
    /// <returns>
    /// The stage with <see cref="AnalysisToken.Match"/> set. Comparisons are ordinal, as Lucene
    /// compares terms; removed tokens, fuzzy and other clauses are never marked.
    /// </returns>
    public static AnalysisStage Highlight(AnalysisStage final, IReadOnlyList<SearchTermPart> parts)
    {
        ArgumentNullException.ThrowIfNull(final);
        ArgumentNullException.ThrowIfNull(parts);
        Regex[] patterns =
        [
            .. parts
                .Where(part => part.Kind == SearchTermPartKind.Wildcard)
                .Select(part => Pattern(part.Text)),
        ];

        return final with
        {
            Tokens =
            [
                .. final.Tokens.Select(token =>
                    token.State == TokenState.Removed
                        ? token
                        : token with
                        {
                            Match = MatchOf(token.Text, parts, patterns),
                        }
                ),
            ],
        };
    }

    /// <summary>The strongest match any part has on a term: exact, then prefix, then pattern.</summary>
    private static TokenMatch MatchOf(
        string term,
        IReadOnlyList<SearchTermPart> parts,
        Regex[] patterns
    )
    {
        bool exact = parts.Any(part =>
            part.Kind switch
            {
                SearchTermPartKind.Word or SearchTermPartKind.Prefix => part.Text == term,
                SearchTermPartKind.Phrase => part
                    .Text.Split(' ')
                    .Contains(term, StringComparer.Ordinal),
                _ => false,
            }
        );
        if (exact)
        {
            return TokenMatch.Exact;
        }

        if (
            parts.Any(part =>
                part.Kind == SearchTermPartKind.Prefix
                && term.StartsWith(part.Text, StringComparison.Ordinal)
            )
        )
        {
            return TokenMatch.Prefix;
        }

        return patterns.Any(pattern => pattern.IsMatch(term))
            ? TokenMatch.Pattern
            : TokenMatch.None;
    }

    /// <summary>A Lucene wildcard (<c>*</c> any run, <c>?</c> one character) as a whole-term regex.</summary>
    private static Regex Pattern(string wildcard) =>
        new(
            "^"
                + Regex
                    .Escape(wildcard)
                    .Replace(@"\*", ".*", StringComparison.Ordinal)
                    .Replace(@"\?", ".", StringComparison.Ordinal)
                + "$",
            RegexOptions.CultureInvariant | RegexOptions.Singleline,
            TimeSpan.FromMilliseconds(100)
        );
}
