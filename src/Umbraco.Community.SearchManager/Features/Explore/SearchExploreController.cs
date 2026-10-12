using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Community.SearchManager.Core.Explore;
using Umbraco.Community.SearchManager.Features.Indexes;

namespace Umbraco.Community.SearchManager.Features.Explore;

/// <summary>
/// <c>POST /explore/{target}/search</c>: runs the chips against an index or searcher. The source
/// compiles them to its own language; chips it cannot run come back in <c>skipped</c>.
/// </summary>
/// <param name="registry">Finds targets and their sources.</param>
/// <param name="memberIndexAccess">Decides who may see member data.</param>
public sealed class SearchExploreController(
    IExploreSourceRegistry registry,
    IMemberIndexAccess memberIndexAccess
) : ExploreControllerBase(registry, memberIndexAccess)
{
    /// <summary>The largest page the endpoint returns.</summary>
    public const int MaxTake = 100;

    /// <summary>Runs a search.</summary>
    /// <param name="target">The index or searcher name.</param>
    /// <param name="query">The chips, culture and page.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>
    /// The results; 400 for a bad page or a raw Lucene chip that does not parse, 404 for an
    /// unknown target, 403 for member data without Members access.
    /// </returns>
    [HttpPost("explore/{target}/search")]
    [ProducesResponseType<ExploreResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SearchExplore(
        string target,
        ExploreQuery query,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Skip < 0 || query.Take is < 1 or > MaxTake)
        {
            return BadRequest(
                new ProblemDetails
                {
                    Title = "Invalid page",
                    Detail = $"Skip must be 0 or more and take between 1 and {MaxTake}.",
                    Status = StatusCodes.Status400BadRequest,
                }
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
                await lookup.Source!.SearchAsync(
                    lookup.Target!,
                    query with
                    {
                        Filters = query.Filters ?? [],
                    },
                    cancellationToken
                )
            );
        }
        catch (InvalidNativeQueryException exception)
        {
            return BadRequest(
                new ProblemDetails
                {
                    Title = "Invalid Lucene query",
                    Detail = exception.Message,
                    Status = StatusCodes.Status400BadRequest,
                }
            );
        }
    }
}
