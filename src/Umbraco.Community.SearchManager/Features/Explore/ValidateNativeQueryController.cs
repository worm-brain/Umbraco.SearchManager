using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Community.SearchManager.Core.Explore;
using Umbraco.Community.SearchManager.Features.Indexes;

namespace Umbraco.Community.SearchManager.Features.Explore;

/// <summary>
/// <c>POST /explore/{target}/validate</c>: checks raw Lucene with the parser Examine's
/// <c>NativeQuery</c> uses, with the error position, and lints it (ADR 0005).
/// </summary>
/// <param name="registry">Finds targets and their sources.</param>
/// <param name="memberIndexAccess">Decides who may see member data.</param>
public sealed class ValidateNativeQueryController(
    IExploreSourceRegistry registry,
    IMemberIndexAccess memberIndexAccess
) : ExploreControllerBase(registry, memberIndexAccess)
{
    /// <summary>Validates a raw query.</summary>
    /// <param name="target">The index or searcher name.</param>
    /// <param name="request">The raw query.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>
    /// The verdict; 400 for a target without native queries, 404 for an unknown target, 403
    /// for member data without Members access.
    /// </returns>
    [HttpPost("explore/{target}/validate")]
    [ProducesResponseType<NativeValidation>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ValidateNativeQuery(
        string target,
        NativeRequest request,
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
                await lookup.Source!.ValidateNativeAsync(
                    lookup.Target!,
                    request.Query ?? string.Empty,
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
