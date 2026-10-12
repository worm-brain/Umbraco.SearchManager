using Asp.Versioning;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Community.SearchManager.Core.Rebuilds;
using Umbraco.Community.SearchManager.Features.Indexes;
using Umbraco.Community.SearchManager.Infrastructure.Api;

namespace Umbraco.Community.SearchManager.Features.Rebuilds.History;

/// <summary><c>GET /rebuilds/history</c>: an index's recorded rebuilds, for its details panel.</summary>
[ApiVersion("1.0")]
[ApiExplorerSettings(GroupName = "Rebuilds")]
public sealed class GetRebuildHistoryController(
    IRebuildHistory history,
    IMemberIndexAccess memberIndexAccess
) : SearchManagerApiControllerBase
{
    /// <summary>
    /// Lists the index's last 20 rebuilds, newest first. A member index's history needs Members
    /// section access; without it the list is empty.
    /// </summary>
    /// <param name="indexName">The index.</param>
    /// <returns>The entries.</returns>
    [HttpGet("rebuilds/history")]
    [ProducesResponseType<IReadOnlyList<RebuildHistoryEntry>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRebuildHistory([FromQuery] string indexName)
    {
        IReadOnlyList<RebuildHistoryEntry> entries = history.GetRecent(indexName);
        bool members = await memberIndexAccess.CanSeeMemberIndexesAsync(User);
        return Ok(entries.Where(entry => members || !entry.IsMemberIndex).ToArray());
    }
}

/// <summary><c>GET /rebuilds/history/latest</c>: each index's latest rebuild, for "Last rebuilt".</summary>
[ApiVersion("1.0")]
[ApiExplorerSettings(GroupName = "Rebuilds")]
public sealed class GetLatestRebuildsController(
    IRebuildHistory history,
    IMemberIndexAccess memberIndexAccess
) : SearchManagerApiControllerBase
{
    /// <summary>Lists the newest recorded rebuild of each index the user may see.</summary>
    /// <returns>One entry per index.</returns>
    [HttpGet("rebuilds/history/latest")]
    [ProducesResponseType<IReadOnlyList<RebuildHistoryEntry>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetLatestRebuilds()
    {
        IReadOnlyList<RebuildHistoryEntry> entries = history.GetLatestPerIndex();
        bool members = await memberIndexAccess.CanSeeMemberIndexesAsync(User);
        return Ok(entries.Where(entry => members || !entry.IsMemberIndex).ToArray());
    }
}
