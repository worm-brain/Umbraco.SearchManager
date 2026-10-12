using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Community.SearchManager.Core.Explore;
using Umbraco.Community.SearchManager.Features.Indexes;

namespace Umbraco.Community.SearchManager.Features.Explore;

/// <summary><c>GET /explore/targets</c>: the indexes and searchers the Explore view can search.</summary>
/// <param name="registry">Lists targets from every source.</param>
/// <param name="memberIndexAccess">Decides who may see member data.</param>
public sealed class GetExploreTargetsController(
    IExploreSourceRegistry registry,
    IMemberIndexAccess memberIndexAccess
) : ExploreControllerBase(registry, memberIndexAccess)
{
    /// <summary>Lists every target. Member data is left out unless the user has Members access.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The targets.</returns>
    [HttpGet("explore/targets")]
    [ProducesResponseType<IReadOnlyList<ExploreTarget>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetExploreTargets(CancellationToken cancellationToken)
    {
        IReadOnlyList<ExploreTarget> targets = await Registry.GetTargetsAsync(cancellationToken);
        bool members = await CanSeeMembersAsync();
        return Ok(targets.Where(target => members || !target.IsMemberData).ToArray());
    }
}
