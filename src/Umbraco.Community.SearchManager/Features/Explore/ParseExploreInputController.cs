using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Community.SearchManager.Core.Explore;
using Umbraco.Community.SearchManager.Features.Indexes;

namespace Umbraco.Community.SearchManager.Features.Explore;

/// <summary>The body of <c>POST /explore/{target}/parse</c>.</summary>
/// <param name="Input">What the user typed in the search box.</param>
public sealed record ParseRequest(string Input);

/// <summary>
/// <c>POST /explore/{target}/parse</c>: turns search box input into chips with the target's
/// fields, so the parser has one implementation, in Core (ADR 0002).
/// </summary>
/// <param name="registry">Finds targets and their sources.</param>
/// <param name="memberIndexAccess">Decides who may see member data.</param>
public sealed class ParseExploreInputController(
    IExploreSourceRegistry registry,
    IMemberIndexAccess memberIndexAccess
) : ExploreControllerBase(registry, memberIndexAccess)
{
    /// <summary>Parses the input.</summary>
    /// <param name="target">The index or searcher name.</param>
    /// <param name="request">The input.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The chips; 404 for an unknown target, 403 for member data without Members access.</returns>
    [HttpPost("explore/{target}/parse")]
    [ProducesResponseType<ParseResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ParseExploreInput(
        string target,
        ParseRequest request,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        TargetLookup lookup = await FindTargetAsync(target, cancellationToken);
        if (lookup.Error is not null)
        {
            return lookup.Error;
        }

        FieldSchema schema = await lookup.Source!.GetSchemaAsync(lookup.Target!, cancellationToken);
        return Ok(SimpleSyntaxParser.Parse(request.Input ?? string.Empty, schema));
    }
}
