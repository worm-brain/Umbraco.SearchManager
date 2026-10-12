using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Community.SearchManager.Core.Explore;
using Umbraco.Community.SearchManager.Features.Indexes;

namespace Umbraco.Community.SearchManager.Features.Explore;

/// <summary>
/// <c>GET /explore/{target}/terms</c>: a field's most common values starting with what the user
/// typed, for the search box's value suggestions (ADR 0004).
/// </summary>
/// <param name="registry">Finds targets and their sources.</param>
/// <param name="memberIndexAccess">Decides who may see member data.</param>
public sealed class GetExploreTermsController(
    IExploreSourceRegistry registry,
    IMemberIndexAccess memberIndexAccess
) : ExploreControllerBase(registry, memberIndexAccess)
{
    /// <summary>The most values one request returns.</summary>
    public const int MaxTake = 50;

    /// <summary>Reads a field's values.</summary>
    /// <param name="target">The index or searcher name.</param>
    /// <param name="field">The field or alias.</param>
    /// <param name="prefix">What the user typed after the colon.</param>
    /// <param name="take">How many values, 1 to 50.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>
    /// The values; 400 for a bad take or a target without term statistics, 404 for an unknown
    /// target or field, 403 for member data without Members access.
    /// </returns>
    [HttpGet("explore/{target}/terms")]
    [ProducesResponseType<TermsResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetExploreTerms(
        string target,
        [FromQuery] string field,
        [FromQuery] string? prefix,
        CancellationToken cancellationToken,
        [FromQuery] int take = 10
    )
    {
        if (take is < 1 or > MaxTake)
        {
            return Problem400("Invalid take", $"Take must be between 1 and {MaxTake}.");
        }

        TargetLookup lookup = await FindTargetAsync(target, cancellationToken);
        if (lookup.Error is not null)
        {
            return lookup.Error;
        }

        try
        {
            TermsResult? terms = await lookup.Source!.GetTermsAsync(
                lookup.Target!,
                field,
                prefix ?? string.Empty,
                take,
                cancellationToken
            );
            return terms is null
                ? NotFound(
                    new ProblemDetails
                    {
                        Title = "Field not found",
                        Detail = $"{target} has no field {field}.",
                        Status = StatusCodes.Status404NotFound,
                    }
                )
                : Ok(terms);
        }
        catch (NotSupportedException exception)
        {
            return Problem400("Terms not available", exception.Message);
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
