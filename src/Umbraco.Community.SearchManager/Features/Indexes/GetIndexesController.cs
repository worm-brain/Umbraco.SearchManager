using Asp.Versioning;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Community.SearchManager.Core.Indexes;
using Umbraco.Community.SearchManager.Infrastructure.Api;

namespace Umbraco.Community.SearchManager.Features.Indexes;

/// <summary><c>GET /indexes</c>: every index the user may see, from every source.</summary>
[ApiVersion("1.0")]
[ApiExplorerSettings(GroupName = "Indexes")]
public sealed class GetIndexesController(
    IIndexSourceRegistry registry,
    IMemberIndexAccess memberIndexAccess
) : SearchManagerApiControllerBase
{
    /// <summary>Lists every index. Member indexes are left out unless the user has Members access.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The indexes.</returns>
    [HttpGet("indexes")]
    [ProducesResponseType<IReadOnlyList<IndexSummary>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetIndexes(CancellationToken cancellationToken)
    {
        IReadOnlyList<IndexSummary> indexes = await registry.GetIndexesAsync(cancellationToken);
        bool members = await memberIndexAccess.CanSeeMemberIndexesAsync(User);
        return Ok(indexes.Where(index => members || !index.IsMemberIndex).ToArray());
    }
}
