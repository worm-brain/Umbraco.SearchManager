using System.Text.Json.Serialization;
using Umbraco.Community.SearchManager.Core.Explore;

namespace Umbraco.Community.SearchManager.Core.Diagnose;

/// <summary>A field the analyzer view can run text through.</summary>
/// <param name="Name">The field as the view names it (<c>title</c> for Umbraco Search, not <c>Field_title_texts</c>).</param>
/// <param name="Label">A human name, such as "Name"; null when none.</param>
/// <param name="Type">How the field is indexed: analysed text or a whole-value keyword.</param>
/// <param name="Group">What kind of field it is, so the picker lists the copies last.</param>
/// <param name="Analyzer">The analyzer the index runs over the field's values.</param>
/// <param name="Physical">The field's name in the Lucene index when it differs from <paramref name="Name"/>.</param>
public sealed record AnalyzerField(
    string Name,
    string? Label,
    FieldType Type,
    FieldGroup Group,
    string Analyzer,
    string? Physical
);

/// <summary>The fields of one index the analyzer view can use (ADR 0023).</summary>
/// <param name="Target">The index.</param>
/// <param name="Available">Whether the index's analyzers can be run; when not, <paramref name="UnavailableReason"/> says why.</param>
/// <param name="UnavailableReason">Why not, as a sentence for the view; null when available.</param>
/// <param name="DefaultField">The field to pick first, when the index has it.</param>
/// <param name="Fields">The fields, in the order the picker shows them.</param>
public sealed record AnalyzerFieldsResult(
    ExploreTarget Target,
    bool Available,
    string? UnavailableReason,
    string? DefaultField,
    IReadOnlyList<AnalyzerField> Fields
)
{
    /// <summary>A result saying why a target's analyzers cannot be run.</summary>
    /// <param name="target">The target.</param>
    /// <param name="reason">Why, as a sentence for the view.</param>
    /// <returns>The result.</returns>
    public static AnalyzerFieldsResult Unavailable(ExploreTarget target, string reason) =>
        new(target, false, reason, null, []);
}

/// <summary>Text to run through one field's analyzer, and optionally what a visitor searches for.</summary>
/// <param name="Field">The field, as <see cref="AnalyzerField.Name"/> names it.</param>
/// <param name="Text">The text as an editor would enter it.</param>
/// <param name="SearchTerm">What a visitor types into a search box; null or blank for no verdict.</param>
public sealed record AnalyzeRequest(string Field, string Text, string? SearchTerm);

/// <summary>A token as one stage of a Lucene token stream emitted it.</summary>
/// <param name="Text">The term text.</param>
/// <param name="PositionIncrement">Positions moved since the previous token; more than 1 after a removed word.</param>
/// <param name="StartOffset">Where the token starts in the original text.</param>
/// <param name="EndOffset">Where it ends (exclusive).</param>
public sealed record RawToken(string Text, int PositionIncrement, int StartOffset, int EndOffset);

/// <summary>Everything one stage of an analyzer (its tokenizer or one filter) emitted.</summary>
/// <param name="Name">The stage's type name, such as <c>StopFilter</c>.</param>
/// <param name="Detail">What the stage does when worth saying, such as how many words it removes.</param>
/// <param name="Tokens">The tokens, in order.</param>
public sealed record RawStage(string Name, string? Detail, IReadOnlyList<RawToken> Tokens);

/// <summary>What a stage did to a token compared with the stage before it.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TokenState>))]
public enum TokenState
{
    /// <summary>Passed through unchanged (and every token of the first stage).</summary>
    Kept,

    /// <summary>Same place in the text, different term, such as <c>Café</c> lower-cased to <c>café</c>.</summary>
    Changed,

    /// <summary>Dropped by this stage, such as a stop word; shown only in the stage that dropped it.</summary>
    Removed,

    /// <summary>A new token at a place the previous stage had none, such as a synonym.</summary>
    Added,
}

/// <summary>Whether a final token is one the search term looks for. Display only; never the verdict.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TokenMatch>))]
public enum TokenMatch
{
    /// <summary>The search does not look for it.</summary>
    None,

    /// <summary>The search looks for exactly this term.</summary>
    Exact,

    /// <summary>The search looks for terms starting with a prefix of it.</summary>
    Prefix,

    /// <summary>A wildcard pattern of the search matches it.</summary>
    Pattern,
}

/// <summary>A token as the view shows it.</summary>
/// <param name="Text">The term text.</param>
/// <param name="Position">The token's position (0-based); stop words leave gaps.</param>
/// <param name="StartOffset">Where the token starts in the original text.</param>
/// <param name="EndOffset">Where it ends (exclusive).</param>
/// <param name="State">What this stage did to it.</param>
/// <param name="Match">Whether the search looks for it; set on the final stage only.</param>
public sealed record AnalysisToken(
    string Text,
    int Position,
    int StartOffset,
    int EndOffset,
    TokenState State,
    TokenMatch Match
);

/// <summary>One stage of the analysis, with each token marked.</summary>
/// <param name="Name">The stage's type name.</param>
/// <param name="Detail">What the stage does when worth saying.</param>
/// <param name="Tokens">The stage's tokens, plus the ones it removed, in text order.</param>
public sealed record AnalysisStage(
    string Name,
    string? Detail,
    IReadOnlyList<AnalysisToken> Tokens
);

/// <summary>What kind of clause a search looks for.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SearchTermPartKind>))]
public enum SearchTermPartKind
{
    /// <summary>One whole term.</summary>
    Word,

    /// <summary>Any term starting with the text.</summary>
    Prefix,

    /// <summary>Terms matching a pattern with <c>*</c> and <c>?</c>.</summary>
    Wildcard,

    /// <summary>Several terms next to each other, in order.</summary>
    Phrase,

    /// <summary>Terms within a few edits of the text.</summary>
    Fuzzy,

    /// <summary>Any other clause, shown as Lucene writes it.</summary>
    Other,
}

/// <summary>One clause of the query a search term becomes.</summary>
/// <param name="Kind">What the clause looks for.</param>
/// <param name="Text">The term, prefix, pattern or phrase (terms separated by spaces).</param>
/// <param name="Required">
/// Whether a document must match this clause's group; parts of a group that is not required only
/// rank documents higher.
/// </param>
/// <param name="Group">
/// The top-level clause this part belongs to. Parts of one group are alternatives: any one of them
/// satisfies the group.
/// </param>
public sealed record SearchTermPart(SearchTermPartKind Kind, string Text, bool Required, int Group);

/// <summary>Whether a search term finds the text, decided by running the engine's own query.</summary>
/// <param name="Matches">Whether the query found a document holding only the text.</param>
/// <param name="Parts">The query's clauses, for "The search becomes ...".</param>
/// <param name="Notes">Sentences on the engine's rules that explain a surprising verdict.</param>
public sealed record SearchTermResult(
    bool Matches,
    IReadOnlyList<SearchTermPart> Parts,
    IReadOnlyList<string> Notes
);

/// <summary>Text run through one field's analyzer, stage by stage, and the search verdict (ADR 0023).</summary>
/// <param name="Target">The index.</param>
/// <param name="Available">Whether the text could be analysed; when not, <paramref name="UnavailableReason"/> says why.</param>
/// <param name="UnavailableReason">Why not, as a sentence for the view; null when available.</param>
/// <param name="Field">The field, as the request named it.</param>
/// <param name="Analyzer">The analyzer's name.</param>
/// <param name="StagesShown">Whether every stage is shown; false when only the analyzer's output could be read.</param>
/// <param name="StagesNote">Why only the output is shown; null when every stage is.</param>
/// <param name="Stages">The stages, tokenizer first; the last one is what the index stores.</param>
/// <param name="Search">The verdict for the request's search term; null without one or when not judged.</param>
/// <param name="SearchUnavailableReason">Why a search term was not judged on this field; null otherwise.</param>
/// <param name="Truncated">Whether a stage had more tokens than are shown.</param>
public sealed record AnalysisResult(
    ExploreTarget Target,
    bool Available,
    string? UnavailableReason,
    string Field,
    string? Analyzer,
    bool StagesShown,
    string? StagesNote,
    IReadOnlyList<AnalysisStage> Stages,
    SearchTermResult? Search,
    string? SearchUnavailableReason,
    bool Truncated
)
{
    /// <summary>A result saying why the text could not be analysed.</summary>
    /// <param name="target">The target.</param>
    /// <param name="field">The field requested.</param>
    /// <param name="reason">Why, as a sentence for the view.</param>
    /// <returns>The result.</returns>
    public static AnalysisResult Unavailable(ExploreTarget target, string field, string reason) =>
        new(target, false, reason, field, null, false, null, [], null, null, false);
}

/// <summary>
/// One engine's analyzer view (Diagnose, ADR 0023), keyed by <see cref="ExploreTarget.Engine"/>
/// so Explore's targets are reused. An engine without one gets a reason instead of an error.
/// </summary>
public interface IAnalyzerSource
{
    /// <summary>The engine id, matching <see cref="ExploreTarget.Engine"/>.</summary>
    string Engine { get; }

    /// <summary>Lists the fields of a target whose analyzer can be run.</summary>
    /// <param name="target">A target of this engine.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The fields, or an unavailable result with the reason.</returns>
    Task<AnalyzerFieldsResult> GetFieldsAsync(
        ExploreTarget target,
        CancellationToken cancellationToken
    );

    /// <summary>Runs text through a field's analyzer and judges the search term.</summary>
    /// <param name="target">A target of this engine.</param>
    /// <param name="request">The field, text and search term.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The analysis, or an unavailable result with the reason (such as a number field).</returns>
    Task<AnalysisResult> AnalyzeAsync(
        ExploreTarget target,
        AnalyzeRequest request,
        CancellationToken cancellationToken
    );
}
