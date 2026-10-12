using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Community.SearchManager.Core.Explore;
using Umbraco.Community.SearchManager.Features.Indexes;

namespace Umbraco.Community.SearchManager.Features.Explore;

/// <summary>
/// <c>POST /explore/{target}/facets</c>: the fields panel's top values, over the results of the
/// chips, or over the whole index when no chip runs (ADR 0004).
/// </summary>
/// <param name="registry">Finds targets and their sources.</param>
/// <param name="memberIndexAccess">Decides who may see member data.</param>
public sealed class GetExploreFacetsController(
    IExploreSourceRegistry registry,
    IMemberIndexAccess memberIndexAccess
) : ExploreControllerBase(registry, memberIndexAccess)
{
    /// <summary>The most values per field one request returns.</summary>
    public const int MaxTake = 50;

    /// <summary>The most fields one request counts.</summary>
    public const int MaxFields = 20;

    /// <summary>Counts top values.</summary>
    /// <param name="target">The index or searcher name.</param>
    /// <param name="query">The chips, culture and fields.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>
    /// The counts; 400 for a bad request, a target without term statistics or a raw Lucene chip
    /// that does not parse, 404 for an unknown target, 403 for member data without Members access.
    /// </returns>
    [HttpPost("explore/{target}/facets")]
    [ProducesResponseType<FacetsResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetExploreFacets(
        string target,
        FacetsQuery query,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Take is < 1 or > MaxTake || (query.Fields?.Count ?? 0) > MaxFields)
        {
            return Problem400(
                "Invalid request",
                $"Take must be between 1 and {MaxTake}, and at most {MaxFields} fields."
            );
        }

        TargetLookup lookup = await FindTargetAsync(target, cancellationToken);
        if (lookup.Error is not null)
        {
            return lookup.Error;
        }

        try
        {
            return Ok(
                await lookup.Source!.GetFacetsAsync(
                    lookup.Target!,
                    query with
                    {
                        Filters = query.Filters ?? [],
                        Fields = query.Fields ?? [],
                    },
                    cancellationToken
                )
            );
        }
        catch (NotSupportedException exception)
        {
            return Problem400("Counts not available", exception.Message);
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
