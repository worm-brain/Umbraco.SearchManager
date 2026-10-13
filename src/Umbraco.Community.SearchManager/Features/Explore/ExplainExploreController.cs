using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Community.SearchManager.Core.Explore;
using Umbraco.Community.SearchManager.Features.Indexes;

namespace Umbraco.Community.SearchManager.Features.Explore;

/// <summary>
/// <c>POST /explore/{target}/explain</c>: why one result scored as it did, or two side by side
/// for comparing, under the query the same search runs (ADR 0020). On request only: explaining
/// costs a search per id.
/// </summary>
/// <param name="registry">Finds targets and their sources.</param>
/// <param name="memberIndexAccess">Decides who may see member data.</param>
public sealed class ExplainExploreController(
    IExploreSourceRegistry registry,
    IMemberIndexAccess memberIndexAccess
) : ExploreControllerBase(registry, memberIndexAccess)
{
    /// <summary>The most documents one request explains: one, or two to compare.</summary>
    public const int MaxIds = 2;

    /// <summary>Explains scores.</summary>
    /// <param name="target">The index or searcher name.</param>
    /// <param name="request">The chips, culture or raw query the search sent, and the ids.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>
    /// One explanation per id; 400 for no or more than two ids, a target whose scores cannot be
    /// explained or a raw query that does not parse, 404 for an unknown target, 403 for member
    /// data without Members access.
    /// </returns>
    [HttpPost("explore/{target}/explain")]
    [ProducesResponseType<ExplainResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ExplainExplore(
        string target,
        ExplainRequest request,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        if ((request.Ids?.Count ?? 0) is < 1 or > MaxIds)
        {
            return Problem400("Invalid request", $"Explain 1 to {MaxIds} documents.");
        }

        TargetLookup lookup = await FindTargetAsync(target, cancellationToken);
        if (lookup.Error is not null)
        {
            return lookup.Error;
        }

        try
        {
            return Ok(
                await lookup.Source!.ExplainAsync(
                    lookup.Target!,
                    request with
                    {
                        Filters = request.Filters ?? [],
                    },
                    cancellationToken
                )
            );
        }
        catch (NotSupportedException exception)
        {
            return Problem400("Scores not available", exception.Message);
        }
        catch (InvalidNativeQueryException exception)
        {
            return Problem400("Invalid Lucene query", exception.Message);
        }
    }

    private BadRequestObjectResult Problem400(string title, string detail) =>
        BadRequest(
            new ProblemDetails
            {
                Title = title,
                Detail = detail,
                Status = StatusCodes.Status400BadRequest,
            }
        );
}
