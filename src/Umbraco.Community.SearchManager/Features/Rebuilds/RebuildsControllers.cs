using Asp.Versioning;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Core.Security;
using Umbraco.Community.SearchManager.Core.Indexes;
using Umbraco.Community.SearchManager.Core.Rebuilds;
using Umbraco.Community.SearchManager.Features.Indexes;
using Umbraco.Community.SearchManager.Infrastructure.Api;

namespace Umbraco.Community.SearchManager.Features.Rebuilds;

/// <summary>The body of <c>POST /rebuilds</c>.</summary>
/// <param name="IndexNames">The indexes to rebuild.</param>
/// <param name="Mode">One at a time (the default) or all at once.</param>
public sealed record QueueRebuildsRequest(IReadOnlyList<string> IndexNames, RebuildMode Mode);

/// <summary><c>GET /rebuilds</c>: the runs the server's queue holds.</summary>
[ApiVersion("1.0")]
[ApiExplorerSettings(GroupName = "Rebuilds")]
public sealed class GetRebuildsController(IRebuildQueue queue, IMemberIndexAccess memberIndexAccess)
    : SearchManagerApiControllerBase
{
    /// <summary>Lists queued, running and finished runs, oldest first. Member index runs need Members access.</summary>
    /// <returns>The runs.</returns>
    [HttpGet("rebuilds")]
    [ProducesResponseType<IReadOnlyList<RebuildRun>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRebuilds()
    {
        bool members = await memberIndexAccess.CanSeeMemberIndexesAsync(User);
        return Ok(queue.GetRuns().Where(run => members || !run.IsMemberIndex).ToArray());
    }
}

/// <summary><c>POST /rebuilds</c>: queues rebuilds of the given indexes.</summary>
[ApiVersion("1.0")]
[ApiExplorerSettings(GroupName = "Rebuilds")]
public sealed class QueueRebuildsController(
    IIndexSourceRegistry registry,
    IRebuildQueue queue,
    IMemberIndexAccess memberIndexAccess,
    IBackOfficeSecurityAccessor backOfficeSecurityAccessor
) : SearchManagerApiControllerBase
{
    /// <summary>
    /// Queues a rebuild of each index. Indexes that are unknown, cannot be rebuilt, are member
    /// indexes the user may not rebuild, or already have a queued or running run are refused with
    /// the reason; the rest are queued.
    /// </summary>
    /// <param name="request">The indexes and the mode.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The queued runs and the refusals.</returns>
    [HttpPost("rebuilds")]
    [ProducesResponseType<EnqueueResult>(StatusCodes.Status200OK)]
    public async Task<IActionResult> QueueRebuilds(
        QueueRebuildsRequest request,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(request);

        IReadOnlyList<IndexSummary> indexes = await registry.GetIndexesAsync(cancellationToken);
        bool members = await memberIndexAccess.CanSeeMemberIndexesAsync(User);
        (IReadOnlyList<RebuildRequest> requests, IReadOnlyList<RebuildRefusal> refused) =
            RebuildPlanner.Plan(indexes, request.IndexNames, members, PreviousFinalCount);

        EnqueueResult result = queue.Enqueue(requests, request.Mode, TriggeredBy());
        return Ok(result with { Refused = [.. refused, .. result.Refused] });
    }

    /// <summary>
    /// The final document count of the index's latest successful run, for the progress estimate;
    /// null (so the planner falls back to the current count) when it has not been rebuilt yet.
    /// </summary>
    /// <param name="index">The index.</param>
    /// <returns>The count, or null.</returns>
    private long? PreviousFinalCount(IndexSummary index) =>
        queue
            .GetRuns()
            .LastOrDefault(run =>
                run.Status is RebuildStatus.Succeeded
                && string.Equals(run.IndexName, index.Name, StringComparison.OrdinalIgnoreCase)
            )
            ?.DocumentsAfter;

    /// <summary>Names the signed-in user for the run's history.</summary>
    /// <returns>The user's name, or a generic label.</returns>
    private string TriggeredBy() =>
        backOfficeSecurityAccessor.BackOfficeSecurity?.CurrentUser?.Name
        ?? User.Identity?.Name
        ?? "A backoffice user";
}

/// <summary><c>POST /rebuilds/{id}/cancel</c>: cancels a queued run.</summary>
[ApiVersion("1.0")]
[ApiExplorerSettings(GroupName = "Rebuilds")]
public sealed class CancelRebuildController(
    IRebuildQueue queue,
    IMemberIndexAccess memberIndexAccess
) : SearchManagerApiControllerBase
{
    /// <summary>Cancels a queued run. A run that has started cannot be cancelled.</summary>
    /// <param name="id">The run.</param>
    /// <returns>200, 404 when there is no such run, 409 when it has already started.</returns>
    [HttpPost("rebuilds/{id:guid}/cancel")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CancelRebuild(Guid id)
    {
        RebuildRun? run = queue.GetRuns().FirstOrDefault(candidate => candidate.Id == id);
        if (
            run is null
            || (run.IsMemberIndex && !await memberIndexAccess.CanSeeMemberIndexesAsync(User))
        )
        {
            return NotFound(new ProblemDetails { Title = "There is no rebuild run with this id." });
        }

        return queue.Cancel(id) switch
        {
            CancelResult.Cancelled => Ok(),
            CancelResult.NotFound => NotFound(
                new ProblemDetails { Title = "There is no rebuild run with this id." }
            ),
            _ => Conflict(
                new ProblemDetails
                {
                    Title = "The rebuild has already started, so it can no longer be cancelled.",
                }
            ),
        };
    }
}

/// <summary><c>DELETE /rebuilds/finished</c>: clears finished runs from the Rebuilds panel.</summary>
[ApiVersion("1.0")]
[ApiExplorerSettings(GroupName = "Rebuilds")]
public sealed class ClearFinishedRebuildsController(IRebuildQueue queue)
    : SearchManagerApiControllerBase
{
    /// <summary>Forgets every finished run.</summary>
    /// <returns>200.</returns>
    [HttpDelete("rebuilds/finished")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult ClearFinishedRebuilds()
    {
        queue.ClearFinished();
        return Ok();
    }
}
