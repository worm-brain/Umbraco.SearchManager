using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Community.SearchManager.Core.Diagnose;
using Umbraco.Community.SearchManager.Core.Explore;
using Umbraco.Community.SearchManager.Features.Explore;
using Umbraco.Community.SearchManager.Features.Indexes;

namespace Umbraco.Community.SearchManager.Features.Diagnose.Nodes;

/// <summary><c>GET /diagnose/nodes</c>: finds documents by name, id or key for "Find a node" (ADR 0019).</summary>
/// <param name="registry">Explore's targets (unused here; the base class needs it).</param>
/// <param name="memberIndexAccess">Decides who may see member data.</param>
/// <param name="reader">Finds the documents.</param>
[ApiExplorerSettings(GroupName = "Diagnose")]
public sealed class FindNodesController(
    IExploreSourceRegistry registry,
    IMemberIndexAccess memberIndexAccess,
    INodeFactsReader reader
) : ExploreControllerBase(registry, memberIndexAccess)
{
    /// <summary>The most candidates one request returns.</summary>
    public const int MaxTake = 50;

    /// <summary>Finds documents.</summary>
    /// <param name="query">A node id, a key, or part of a name.</param>
    /// <param name="take">How many candidates, 1 to 50.</param>
    /// <returns>The candidates; 400 for a bad take.</returns>
    [HttpGet("diagnose/nodes")]
    [ProducesResponseType<IReadOnlyList<NodeCandidate>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public IActionResult FindNodes([FromQuery] string? query, [FromQuery] int take = 10)
    {
        if (take is < 1 or > MaxTake)
        {
            return BadRequest(
                new ProblemDetails
                {
                    Title = "Invalid take",
                    Detail = $"Take must be between 1 and {MaxTake}.",
                    Status = StatusCodes.Status400BadRequest,
                }
            );
        }

        return Ok(reader.Find(query ?? string.Empty, take).ToArray());
    }
}

/// <summary>
/// <c>GET /diagnose/nodes/{key}</c>: a document and whether each index the user may see holds it,
/// with the reason when not (ADR 0019).
/// </summary>
/// <param name="registry">Lists the indexes.</param>
/// <param name="memberIndexAccess">Decides who may see member indexes.</param>
/// <param name="reader">Reads the document.</param>
/// <param name="sources">The presence source of each engine.</param>
[ApiExplorerSettings(GroupName = "Diagnose")]
public sealed class GetNodePresenceController(
    IExploreSourceRegistry registry,
    IMemberIndexAccess memberIndexAccess,
    INodeFactsReader reader,
    IEnumerable<INodePresenceSource> sources
) : ExploreControllerBase(registry, memberIndexAccess)
{
    private readonly INodePresenceSource[] _sources = [.. sources];

    /// <summary>Checks every index for a document.</summary>
    /// <param name="key">The document key.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The document and one entry per index; 404 when there is no such document.</returns>
    [HttpGet("diagnose/nodes/{key:guid}")]
    [ProducesResponseType<NodePresenceResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetNodePresence(Guid key, CancellationToken cancellationToken)
    {
        NodeFacts? node = reader.Read(key);
        if (node is null)
        {
            return NodeResponses.NotFound(key);
        }

        bool members = await CanSeeMembersAsync();
        IReadOnlyList<ExploreTarget> targets = await Registry.GetTargetsAsync(cancellationToken);
        var presences = new List<IndexPresence>();
        foreach (
            IGrouping<string, ExploreTarget> engine in targets
                .Where(target =>
                    target.Kind == ExploreTargetKind.Index && (members || !target.IsMemberData)
                )
                .GroupBy(target => target.Engine, StringComparer.Ordinal)
        )
        {
            INodePresenceSource? source = _sources.FirstOrDefault(candidate =>
                candidate.Engine == engine.Key
            );
            if (source is not null)
            {
                presences.AddRange(
                    await source.GetPresenceAsync(node, [.. engine], cancellationToken)
                );
            }
        }

        return Ok(new NodePresenceResult(node, presences));
    }
}

/// <summary>
/// <c>POST /diagnose/{target}/nodes/{key}/match</c>: whether a query finds a document in one
/// index, chip by chip (ADR 0019).
/// </summary>
/// <param name="registry">Finds the index.</param>
/// <param name="memberIndexAccess">Decides who may see member data.</param>
/// <param name="reader">Reads the document.</param>
/// <param name="sources">The presence source of each engine.</param>
[ApiExplorerSettings(GroupName = "Diagnose")]
public sealed class MatchNodeController(
    IExploreSourceRegistry registry,
    IMemberIndexAccess memberIndexAccess,
    INodeFactsReader reader,
    IEnumerable<INodePresenceSource> sources
) : ExploreControllerBase(registry, memberIndexAccess)
{
    private readonly INodePresenceSource[] _sources = [.. sources];

    /// <summary>Tests a query against a document.</summary>
    /// <param name="target">The index name.</param>
    /// <param name="key">The document key.</param>
    /// <param name="request">The chips and culture.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>
    /// The verdicts; 400 when the index's engine cannot test queries, 404 for an unknown index or
    /// document, 403 for member data without Members access.
    /// </returns>
    [HttpPost("diagnose/{target}/nodes/{key:guid}/match")]
    [ProducesResponseType<NodeMatchResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MatchNode(
        string target,
        Guid key,
        NodeMatchRequest request,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        TargetLookup lookup = await FindTargetAsync(target, cancellationToken);
        if (lookup.Error is not null)
        {
            return lookup.Error;
        }

        NodeFacts? node = reader.Read(key);
        if (node is null)
        {
            return NodeResponses.NotFound(key);
        }

        ExploreTarget found = lookup.Target!;
        INodePresenceSource? source = _sources.FirstOrDefault(candidate =>
            candidate.Engine == found.Engine
        );
        if (source is null)
        {
            return CannotMatch($"Queries cannot be tested against {found.Engine} indexes.");
        }

        try
        {
            return Ok(await source.MatchAsync(found, node, request, cancellationToken));
        }
        catch (NotSupportedException exception)
        {
            return CannotMatch(exception.Message);
        }
    }

    private BadRequestObjectResult CannotMatch(string detail) =>
        BadRequest(
            new ProblemDetails
            {
                Title = "Cannot test the query",
                Detail = detail,
                Status = StatusCodes.Status400BadRequest,
            }
        );
}

/// <summary>Responses the node endpoints share.</summary>
internal static class NodeResponses
{
    /// <summary>The 404 for a key that names no document.</summary>
    /// <param name="key">The key.</param>
    /// <returns>The response.</returns>
    public static NotFoundObjectResult NotFound(Guid key) =>
        new(
            new ProblemDetails
            {
                Title = "Document not found",
                Detail = $"No document has the key {key}.",
                Status = StatusCodes.Status404NotFound,
            }
        );
}
