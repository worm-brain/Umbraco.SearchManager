using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Community.SearchManager.Core.Diagnose;
using Umbraco.Community.SearchManager.Core.Explore;
using Umbraco.Community.SearchManager.Features.Explore;
using Umbraco.Community.SearchManager.Features.Indexes;

namespace Umbraco.Community.SearchManager.Features.Diagnose.Fields;

/// <summary>
/// <c>GET /diagnose/{target}/fields</c>: the statistics of every field of an index (ADR 0018).
/// Targets are Explore's, so a missing index is a 404 and member data needs Members access.
/// </summary>
/// <param name="registry">Finds targets.</param>
/// <param name="memberIndexAccess">Decides who may see member data.</param>
/// <param name="sources">The field statistics source of each engine.</param>
[ApiExplorerSettings(GroupName = "Diagnose")]
public sealed class GetFieldStatisticsController(
    IExploreSourceRegistry registry,
    IMemberIndexAccess memberIndexAccess,
    IEnumerable<IFieldStatisticsSource> sources
) : ExploreControllerBase(registry, memberIndexAccess)
{
    private readonly IFieldStatisticsSource[] _sources = [.. sources];

    /// <summary>Reads the field statistics of an index.</summary>
    /// <param name="target">The index name.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>
    /// The statistics, or a result with <c>available: false</c> and the reason when the index has
    /// none; 404 for an unknown index, 403 for member data without Members access.
    /// </returns>
    [HttpGet("diagnose/{target}/fields")]
    [ProducesResponseType<FieldStatisticsResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetFieldStatistics(
        string target,
        CancellationToken cancellationToken
    )
    {
        TargetLookup lookup = await FindTargetAsync(target, cancellationToken);
        if (lookup.Error is not null)
        {
            return lookup.Error;
        }

        ExploreTarget found = lookup.Target!;
        IFieldStatisticsSource? source = _sources.FirstOrDefault(candidate =>
            candidate.Engine == found.Engine
        );
        return Ok(
            source is null
                ? FieldStatisticsResult.Unavailable(
                    found,
                    $"Field statistics are not available for {found.Engine} indexes."
                )
                : await source.GetFieldStatisticsAsync(found, cancellationToken)
        );
    }
}
