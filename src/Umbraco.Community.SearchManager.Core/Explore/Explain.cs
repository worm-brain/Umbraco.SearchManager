using System.Text.Json.Serialization;

namespace Umbraco.Community.SearchManager.Core.Explore;

/// <summary>A request to explain why one or two documents scored as they did (ADR 0020).</summary>
/// <param name="Filters">The chips the search sent; indexes in the answer refer to this list.</param>
/// <param name="Culture">The culture picked, or null.</param>
/// <param name="Native">The raw query in native mode, or null to explain the chips.</param>
/// <param name="Ids">The document ids to explain: one, or two to compare.</param>
public sealed record ExplainRequest(
    IReadOnlyList<Filter> Filters,
    string? Culture,
    string? Native,
    IReadOnlyList<string> Ids
);

/// <summary>The explanations asked for, in the order of the request's ids.</summary>
/// <param name="Documents">One explanation per id.</param>
public sealed record ExplainResult(IReadOnlyList<DocumentExplanation> Documents);

/// <summary>Why one document scored as it did.</summary>
/// <param name="Id">The document id asked for.</param>
/// <param name="Found">Whether the index has the document at all.</param>
/// <param name="Matched">Whether the document still matches the query; false when it has changed since the search.</param>
/// <param name="Score">The score the query gives it, when it matches.</param>
/// <param name="Root">The explanation, when it matches.</param>
/// <param name="MustNotMatch">
/// Indexes of the chips that exclude documents. They add no score, and a matching document's
/// explanation never mentions them, so they are listed from the chips; empty in native mode.
/// </param>
public sealed record DocumentExplanation(
    string Id,
    bool Found,
    bool Matched,
    float? Score,
    ExplainNode? Root,
    IReadOnlyList<int> MustNotMatch
);

/// <summary>What one node of a score explanation stands for.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ExplainNodeKind>))]
public enum ExplainNodeKind
{
    /// <summary>The score is the sum of the children: every clause that matched adds its part.</summary>
    Sum,

    /// <summary>The child's score scaled by the share of optional clauses that matched (<see cref="ExplainFactors.Matched"/> of <see cref="ExplainFactors.Of"/>).</summary>
    Coord,

    /// <summary>A word found in a field, scored by TF-IDF.</summary>
    TermMatch,

    /// <summary>A phrase found in a field, scored by TF-IDF over its words.</summary>
    PhraseMatch,

    /// <summary>A prefix, wildcard or range clause on a text field: every match gets the same score.</summary>
    Constant,

    /// <summary>The query matches every document, so every document scores the same.</summary>
    MatchAll,

    /// <summary>
    /// A clause on a keyword, number or date field, or a range: it decides which documents match;
    /// in Lucene it still adds to the score (<see cref="ExplainNode.Value"/>).
    /// </summary>
    Filter,

    /// <summary>A clause the document does not meet.</summary>
    NoMatch,

    /// <summary>Anything else, described in Lucene's own words.</summary>
    Other,
}

/// <summary>The numbers TF-IDF multiplies together for a match, as far as the engine reports them.</summary>
/// <param name="Freq">How many times the word or phrase occurs in the field.</param>
/// <param name="Tf">Term frequency weight: the square root of <paramref name="Freq"/>.</param>
/// <param name="Idf">Inverse document frequency weight: higher for rarer words.</param>
/// <param name="DocFreq">Documents with the word.</param>
/// <param name="MaxDocs">Documents in the index.</param>
/// <param name="FieldNorm">Length weight: higher for shorter fields.</param>
/// <param name="QueryNorm">The normalisation shared by every clause of the query.</param>
/// <param name="Boost">The clause's boost, when it has one.</param>
/// <param name="Matched">For coord: optional clauses that matched.</param>
/// <param name="Of">For coord: optional clauses in the query.</param>
public sealed record ExplainFactors(
    float? Freq = null,
    float? Tf = null,
    float? Idf = null,
    long? DocFreq = null,
    long? MaxDocs = null,
    float? FieldNorm = null,
    float? QueryNorm = null,
    float? Boost = null,
    int? Matched = null,
    int? Of = null
);

/// <summary>One node of a score explanation, read from the engine's explanation tree.</summary>
/// <param name="Kind">What the node stands for.</param>
/// <param name="Value">Its part of the score.</param>
/// <param name="Field">The field it matched in, for matches, constants and filters.</param>
/// <param name="Term">The word, phrase, prefix or range it matched, for matches, constants and filters.</param>
/// <param name="Factors">The TF-IDF factors, for matches, constants, coord and match-all.</param>
/// <param name="FilterIndex">For a filter: the index of the chip it came from, when one fits.</param>
/// <param name="Description">The engine's own description, for anything the reader did not recognise.</param>
/// <param name="Children">The nodes it is made of.</param>
public sealed record ExplainNode(
    ExplainNodeKind Kind,
    float Value,
    string? Field,
    string? Term,
    ExplainFactors? Factors,
    int? FilterIndex,
    string Description,
    IReadOnlyList<ExplainNode> Children
);

/// <summary>
/// An engine's explanation as it reports it (Lucene's <c>Explanation</c>), copied into a
/// dependency-free tree for <see cref="ScoreExplanationReader"/>.
/// </summary>
/// <param name="Value">The node's value.</param>
/// <param name="Description">The node's description.</param>
/// <param name="IsMatch">Whether the node matched.</param>
/// <param name="Details">The nodes it is made of.</param>
public sealed record RawExplanation(
    float Value,
    string Description,
    bool IsMatch,
    IReadOnlyList<RawExplanation> Details
);
