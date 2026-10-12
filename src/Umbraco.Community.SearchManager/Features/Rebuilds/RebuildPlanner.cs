using Umbraco.Community.SearchManager.Core.Indexes;
using Umbraco.Community.SearchManager.Core.Rebuilds;

namespace Umbraco.Community.SearchManager.Features.Rebuilds;

/// <summary>
/// Turns the index names a user asked to rebuild into queue requests, refusing the ones they may
/// not or cannot rebuild: unknown indexes, indexes without the rebuild capability, and member
/// indexes for a user without Members section access.
/// </summary>
internal static class RebuildPlanner
{
    /// <summary>Plans the rebuild of <paramref name="indexNames"/>.</summary>
    /// <param name="indexes">Every index, as the registry lists them.</param>
    /// <param name="indexNames">What the user asked for (duplicates are ignored).</param>
    /// <param name="canSeeMemberIndexes">Whether the user has Members section access.</param>
    /// <param name="estimate">
    /// The document count a run of an index is expected to reach, when known (the previous run's
    /// final count); falls back to the index's current count.
    /// </param>
    /// <returns>The requests to queue and the refusals.</returns>
    public static (
        IReadOnlyList<RebuildRequest> Requests,
        IReadOnlyList<RebuildRefusal> Refused
    ) Plan(
        IReadOnlyList<IndexSummary> indexes,
        IEnumerable<string> indexNames,
        bool canSeeMemberIndexes,
        Func<IndexSummary, long?>? estimate = null
    )
    {
        var requests = new List<RebuildRequest>();
        var refused = new List<RebuildRefusal>();
        foreach (string name in indexNames.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            IndexSummary? index = indexes.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase)
            );

            // The Indexes view never shows a member index to a user without Members access, so
            // this refusal only answers a hand-made request, and says what is missing.
            if (index is null || (index.IsMemberIndex && !canSeeMemberIndexes))
            {
                refused.Add(
                    new RebuildRefusal(
                        name,
                        index is null
                            ? "There is no index with this name."
                            : "Rebuilding a member index needs access to the Members section."
                    )
                );
                continue;
            }

            if (!index.Capabilities.Rebuild)
            {
                refused.Add(
                    new RebuildRefusal(
                        index.Name,
                        index.RebuildUnavailableReason ?? "This index cannot be rebuilt."
                    )
                );
                continue;
            }

            requests.Add(
                new RebuildRequest(
                    index.Name,
                    index.Engine,
                    index.DocumentCount,
                    estimate?.Invoke(index) ?? index.DocumentCount,
                    index.ZeroDowntime,
                    index.IsMemberIndex
                )
            );
        }

        return (requests, refused);
    }
}
