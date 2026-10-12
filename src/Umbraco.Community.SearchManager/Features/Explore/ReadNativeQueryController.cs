using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Community.SearchManager.Core.Explore;
using Umbraco.Community.SearchManager.Features.Indexes;

namespace Umbraco.Community.SearchManager.Features.Explore;

/// <summary>
/// <c>POST /explore/{target}/read-native</c>: turns raw Lucene back into chips when the user
/// leaves native mode; what has no chip form stays as a raw Lucene chip (ADR 0005).
/// </summary>
/// <param name="registry">Finds targets and their sources.</param>
/// <param name="memberIndexAccess">Decides who may see member data.</param>
public sealed class ReadNativeQueryController(
    IExploreSourceRegistry registry,
    IMemberIndexAccess memberIndexAccess
) : ExploreControllerBase(registry, memberIndexAccess)
{
    /// <summary>Reads a raw query as chips.</summary>
    /// <param name="target">The index or searcher name.</param>
    /// <param name="request">The raw query.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>
    /// The chips, in the parse result's shape; 400 for a target without native queries, 404 for
    /// an unknown target, 403 for member data without Members access.
    /// </returns>
    [HttpPost("explore/{target}/read-native")]
    [ProducesResponseType<ParseResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ReadNativeQuery(
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
            IReadOnlyList<Filter> filters = await lookup.Source!.ReadNativeAsync(
                lookup.Target!,
                request.Query ?? string.Empty,
                cancellationToken
            );
            return Ok(new ParseResult(filters, null));
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
