using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Community.SearchManager.Core.Diagnose;
using Umbraco.Community.SearchManager.Core.Explore;
using Umbraco.Community.SearchManager.Features.Explore;
using Umbraco.Community.SearchManager.Features.Indexes;

namespace Umbraco.Community.SearchManager.Features.Diagnose.Analysis;

/// <summary>
/// <c>GET /diagnose/{target}/analyzer/fields</c>: the fields of an index whose analyzer can be run
/// (ADR 0023). Targets are Explore's, so a missing index is a 404 and member data needs Members access.
/// </summary>
/// <param name="registry">Finds targets.</param>
/// <param name="memberIndexAccess">Decides who may see member data.</param>
/// <param name="sources">The analyzer source of each engine.</param>
[ApiExplorerSettings(GroupName = "Diagnose")]
public sealed class GetAnalyzerFieldsController(
    IExploreSourceRegistry registry,
    IMemberIndexAccess memberIndexAccess,
    IEnumerable<IAnalyzerSource> sources
) : ExploreControllerBase(registry, memberIndexAccess)
{
    private readonly IAnalyzerSource[] _sources = [.. sources];

    /// <summary>Lists the fields of an index whose analyzer can be run.</summary>
    /// <param name="target">The index name.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>
    /// The fields, or a result with <c>available: false</c> and the reason; 404 for an unknown
    /// index, 403 for member data without Members access.
    /// </returns>
    [HttpGet("diagnose/{target}/analyzer/fields")]
    [ProducesResponseType<AnalyzerFieldsResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAnalyzerFields(
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
        IAnalyzerSource? source = AnalyzerSources.For(_sources, found);
        return Ok(
            source is null
                ? AnalyzerFieldsResult.Unavailable(found, AnalyzerSources.NoSource(found))
                : await source.GetFieldsAsync(found, cancellationToken)
        );
    }
}

/// <summary>
/// <c>POST /diagnose/{target}/analyzer</c>: runs text through a field's analyzer stage by stage
/// and judges a search term with the engine's own query (ADR 0023).
/// </summary>
/// <param name="registry">Finds targets.</param>
/// <param name="memberIndexAccess">Decides who may see member data.</param>
/// <param name="sources">The analyzer source of each engine.</param>
[ApiExplorerSettings(GroupName = "Diagnose")]
public sealed class AnalyzeTextController(
    IExploreSourceRegistry registry,
    IMemberIndexAccess memberIndexAccess,
    IEnumerable<IAnalyzerSource> sources
) : ExploreControllerBase(registry, memberIndexAccess)
{
    /// <summary>The longest text analysed.</summary>
    public const int MaxTextLength = 2_000;

    /// <summary>The longest search term judged.</summary>
    public const int MaxSearchTermLength = 200;

    private readonly IAnalyzerSource[] _sources = [.. sources];

    /// <summary>Analyses text for one field of an index.</summary>
    /// <param name="target">The index name.</param>
    /// <param name="request">The field, text and search term.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>
    /// The analysis, or a result with <c>available: false</c> and the reason (such as a number
    /// field); 400 for a missing field, empty or too long text or too long search term, 404 for
    /// an unknown index, 403 for member data without Members access.
    /// </returns>
    [HttpPost("diagnose/{target}/analyzer")]
    [ProducesResponseType<AnalysisResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Analyze(
        string target,
        AnalyzeRequest request,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        if (Invalid(request) is { } detail)
        {
            return BadRequest(
                new ProblemDetails
                {
                    Title = "Invalid analysis request",
                    Detail = detail,
                    Status = StatusCodes.Status400BadRequest,
                }
            );
        }

        TargetLookup lookup = await FindTargetAsync(target, cancellationToken);
        if (lookup.Error is not null)
        {
            return lookup.Error;
        }

        ExploreTarget found = lookup.Target!;
        IAnalyzerSource? source = AnalyzerSources.For(_sources, found);
        return Ok(
            source is null
                ? AnalysisResult.Unavailable(found, request.Field, AnalyzerSources.NoSource(found))
                : await source.AnalyzeAsync(found, request, cancellationToken)
        );
    }

    /// <summary>What is wrong with a request, or null when nothing is.</summary>
    /// <param name="request">The request.</param>
    /// <returns>A sentence for the problem details.</returns>
    internal static string? Invalid(AnalyzeRequest request) =>
        string.IsNullOrWhiteSpace(request.Field) ? "Pick a field to analyse."
        : string.IsNullOrWhiteSpace(request.Text) ? "Enter some text to analyse."
        : request.Text.Length > MaxTextLength
            ? $"The text can be at most {MaxTextLength} characters."
        : request.SearchTerm?.Length > MaxSearchTermLength
            ? $"The search can be at most {MaxSearchTermLength} characters."
        : null;
}

/// <summary>Picks the analyzer source of a target's engine.</summary>
internal static class AnalyzerSources
{
    /// <summary>The source for a target's engine.</summary>
    /// <param name="sources">Every registered source.</param>
    /// <param name="target">The target.</param>
    /// <returns>The source, or null when the engine has none.</returns>
    public static IAnalyzerSource? For(IAnalyzerSource[] sources, ExploreTarget target) =>
        sources.FirstOrDefault(candidate => candidate.Engine == target.Engine);

    /// <summary>Why an engine without a source has no analyzer view.</summary>
    /// <param name="target">The target.</param>
    /// <returns>The reason.</returns>
    public static string NoSource(ExploreTarget target) =>
        $"The analyzer is not available for {target.Engine} indexes.";
}
