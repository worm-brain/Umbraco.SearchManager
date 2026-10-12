using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Community.SearchManager.Core.Explore;
using Umbraco.Community.SearchManager.Features.Indexes;

namespace Umbraco.Community.SearchManager.Features.Explore;

/// <summary>
/// <c>POST /explore/{target}/compile</c>: the query a search would run, for the show-query
/// panel: as sent, as the parser read it, and as C# (ADR 0005).
/// </summary>
/// <param name="registry">Finds targets and their sources.</param>
/// <param name="memberIndexAccess">Decides who may see member data.</param>
public sealed class CompileExploreController(
    IExploreSourceRegistry registry,
    IMemberIndexAccess memberIndexAccess
) : ExploreControllerBase(registry, memberIndexAccess)
{
    /// <summary>Compiles the chips, or shows the raw query in native mode.</summary>
    /// <param name="target">The index or searcher name.</param>
    /// <param name="request">The chips, culture, or raw query.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>
    /// The query; 400 for a raw query on a target without native queries, 404 for an unknown
    /// target, 403 for member data without Members access.
    /// </returns>
    [HttpPost("explore/{target}/compile")]
    [ProducesResponseType<CompiledQuery>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CompileExplore(
        string target,
        CompileRequest request,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        TargetLookup lookup = await FindTargetAsync(target, cancellationToken);
        if (lookup.Error is not null)
        {
            return lookup.Error;
        }

        try
        {
            return Ok(
                await lookup.Source!.CompileAsync(
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
            return BadRequest(
                new ProblemDetails
                {
                    Title = "Native queries not available",
                    Detail = exception.Message,
                    Status = StatusCodes.Status400BadRequest,
                }
            );
        }
    }
}
