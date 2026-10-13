using Examine;
using Examine.Lucene.Providers;
using Umbraco.Community.SearchManager.Core.Explore;

namespace Umbraco.Community.SearchManager.Features.Explore;

/// <summary>
/// Resolves a core Examine target (an index or a searcher registered on its own) to the Examine
/// objects behind it. Shared by Explore and Diagnose, so both read a target the same way.
/// </summary>
/// <param name="examineManager">Examine's indexes and searchers.</param>
internal sealed class ExamineTargets(IExamineManager examineManager)
{
    /// <summary>The searcher behind a target.</summary>
    /// <param name="target">A core Examine target.</param>
    /// <returns>The index's own searcher, or the registered searcher.</returns>
    /// <exception cref="InvalidOperationException">The index or searcher has gone.</exception>
    public ISearcher SearcherOf(ExploreTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (
            target.Kind == ExploreTargetKind.Index
            && examineManager.TryGetIndex(target.Name, out IIndex? index)
        )
        {
            return index.Searcher;
        }

        if (examineManager.TryGetSearcher(target.Name, out ISearcher? searcher))
        {
            return searcher;
        }

        throw new InvalidOperationException($"Examine has no index or searcher {target.Name}");
    }

    /// <summary>The index behind an index target.</summary>
    /// <param name="target">A core Examine target.</param>
    /// <returns>The index, or null for a searcher target or an index that has gone.</returns>
    public IIndex? IndexOf(ExploreTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return
            target.Kind == ExploreTargetKind.Index
            && examineManager.TryGetIndex(target.Name, out IIndex? index)
            ? index
            : null;
    }

    /// <summary>The Lucene indexes a searcher reads: its own index, or a multi-index searcher's.</summary>
    /// <param name="searcher">The searcher.</param>
    /// <returns>The indexes, in <see cref="IExamineManager"/> order.</returns>
    public LuceneIndex[] IndexesBehind(ISearcher searcher)
    {
        ISearcher[] searchers = searcher is MultiIndexSearcher multi
            ? SearchersOf(multi)
            : [searcher];
        return
        [
            .. examineManager
                .Indexes.OfType<LuceneIndex>()
                .Where(index =>
                    searchers.Any(candidate => ReferenceEquals(candidate, index.Searcher))
                ),
        ];
    }

    /// <summary>The searchers a multi-index searcher combines.</summary>
    /// <param name="multi">The multi-index searcher.</param>
    /// <returns>Its searchers.</returns>
    /// <remarks>
    /// Read by name: the property's type differs between Examine 3 and Examine 4 (which Umbraco
    /// Search brings), so a compiled call binds to one signature and throws
    /// <see cref="MissingMethodException"/> under the other.
    /// </remarks>
    public static ISearcher[] SearchersOf(MultiIndexSearcher multi) =>
        typeof(MultiIndexSearcher)
            .GetProperty(nameof(MultiIndexSearcher.Searchers))
            ?.GetValue(multi)
            is System.Collections.IEnumerable searchers
            ? [.. searchers.OfType<ISearcher>()]
            : [];
}
