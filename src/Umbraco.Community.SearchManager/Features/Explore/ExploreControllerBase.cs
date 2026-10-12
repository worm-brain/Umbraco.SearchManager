using Asp.Versioning;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Community.SearchManager.Core.Explore;
using Umbraco.Community.SearchManager.Features.Indexes;
using Umbraco.Community.SearchManager.Infrastructure.Api;

namespace Umbraco.Community.SearchManager.Features.Explore;

/// <summary>
/// Base for the Explore endpoints: finds the target named in the route and refuses member data
/// to users without Members section access (GHSA-w5q3-9wf8-43gg), on top of the Settings access
/// every endpoint requires.
/// </summary>
/// <param name="registry">Finds targets and their sources.</param>
/// <param name="memberIndexAccess">Decides who may see member data.</param>
[ApiVersion("1.0")]
[ApiExplorerSettings(GroupName = "Explore")]
public abstract class ExploreControllerBase(
    IExploreSourceRegistry registry,
    IMemberIndexAccess memberIndexAccess
) : SearchManagerApiControllerBase
{
    /// <summary>The target registry.</summary>
    protected IExploreSourceRegistry Registry { get; } = registry;

    /// <summary>Whether the current user may see member indexes and searchers.</summary>
    /// <returns><see langword="true"/> with Members section access.</returns>
    protected Task<bool> CanSeeMembersAsync() => memberIndexAccess.CanSeeMemberIndexesAsync(User);

    /// <summary>
    /// Finds the target the request names and checks the user may search it.
    /// </summary>
    /// <param name="name">The target name from the route.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>
    /// The target and its source, or the response to send instead: 404 when no source lists it,
    /// 403 for member data without Members access.
    /// </returns>
    protected async Task<TargetLookup> FindTargetAsync(
        string name,
        CancellationToken cancellationToken
    )
    {
        (ExploreTarget Target, IExploreSource Source)? found = await Registry.FindAsync(
            name,
            cancellationToken
        );
        if (found is null)
        {
            return new TargetLookup(
                null,
                null,
                NotFound(
                    new ProblemDetails
                    {
                        Title = "Index not found",
                        Detail = $"No index or searcher is called {name}.",
                        Status = StatusCodes.Status404NotFound,
                    }
                )
            );
        }

        if (found.Value.Target.IsMemberData && !await CanSeeMembersAsync())
        {
            return new TargetLookup(
                null,
                null,
                StatusCode(
                    StatusCodes.Status403Forbidden,
                    new ProblemDetails
                    {
                        Title = "Members access required",
                        Detail =
                            $"{name} holds member data, which needs access to the Members section.",
                        Status = StatusCodes.Status403Forbidden,
                    }
                )
            );
        }

        return new TargetLookup(found.Value.Target, found.Value.Source, null);
    }

    /// <summary>The outcome of <see cref="FindTargetAsync"/>.</summary>
    /// <param name="Target">The target, when found and allowed.</param>
    /// <param name="Source">Its source, when found and allowed.</param>
    /// <param name="Error">The response to send instead, otherwise.</param>
    protected sealed record TargetLookup(
        ExploreTarget? Target,
        IExploreSource? Source,
        IActionResult? Error
    );
}
