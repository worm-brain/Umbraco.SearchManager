using System.Reflection;
using Lucene.Net.Analysis;
using Lucene.Net.Analysis.TokenAttributes;
using Umbraco.Community.SearchManager.Core.Diagnose;

namespace Umbraco.Community.SearchManager.Features.Diagnose.Analysis;

/// <summary>
/// Reads what each stage of a Lucene analyzer (its tokenizer, then each filter) emits for a text
/// (ADR 0023). Public so source packages (the Umbraco Search adapter) read stages the same way.
/// </summary>
/// <remarks>
/// <para>
/// Lucene keeps no public list of an analyzer's stages: <c>CreateComponents</c> is protected, and
/// the only link from a filter to the stream before it is the protected <c>TokenFilter.m_input</c>,
/// read here by reflection (the one reflective read; its tests pin the name). When the chain holds
/// anything else, <see cref="TryRead"/> gives up and <see cref="ReadOutput"/> shows the output only.
/// </para>
/// <para>
/// Each stage is read by asking the analyzer for its stream again, which on the same thread hands
/// back the same reused components with the new text, and consuming the chain only up to that
/// stage. Analyzers keep their components per thread, so both methods are synchronous and must
/// not be split across an <c>await</c>. Char filters (none in Umbraco's or Examine's analyzers)
/// change the text before the tokenizer and are not shown.
/// </para>
/// </remarks>
public static class TokenStreamStages
{
    /// <summary>The protected link from a filter to the stream it reads.</summary>
    private static readonly FieldInfo? Input = typeof(TokenFilter).GetField(
        "m_input",
        BindingFlags.NonPublic | BindingFlags.Instance
    );

    /// <summary>Reads every stage of the analyzer's chain for one field.</summary>
    /// <param name="analyzer">
    /// The analyzer, such as an index's per-field analyzer, which dispatches on <paramref name="field"/>.
    /// It is reused, never disposed.
    /// </param>
    /// <param name="field">The field the text is analysed for.</param>
    /// <param name="text">The text.</param>
    /// <param name="maxTokens">Most tokens read per stage.</param>
    /// <param name="truncated">Whether a stage had more tokens than <paramref name="maxTokens"/>.</param>
    /// <returns>
    /// The stages, tokenizer first; null when the chain cannot be walked (a link that is neither a
    /// filter nor a tokenizer, the link field missing in this Lucene version) or reading failed.
    /// </returns>
    public static IReadOnlyList<RawStage>? TryRead(
        Analyzer analyzer,
        string field,
        string text,
        int maxTokens,
        out bool truncated
    )
    {
        ArgumentNullException.ThrowIfNull(analyzer);
        truncated = false;
        if (Input is null)
        {
            return null;
        }

        TokenStream outermost = analyzer.GetTokenStream(field, text);
        try
        {
            List<TokenStream>? chain = Chain(outermost);
            if (chain is null)
            {
                return null;
            }

            var stages = new List<RawStage>(chain.Count);
            bool first = true;
            foreach (TokenStream stage in chain)
            {
                // The first stream came back with the text already set; later stages ask again,
                // which resets the shared tokenizer onto the text.
                if (!first)
                {
                    analyzer.GetTokenStream(field, text);
                }

                first = false;
                stages.Add(
                    new RawStage(NameOf(stage), null, Consume(stage, maxTokens, ref truncated))
                );
            }

            return stages;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // A filter that misbehaves when read part-way: the output alone still shows.
            Release(outermost);
            return null;
        }
    }

    /// <summary>Reads only what the analyzer finally emits, for chains <see cref="TryRead"/> cannot walk.</summary>
    /// <param name="analyzer">The analyzer; reused, never disposed.</param>
    /// <param name="field">The field the text is analysed for.</param>
    /// <param name="text">The text.</param>
    /// <param name="maxTokens">Most tokens read.</param>
    /// <param name="truncated">Whether there were more tokens than <paramref name="maxTokens"/>.</param>
    /// <returns>One stage named after the analyzer.</returns>
    public static RawStage ReadOutput(
        Analyzer analyzer,
        string field,
        string text,
        int maxTokens,
        out bool truncated
    )
    {
        ArgumentNullException.ThrowIfNull(analyzer);
        truncated = false;
        TokenStream stream = analyzer.GetTokenStream(field, text);
        return new RawStage(
            analyzer.GetType().Name,
            null,
            Consume(stream, maxTokens, ref truncated)
        );
    }

    /// <summary>The stages from the tokenizer out to <paramref name="outermost"/>.</summary>
    /// <param name="outermost">The stream the analyzer returned.</param>
    /// <returns>The chain, tokenizer first; null when a link is neither a filter nor a tokenizer.</returns>
    private static List<TokenStream>? Chain(TokenStream outermost)
    {
        var chain = new List<TokenStream>();
        TokenStream? link = outermost;
        while (link is TokenFilter filter)
        {
            chain.Add(filter);
            link = Input!.GetValue(filter) as TokenStream;
        }

        if (link is not Tokenizer tokenizer)
        {
            return null;
        }

        chain.Add(tokenizer);
        chain.Reverse();
        return chain;
    }

    /// <summary>
    /// Reads a stage through the token stream contract (reset, increment, end, dispose); disposing
    /// a stage closes the tokenizer under it, so the analyzer can hand the components out again.
    /// </summary>
    /// <param name="stage">The stage.</param>
    /// <param name="maxTokens">Most tokens read.</param>
    /// <param name="truncated">Set when the stage had more.</param>
    /// <returns>The tokens.</returns>
    private static List<RawToken> Consume(TokenStream stage, int maxTokens, ref bool truncated)
    {
        // The chain shares one attribute source, so these are the attributes every stage writes.
        ICharTermAttribute term = stage.AddAttribute<ICharTermAttribute>();
        IPositionIncrementAttribute increment = stage.AddAttribute<IPositionIncrementAttribute>();
        IOffsetAttribute offset = stage.AddAttribute<IOffsetAttribute>();
        var tokens = new List<RawToken>();
        try
        {
            stage.Reset();
            while (stage.IncrementToken())
            {
                if (tokens.Count == maxTokens)
                {
                    truncated = true;
                    break;
                }

                tokens.Add(
                    new RawToken(
                        term.ToString(),
                        increment.PositionIncrement,
                        offset.StartOffset,
                        offset.EndOffset
                    )
                );
            }

            stage.End();
        }
        finally
        {
            stage.Dispose();
        }

        return tokens;
    }

    /// <summary>Closes a stream after a failed read so the analyzer stays usable on this thread.</summary>
    /// <param name="stream">The outermost stream.</param>
    private static void Release(TokenStream stream)
    {
        try
        {
            stream.Dispose();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Already closed, or broken beyond use: nothing more to do.
        }
    }

    /// <summary>A stage's name for the view: its type name, without a nesting type's name.</summary>
    /// <param name="stage">The stage.</param>
    /// <returns>For example <c>StopFilter</c> or <c>LetterOrDigitTokenizer</c>.</returns>
    private static string NameOf(TokenStream stage) => stage.GetType().Name;
}
