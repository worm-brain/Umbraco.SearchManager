using System.Text.Json.Serialization;
using Umbraco.Community.SearchManager.Core.Explore;

namespace Umbraco.Community.SearchManager.Core.Diagnose;

/// <summary>One culture of a content node, for comparing with what an index holds.</summary>
/// <param name="Iso">The ISO culture code, as Umbraco stores it (<c>en-US</c>).</param>
/// <param name="Published">Whether the culture is published.</param>
/// <param name="Draft">Whether the culture has changes that are not published.</param>
/// <param name="UpdateDate">When the culture was last saved, when known.</param>
/// <param name="PublishDate">When the culture was last published, when it is.</param>
public sealed record NodeCulture(
    string Iso,
    bool Published,
    bool Draft,
    DateTime? UpdateDate,
    DateTime? PublishDate
);

/// <summary>What Umbraco knows about a content node, to explain where it is indexed (ADR 0019).</summary>
/// <param name="Id">The node id.</param>
/// <param name="Key">The node key.</param>
/// <param name="Name">The node name.</param>
/// <param name="EntityType">The Umbraco entity type, <c>document</c>.</param>
/// <param name="ContentTypeAlias">The document type alias.</param>
/// <param name="Path">The node's path of ids, such as <c>-1,1060,1061</c>.</param>
/// <param name="Published">Whether the node (or, when it varies, any culture) is published.</param>
/// <param name="Trashed">Whether the node is in the recycle bin.</param>
/// <param name="Protected">Whether public access restricts the node.</param>
/// <param name="UpdateDate">When the node was last saved.</param>
/// <param name="PublishDate">When the node was last published, when it is.</param>
/// <param name="Cultures">The node's cultures; empty when it does not vary by culture.</param>
public sealed record NodeFacts(
    int Id,
    Guid Key,
    string Name,
    string EntityType,
    string ContentTypeAlias,
    string Path,
    bool Published,
    bool Trashed,
    bool Protected,
    DateTime UpdateDate,
    DateTime? PublishDate,
    IReadOnlyList<NodeCulture> Cultures
)
{
    /// <summary>Whether the node sits under another node, from its path.</summary>
    /// <param name="ancestorId">The other node's id.</param>
    /// <returns><see langword="true"/> when the path passes through it, or it is the node itself.</returns>
    public bool IsUnder(int ancestorId) =>
        Path.Split(',')
            .Contains(ancestorId.ToString(System.Globalization.CultureInfo.InvariantCulture));
}

/// <summary>A node found by name, id or key, for the candidate list.</summary>
/// <param name="Id">The node id.</param>
/// <param name="Key">The node key.</param>
/// <param name="Name">The node name.</param>
/// <param name="Icon">The document type's icon, when known.</param>
/// <param name="Trashed">Whether it is in the recycle bin.</param>
public sealed record NodeCandidate(int Id, Guid Key, string Name, string? Icon, bool Trashed);

/// <summary>Whether an index holds a node.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PresenceStatus>))]
public enum PresenceStatus
{
    /// <summary>The index holds at least one document for the node.</summary>
    Present,

    /// <summary>The index could hold the node but has no document for it.</summary>
    Missing,

    /// <summary>The index never holds a node like this one (other entity types or document types).</summary>
    NotApplicable,
}

/// <summary>Whether one index holds a node, and why not (ADR 0019).</summary>
/// <param name="Target">The index.</param>
/// <param name="Engine">The engine that owns it.</param>
/// <param name="Status">Present, missing or not applicable.</param>
/// <param name="DocumentCount">Documents the index holds for the node (one per culture for some indexes).</param>
/// <param name="Cultures">The cultures of the documents found, when the index records them.</param>
/// <param name="Reason">Why the node is missing or not applicable; null when present.</param>
/// <param name="Stale">
/// Whether the index's copy is older than the node (a save or publish it has not caught up with);
/// null when the index records no date to compare.
/// </param>
/// <param name="IndexUpdateDate">The date the index holds for the node, when it has one.</param>
/// <param name="ExpectedUpdateDate">The date the index should hold, from the node.</param>
/// <param name="ViewFilter">A chip that finds the node's documents in Explore.</param>
/// <param name="ViewDocumentId">The Explore document id to open in the drawer, when there is one.</param>
/// <param name="ViewCulture">The culture to open Explore in, when the index needs one to find the document.</param>
public sealed record IndexPresence(
    string Target,
    string Engine,
    PresenceStatus Status,
    int DocumentCount,
    IReadOnlyList<string> Cultures,
    string? Reason,
    bool? Stale = null,
    DateTime? IndexUpdateDate = null,
    DateTime? ExpectedUpdateDate = null,
    Filter? ViewFilter = null,
    string? ViewDocumentId = null,
    string? ViewCulture = null
);

/// <summary>A node with the presence of every index the user may see.</summary>
/// <param name="Node">The node.</param>
/// <param name="Indexes">One entry per index.</param>
public sealed record NodePresenceResult(NodeFacts Node, IReadOnlyList<IndexPresence> Indexes);

/// <summary>A request to test which chips of a query match a node.</summary>
/// <param name="Filters">The chips, as the search box parsed them.</param>
/// <param name="Culture">The culture to test in, or null.</param>
public sealed record NodeMatchRequest(IReadOnlyList<Filter> Filters, string? Culture = null);

/// <summary>Whether one chip matches the node.</summary>
/// <param name="Index">The chip's position in the request.</param>
/// <param name="Matched">Whether the node matches the chip on its own; null when the chip was skipped.</param>
/// <param name="SkippedReason">Why the engine could not run the chip; null when it ran.</param>
/// <param name="Field">The field the chip tested, as the index names it, when it tests one.</param>
/// <param name="NodeValues">The node's stored values of that field in the index, when it has any.</param>
public sealed record ClauseMatch(
    int Index,
    bool? Matched,
    string? SkippedReason,
    string? Field,
    IReadOnlyList<string> NodeValues
);

/// <summary>Whether a query finds a node, chip by chip (ADR 0019).</summary>
/// <param name="InIndex">Whether the index holds the node at all; when not, nothing can match.</param>
/// <param name="Matches">Whether the whole query finds the node.</param>
/// <param name="Query">The query the engine ran, in its own language.</param>
/// <param name="Clauses">One entry per chip.</param>
public sealed record NodeMatchResult(
    bool InIndex,
    bool Matches,
    string Query,
    IReadOnlyList<ClauseMatch> Clauses
);

/// <summary>
/// One engine's side of "Find a node" (ADR 0019): where a node is indexed, and which chips of a
/// query match it. Keyed by <see cref="ExploreTarget.Engine"/> so Explore's targets are reused.
/// </summary>
public interface INodePresenceSource
{
    /// <summary>The engine id, matching <see cref="ExploreTarget.Engine"/>.</summary>
    string Engine { get; }

    /// <summary>Checks each of this engine's targets for a node.</summary>
    /// <param name="node">The node.</param>
    /// <param name="targets">This engine's targets the user may see.</param>
    /// <param name="cancellationToken">Cancels the checks.</param>
    /// <returns>One entry per target the engine can check, in the order given.</returns>
    Task<IReadOnlyList<IndexPresence>> GetPresenceAsync(
        NodeFacts node,
        IReadOnlyList<ExploreTarget> targets,
        CancellationToken cancellationToken
    );

    /// <summary>Tests a query against a node in one target, chip by chip and as a whole.</summary>
    /// <param name="target">A target of this engine.</param>
    /// <param name="node">The node.</param>
    /// <param name="request">The chips and culture.</param>
    /// <param name="cancellationToken">Cancels the test.</param>
    /// <returns>The verdicts.</returns>
    Task<NodeMatchResult> MatchAsync(
        ExploreTarget target,
        NodeFacts node,
        NodeMatchRequest request,
        CancellationToken cancellationToken
    );
}
