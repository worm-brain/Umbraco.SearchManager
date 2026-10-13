using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Community.SearchManager.Core.Explore;
using Umbraco.Community.SearchManager.Features.Indexes;

namespace Umbraco.Community.SearchManager.Features.Explore;

/// <summary>
/// <c>GET /explore/{target}/fields</c>: a target's fields, aliases and operators, and the site's
/// cultures with the one the picker starts on, for the chips, the chip editor and the column picker.
/// </summary>
/// <param name="registry">Finds targets and their sources.</param>
/// <param name="memberIndexAccess">Decides who may see member data.</param>
public sealed class GetExploreSchemaController(
    IExploreSourceRegistry registry,
    IMemberIndexAccess memberIndexAccess
) : ExploreControllerBase(registry, memberIndexAccess)
{
    /// <summary>Reads a target's schema.</summary>
    /// <param name="target">The index or searcher name.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The schema; 404 for an unknown target, 403 for member data without Members access.</returns>
    [HttpGet("explore/{target}/fields")]
    [ProducesResponseType<ExploreSchema>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetExploreSchema(
        string target,
        CancellationToken cancellationToken
    )
    {
        TargetLookup lookup = await FindTargetAsync(target, cancellationToken);
        if (lookup.Error is not null)
        {
            return lookup.Error;
        }

        FieldSchema schema = await lookup.Source!.GetSchemaAsync(lookup.Target!, cancellationToken);
        IReadOnlyList<string> cultures = await lookup.Source.GetCulturesAsync(cancellationToken);
        string? defaultCulture = await lookup.Source.GetDefaultCultureAsync(cancellationToken);
        return Ok(
            new ExploreSchema(
                schema.Fields,
                schema.Aliases,
                lookup.Source.Operators,
                cultures,
                schema.DocumentCount,
                lookup.Source.Unsupported,
                defaultCulture
            )
        );
    }
}
